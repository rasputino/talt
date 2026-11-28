using Talt.Core.Construction;
using Talt.Core.Finance;
using Talt.Core.Roads;
using Talt.Core.Stations;
using Talt.Core.Vehicles;

namespace Talt.Core.Rails;

/// <summary>
/// The sequential phase (research R5, R7–R12), visited in ascending train id: tail release, reservations, stops and loading,
/// depots, routing and stuck checks.
/// </summary>
public sealed partial class TrainSystem
{
    /// <summary>Most steps reserved at once when no signal ends the segment earlier.</summary>
    private const int MaxSegmentSteps = 96;

    private int[] _blockOwned = new int[64];

    private void Sequential(Train train)
    {
        switch (train.Mode)
        {
            case TrainMode.InDepot:
                TryLeaveDepot(train);
                return;
            case TrainMode.Loading:
                LoadStep(train);
                return;
        }

        ReleaseTail(train);
        if (train.BreakdownTicks > 0)
        {
            train.BreakdownTicks--;
        }

        if ((train.Flags & TrainBits.Started) == 0)
        {
            return;
        }

        var pulling = (train.Flags & TrainBits.PullingForward) != 0;
        var wantsRoute = (train.Flags & TrainBits.NeedsRoute) != 0 || (train.Route.Count == 0 && train.TargetKind != RouteGoalKind.None);
        if (!pulling && wantsRoute && ((train.Flags & TrainBits.NoRoute) == 0 || _tick - train.LastPlanTick >= Rules.ReplanTicks * 10))
        {
            PlanRoute(train, allowReverse: train.Speed == 0);
        }

        var halted = train.Speed == 0 && AtAuthorityEnd(train);
        if (halted && train.RoutePos >= train.Route.Count && (train.Flags & TrainBits.NeedsRoute) == 0)
        {
            Arrive(train);
            return;
        }

        if ((train.Flags & TrainBits.PullingForward) == 0 && NeedsMoreTrack(train))
        {
            TryExtend(train);
        }

        if (halted)
        {
            train.WaitTicks++;
            if (train.WaitTicks > Rules.StuckNoticeDays * Simulation.GameClock.TicksPerDay && (train.Flags & TrainBits.StuckNotified) == 0)
            {
                train.Flags |= TrainBits.StuckNotified;
                Notify(VehicleNoticeKind.Stuck, train, -1);
            }
        }
        else
        {
            train.WaitTicks = 0;
            train.Flags &= ~(TrainBits.StuckNotified | TrainBits.Waiting);
        }
    }

    private static bool AtAuthorityEnd(Train train) =>
        train.StepCount > 0 && train.HeadStep == train.StepCount - 1 && train.HeadOffset >= train.StepLength(train.HeadStep);

    /// <summary>True when the head is within braking distance (plus look-ahead) of the end of its authority.</summary>
    private bool NeedsMoreTrack(Train train)
    {
        if (train.RoutePos >= train.Route.Count)
        {
            return false;
        }

        long ahead = train.StepCount == 0 ? 0 : train.StepLength(train.HeadStep) - train.HeadOffset;
        for (var i = train.HeadStep + 1; i < train.StepCount; i++)
        {
            ahead += train.StepLength(i);
        }

        var v = (long)train.Speed;
        var stop = (v * v) / (2L * Rules.Braking * RailRules.SpeedScale);
        return ahead <= stop + Rules.LookAhead + (2 * Math.Max(train.MaxSpeed, 1));
    }

    // ----- Releasing -----

    /// <summary>Releases the tail steps the body has fully left (conservatively: a whole step behind the tail).</summary>
    private void ReleaseTail(Train train)
    {
        if (train.StepCount == 0)
        {
            return;
        }

        var tail = train.HeadPosition - train.Length;
        while (train.StepCount > 1 && train.HeadStep > 0 && tail >= train.StepLength(0))
        {
            tail -= train.StepLength(0);
            Release(train.StepFrom(0));
            train.PopStep();
        }
    }

    private void Release(int tile)
    {
        _owner[tile] = 0;
        var block = SignalBlocks.BlockAt(tile);
        if (block >= 0 && block < _blockOwned.Length)
        {
            _blockOwned[block]--;
        }
    }

    private void Own(int tile, int trainId)
    {
        _owner[tile] = trainId;
        var block = SignalBlocks.BlockAt(tile);
        if (block >= 0)
        {
            if (block >= _blockOwned.Length)
            {
                Array.Resize(ref _blockOwned, Math.Max(block + 1, _blockOwned.Length * 2));
            }

            _blockOwned[block]++;
        }
    }

    /// <summary>Releases everything a train owns (entering a depot, sold, removed).</summary>
    private void ReleaseAll(Train train)
    {
        for (var i = 0; i < train.StepCount; i++)
        {
            if (_owner[train.StepFrom(i)] == train.Id)
            {
                Release(train.StepFrom(i));
            }
        }

        if (train.EndTile >= 0 && _owner[train.EndTile] == train.Id)
        {
            Release(train.EndTile);
        }

        train.ClearSteps();
    }

    /// <summary>After the blocks were rebuilt: counts owned tiles per block again.</summary>
    private void RecountBlocks()
    {
        if (_blockOwned.Length < SignalBlocks.Count)
        {
            _blockOwned = new int[Math.Max(SignalBlocks.Count, _blockOwned.Length * 2)];
        }
        else
        {
            Array.Clear(_blockOwned);
        }

        foreach (var train in _trains)
        {
            for (var i = 0; i <= train.StepCount; i++)
            {
                var tile = i < train.StepCount ? train.StepFrom(i) : train.EndTile;
                if (tile >= 0 && SignalBlocks.BlockAt(tile) is var b and >= 0)
                {
                    _blockOwned[b]++;
                }
            }

            // Track removed or changed under a reservation is refused by construction, so routes stay on track; a train
            // whose planned route crosses a changed tile plans again.
            if (train.Route.Count > 0)
            {
                train.Flags |= TrainBits.NeedsRoute;
            }
        }
    }

    // ----- Reserving (research R5) -----

    /// <summary>
    /// Tries to reserve the next route segment: up to the next signal facing the travel direction, the stop or
    /// <see cref="MaxSegmentSteps"/>. Passing a block signal also needs the block entered to hold no other train.
    /// </summary>
    private bool TryExtend(Train train)
    {
        var route = train.Route;
        var start = train.RoutePos;
        if (start >= route.Count)
        {
            return false;
        }

        var end = train.StepCount == 0 ? DepotTile(train) : train.EndTile;
        if (route.FromAt(start) != end)
        {
            train.Flags |= TrainBits.NeedsRoute;
            return false;
        }

        var firstDir = route.DirAt(start);
        var signal = _rails.SignalAt(end);
        var kind = signal != 0 && RailSignal.Faces(signal, RailSignal.Axis(_rails.LinksAt(end)), firstDir) ? RailSignal.Kind(signal) : SignalKind.None;
        if (train.StepCount == 0)
        {
            kind = SignalKind.Block; // Leaving a depot: the block in front must be empty.
        }

        var i = start;
        var ok = true;
        while (i < route.Count)
        {
            var from = route.FromAt(i);
            var dir = route.DirAt(i);
            var to = _rails.FollowLink(from, dir);
            if (_owner[to] != 0 || !_rails.HasRail(to) || (_rails.LinksAt(from) & Dir8.Bit(dir)) == 0 || !CrossingFree(to))
            {
                ok = false;
                break;
            }

            i++;
            if (i == route.Count || i - start >= MaxSegmentSteps)
            {
                break;
            }

            var nextDir = route.DirAt(i);
            var s = _rails.SignalAt(to);
            if (s != 0 && RailSignal.Faces(s, RailSignal.Axis(_rails.LinksAt(to)), nextDir))
            {
                break; // The segment ends at the next signal facing the travel direction.
            }
        }

        if (ok && kind == SignalKind.Block)
        {
            var entered = _rails.FollowLink(end, firstDir);
            var block = SignalBlocks.BlockAt(entered);
            if (block >= 0 && block < _blockOwned.Length && _blockOwned[block] - OwnedIn(train, block) > 0)
            {
                ok = false;
            }
        }

        if (!ok)
        {
            train.Flags |= TrainBits.Waiting;
            // At a path signal (or anywhere but a block signal) another route may avoid the train in the way (research R9).
            if (kind != SignalKind.Block && _tick - train.LastPlanTick >= Rules.ReplanTicks)
            {
                PlanRoute(train, allowReverse: false, avoidTrains: true);
            }

            return false;
        }

        if (train.StepCount == 0)
        {
            Own(end, train.Id);
        }

        for (var k = start; k < i; k++)
        {
            var from = route.FromAt(k);
            var dir = route.DirAt(k);
            var to = _rails.FollowLink(from, dir);
            train.PushStep(from, dir, _rails.LinkLength(from, dir), to);
            Own(to, train.Id);
        }

        train.RoutePos = i;
        train.Flags &= ~TrainBits.Waiting;
        return true;
    }

    private int OwnedIn(Train train, int block)
    {
        var n = 0;
        for (var i = 0; i <= train.StepCount; i++)
        {
            var tile = i < train.StepCount ? train.StepFrom(i) : train.EndTile;
            if (tile >= 0 && SignalBlocks.BlockAt(tile) == block)
            {
                n++;
            }
        }

        return n;
    }

    /// <summary>A level crossing may be reserved only with no road vehicle on it or on the road tiles next to it (research R12).</summary>
    private bool CrossingFree(int tile)
    {
        if (_roadVehicles is null || !_state.Roads.HasRoad(tile))
        {
            return true;
        }

        if (_roadVehicles.IsTileOccupied(tile))
        {
            return false;
        }

        var links = _state.Roads.LinksAt(tile);
        for (var d = 0; d < 8; d++)
        {
            if ((links & (1 << d)) != 0 && _roadVehicles.IsTileOccupied(_state.Roads.Neighbour(tile, d)))
            {
                return false;
            }
        }

        return true;
    }

    // ----- Targets and routes (research R8, R9, R13) -----

    /// <summary>Picks the target from the orders: a depot when asked or due, otherwise the line's current stop.</summary>
    private void ChooseTarget(Train train, bool advance)
    {
        train.Route.Clear();
        train.RoutePos = 0;
        train.Flags &= ~TrainBits.NoRoute;
        if ((train.Orders & (VehicleOrders.SendToDepot | VehicleOrders.ServiceDue | VehicleOrders.Repair)) != 0)
        {
            train.TargetKind = RouteGoalKind.AnyDepot;
            train.TargetId = -1;
            train.Flags |= TrainBits.NeedsRoute;
            return;
        }

        var line = train.Line;
        var stops = line >= 0 && Lines.Exists(line) ? Lines.StopCount(line) : 0;
        if (stops == 0)
        {
            train.TargetKind = RouteGoalKind.None;
            train.TargetId = -1;
            return;
        }

        if (advance)
        {
            train.StopIndex = (train.StopIndex + 1) % stops;
        }

        train.StopIndex %= stops;
        for (var tries = 0; tries < stops; tries++)
        {
            var station = Lines.StopAt(line, train.StopIndex).StationId;
            if (HasPlatforms(station))
            {
                train.TargetKind = RouteGoalKind.Station;
                train.TargetId = station;
                train.Flags |= TrainBits.NeedsRoute;
                return;
            }

            Notify(VehicleNoticeKind.SkippedStop, train, station);
            train.StopIndex = (train.StopIndex + 1) % stops;
        }

        train.TargetKind = RouteGoalKind.None;
        train.TargetId = -1;
    }

    private bool HasPlatforms(int stationId)
    {
        var slot = _stations.SlotOf(stationId);
        if (slot < 0)
        {
            return false;
        }

        var tiles = _stations.TilesAt(slot);
        for (var i = 0; i < tiles.Count; i++) // Indexed: a foreach over the interface would allocate an enumerator.
        {
            if (Router.IsPlatformOf(tiles[i], stationId))
            {
                return true;
            }
        }

        return false;
    }

    private int DepotTile(Train train)
    {
        var index = Depots.IndexOf(train.Depot >= 0 ? train.Depot : train.HomeDepot);
        return index < 0 ? -1 : Depots.TileAt(index);
    }

    private readonly RailRoute _scratch = new();
    private readonly RailRoute _scratchForward = new();

    /// <summary>
    /// Plans the route from the end of the authority (or the depot). A halted train may also plan from its tail, reversed;
    /// the cheaper wins and the train reverses (research R8).
    /// </summary>
    private void PlanRoute(Train train, bool allowReverse, bool avoidTrains = false)
    {
        if (train.TargetKind == RouteGoalKind.None)
        {
            train.Route.Clear();
            train.RoutePos = 0;
            train.Flags &= ~TrainBits.NeedsRoute;
            return;
        }

        if (_searchBudget <= 0)
        {
            return; // Out of search work this tick: try again next tick.
        }

        train.LastPlanTick = _tick;
        int start, heading;
        if (train.StepCount == 0)
        {
            start = DepotTile(train);
            heading = start < 0 ? -1 : Depots.DirectionAtTile(start);
        }
        else
        {
            start = train.EndTile;
            heading = train.StepDir(train.StepCount - 1);
        }

        if (start < 0 || heading < 0)
        {
            return;
        }

        RouteSearches++;
        var penalty = avoidTrains || train.Speed == 0 ? Rules.OccupiedPenalty : Rules.OccupiedPenalty / 4;
        var budget = Math.Min(Rules.MaxSearchNodes, Math.Max(_searchBudget, 1000));
        var found = Router.FindRoute(start, heading, train.TargetKind, train.TargetId, train.Owner, train.Electric, _owner, train.Id,
            penalty, budget, _scratch, out var cost, out _);
        _searchBudget -= Router.LastExpanded;
        var reverse = false;
        if (allowReverse && train.StepCount > 0 && AtAuthorityEnd(train))
        {
            // From the tail, heading the other way (the first step reversed).
            var tailTile = train.StepFrom(0);
            var tailHeading = Dir8.Opposite(train.StepDir(0));
            if (_rails.LinksAt(tailTile) != 0)
            {
                var forward = _scratchForward;
                forward.CopyFrom(_scratch);
                RouteSearches++;
                var back = Router.FindRoute(tailTile, tailHeading, train.TargetKind, train.TargetId, train.Owner, train.Electric, _owner, train.Id,
                    penalty, budget, _scratch, out var backCost, out _);
                _searchBudget -= Router.LastExpanded;
                if (back && (!found || backCost + ReversePenalty(train) < cost))
                {
                    reverse = true;
                    found = true;
                }
                else
                {
                    _scratch.CopyFrom(forward);
                }
            }
        }

        if (!found)
        {
            train.Route.Clear();
            train.RoutePos = 0;
            if (avoidTrains)
            {
                return; // Keep the old route; trains will move.
            }

            if (train.TargetKind == RouteGoalKind.AnyDepot && (train.Orders & (VehicleOrders.ServiceDue | VehicleOrders.Repair)) != 0)
            {
                // No depot reachable for servicing: carry on with the line and try after the next stop (like road vehicles).
                train.Orders &= ~(VehicleOrders.ServiceDue | VehicleOrders.Repair);
                ChooseTarget(train, advance: false);
                return;
            }

            if ((train.Flags & TrainBits.NoRoute) == 0)
            {
                train.Flags |= TrainBits.NoRoute;
                Notify(VehicleNoticeKind.NoRoute, train, train.TargetKind == RouteGoalKind.Station ? train.TargetId : -1);
            }

            return;
        }

        if (reverse)
        {
            train.ReverseSteps();
        }

        train.Route.CopyFrom(_scratch);
        train.RoutePos = 0;
        train.Flags &= ~(TrainBits.NeedsRoute | TrainBits.NoRoute);
    }

    private static long ReversePenalty(Train train) => 2000 + (train.Length >> 8);

    // ----- Arrivals, loading and depots (research R11) -----

    private void Arrive(Train train)
    {
        if (train.TargetKind is RouteGoalKind.Depot or RouteGoalKind.AnyDepot)
        {
            var tile = train.EndTile;
            var index = Depots.IndexAtTile(tile);
            if (index >= 0)
            {
                EnterDepot(train, Depots.IdAt(index));
                return;
            }
        }

        var pulled = (train.Flags & TrainBits.PullingForward) != 0;
        if (train.TargetKind == RouteGoalKind.Station && !pulled)
        {
            train.ServedMask = 0;
            train.Flags &= ~TrainBits.ShortNotified;
        }

        train.Flags &= ~TrainBits.PullingForward;
        if (train.TargetKind != RouteGoalKind.Station)
        {
            train.Flags |= TrainBits.NeedsRoute;
            return;
        }

        train.Mode = TrainMode.Loading;
        BeginStop(train);
        if (pulled)
        {
            train.Timer += Rules.PullForwardTicks; // Stopping and starting again costs time (clarification Q5).
        }
    }

    private void EnterDepot(Train train, int depotId)
    {
        ReleaseAll(train);
        train.Mode = TrainMode.InDepot;
        train.Depot = depotId;
        train.Speed = 0;
        train.Route.Clear();
        train.RoutePos = 0;
        ReplaceInDepot(train);
        var serviced = (train.Orders & (VehicleOrders.ServiceDue | VehicleOrders.Repair)) != 0;
        if (serviced || train.DaysSinceService > 0)
        {
            Service(train);
        }

        var stay = (train.Orders & VehicleOrders.SendToDepot) != 0;
        train.Orders &= ~(VehicleOrders.ServiceDue | VehicleOrders.Repair | VehicleOrders.SendToDepot);
        if (stay)
        {
            train.Flags &= ~TrainBits.Started;
        }

        train.TargetKind = RouteGoalKind.None;
        ChooseTarget(train, advance: false);
    }

    internal void ReplaceInDepot(Train train)
    {
        var company = _state.FindCompany(train.Owner)!;
        if (company.RailReplacements.Rules.Count == 0)
        {
            return;
        }

        // Price the entire consist before changing any part. Depot visits run sequentially.
        long cost = 0;
        var length = train.Length;
        var depotTile = Depots.TileAt(Depots.IndexOf(train.Depot));
        var electrified = Rules.Types[_rails.TypeAt(depotTile)].Electrified;
        var changed = false;
        foreach (var part in train.Parts)
        {
            var index = ReplacementFor(company, train, part);
            if (index < 0)
            {
                continue;
            }

            var target = Rules.Models[index];
            if (!target.AvailableIn(_state.Year) || (part.Cargo >= 0 && !target.Carries(part.Cargo)) ||
                part.Hold.Total > target.Capacity || (target.Power == RailPower.Electric && !train.Electric && !electrified))
            {
                return;
            }

            changed = true;
            length += target.Length - Rules.Models[part.Model].Length;
            cost += (_finance?.Inflate(target.Price) ?? target.Price) - PartValue(part);
        }

        if (!changed || length > Rules.MaxTrainLength || (cost > company.Balance && !company.HasInfiniteMoney))
        {
            return;
        }

        company.Book(LedgerCategory.VehiclePurchase, -cost);
        var oldestPower = 0;
        foreach (var part in train.Parts)
        {
            var index = ReplacementFor(company, train, part);
            if (index >= 0)
            {
                var target = Rules.Models[index];
                part.Model = index;
                part.Price = _finance?.Inflate(target.Price) ?? target.Price;
                part.PurchaseDay = _stations.Today;
                part.RenewalNotified = false;
                if (part.Cargo < 0)
                {
                    part.Cargo = target.DefaultCargo;
                }

                _finance?.RunningCosts.Replace(part.RunningHandle, target.RunningCostPerYear, part.Price);
            }

            if (ModelOf(part).Kind == RollingStockKind.Locomotive)
            {
                oldestPower = Math.Max(oldestPower, _stations.Today - part.PurchaseDay);
            }
        }

        train.AgeDays = oldestPower;
        Recompute(train);
        Version++;
    }

    private int ReplacementFor(Company company, Train train, TrainPart part)
    {
        var model = ModelOf(part);
        if (!company.RailReplacements.TryGet(model.Id, out var rule))
        {
            return -1;
        }

        var due = _stations.Today - part.PurchaseDay >= model.LifeYears * 365;
        if (rule.Target.Length > 0)
        {
            return due || !rule.EndOfLifeOnly ? Rules.FindModel(rule.Target) : -1;
        }

        if (due && rule.Renew)
        {
            if (model.AvailableIn(_state.Year))
            {
                return part.Model;
            }

            if (!part.RenewalNotified)
            {
                Notify(VehicleNoticeKind.RenewalDue, train, -1);
                part.RenewalNotified = true;
            }
        }

        return -1;
    }

    private void TryLeaveDepot(Train train)
    {
        if ((train.Flags & TrainBits.Started) == 0 || train.TargetKind == RouteGoalKind.None || train.Parts.Count == 0 || train.PowerKw <= 0)
        {
            return;
        }

        if (train.Route.Count == 0 || (train.Flags & TrainBits.NeedsRoute) != 0)
        {
            PlanRoute(train, allowReverse: false);
            if (train.Route.Count == 0)
            {
                return;
            }
        }

        var tile = DepotTile(train);
        if (tile < 0 || _owner[tile] != 0)
        {
            return;
        }

        if (TryExtend(train))
        {
            train.Mode = TrainMode.Running;
            train.Depot = -1;
            train.HeadStep = 0;
            train.HeadOffset = 0;
            train.Flags &= ~TrainBits.Reversed;
        }
    }

    /// <summary>The line rule of the current stop.</summary>
    private StopRule CurrentRule(Train train)
    {
        var line = train.Line;
        return line >= 0 && Lines.Exists(line) && train.StopIndex < Lines.StopCount(line)
            ? Lines.StopAt(line, train.StopIndex).Rule
            : StopRule.LoadIfAvailable;
    }

    /// <summary>The station tile under a part's centre (where it stands along the train), or −1.</summary>
    private int TileUnderPart(Train train, int partIndex)
    {
        var reversed = (train.Flags & TrainBits.Reversed) != 0;
        long offset = 0;
        var count = train.Parts.Count;
        for (var k = 0; k < count; k++)
        {
            var index = reversed ? count - 1 - k : k;
            var len = Rules.Models[train.Parts[index].Model].Length;
            if (index == partIndex)
            {
                var position = train.HeadPosition - offset - (len / 2);
                for (var i = 0; i < train.StepCount; i++)
                {
                    var step = train.StepLength(i);
                    if (position <= step)
                    {
                        var from = train.StepFrom(i);
                        return position * 2 < step ? from : _rails.FollowLink(from, train.StepDir(i));
                    }

                    position -= step;
                }

                return -1;
            }

            offset += len;
        }

        return -1;
    }

    /// <summary>Unloads the parts along the platform and sets the dwell time (FR-013).</summary>
    private void BeginStop(Train train)
    {
        var rule = CurrentRule(train);
        var station = train.TargetId;
        var unloaded = 0;
        for (var p = 0; p < train.Parts.Count; p++)
        {
            if ((train.ServedMask & (1UL << p)) != 0 || !Router.IsPlatformOf(Math.Max(0, TileUnderPart(train, p)), station))
            {
                continue;
            }

            var part = train.Parts[p];
            if (rule == StopRule.NoLoading || part.Hold.Count == 0)
            {
                continue;
            }

            var before = part.Hold.Total;
            var paidBefore = _finance?.TotalPaid ?? 0;
            if (_finance is not null)
            {
                _finance.CurrentVehicleId = -1;
            }

            var result = _stations.Unload(station, part.Hold, rule == StopRule.UnloadOnly ? UnloadMode.Transfer : UnloadMode.Normal, NextStopStation(train));
            if (_finance is not null)
            {
                // Feature 013 (research R12): transfer credits move profit between the legs of a trip, never money.
                train.ProfitThisYear = Money.Add(train.ProfitThisYear, _finance.TotalPaid - paidBefore - result.CreditDelivered + result.CreditEarned);
            }

            if (result.Delivered > 0)
            {
                _deliveries++;
            }

            unloaded += before - part.Hold.Total;
        }

        var steps = (unloaded + 7) / 8;
        train.Timer = Math.Max(Rules.MinDwellTicks, steps * Rules.LoadIntervalTicks);
    }

    private void LoadStep(Train train)
    {
        if (train.Timer > 0)
        {
            train.Timer--;
            return;
        }

        var rule = CurrentRule(train);
        var station = train.TargetId;
        var loaded = 0;
        var waitingForFull = false;
        var unserved = false;
        for (var p = 0; p < train.Parts.Count; p++)
        {
            if ((train.ServedMask & (1UL << p)) != 0)
            {
                continue;
            }

            var part = train.Parts[p];
            var model = Rules.Models[part.Model];
            if (part.Cargo < 0)
            {
                train.ServedMask |= 1UL << p;
                continue;
            }

            if (!Router.IsPlatformOf(Math.Max(0, TileUnderPart(train, p)), station))
            {
                unserved = true;
                continue;
            }

            var space = model.Capacity - part.Hold.Total;
            if (rule is StopRule.UnloadOnly or StopRule.NoLoading || space <= 0)
            {
                train.ServedMask |= 1UL << p;
                continue;
            }

            var amount = _stations.Load(station, part.Cargo, Math.Min(model.LoadPerStep, space), part.Hold, Rules.KmhFromSpeed(train.MaxSpeed), NextStopStation(train));
            if (amount > 0)
            {
                loaded += amount;
            }
            else if (rule == StopRule.FullLoad)
            {
                waitingForFull = true;
            }
            else
            {
                train.ServedMask |= 1UL << p;
            }
        }

        if (loaded > 0 || waitingForFull)
        {
            train.Timer = Rules.LoadIntervalTicks;
            return;
        }

        if (unserved && TryPullForward(train))
        {
            return;
        }

        if (unserved && (train.Flags & TrainBits.ShortNotified) == 0)
        {
            train.Flags |= TrainBits.ShortNotified;
            Notify(VehicleNoticeKind.SkippedStop, train, station);
        }

        Depart(train);
    }

    /// <summary>
    /// At a through platform, reserves one platform length ahead along the route to the next stop and pulls forward so the
    /// next wagons line up (clarification Q5). False at a terminus or when the track ahead is not free.
    /// </summary>
    private bool TryPullForward(Train train)
    {
        var station = train.TargetId;
        var end = train.EndTile;
        if (end < 0)
        {
            return false;
        }

        var heading = train.StepDir(train.StepCount - 1);
        // The platform's length along the heading, counted back from its far end.
        var length = 0;
        var t = end;
        var back = Dir8.Opposite(heading);
        while (Router.IsPlatformOf(t, station) && length < 64)
        {
            length++;
            if ((_rails.LinksAt(t) & Dir8.Bit(back)) == 0)
            {
                break;
            }

            t = _rails.FollowLink(t, back);
        }

        // The track ahead, straight on or following the next stop's route: free tiles for one platform length.
        var exits = RailGeometry.Exits(_rails.LinksAt(end), heading);
        if (exits == 0)
        {
            return false; // A terminus.
        }

        var from = end;
        var dir = (exits & Dir8.Bit(heading)) != 0 ? heading : System.Numerics.BitOperations.TrailingZeroCount(exits);
        var count = 0;
        while (count < length)
        {
            var to = _rails.FollowLink(from, dir);
            if (_owner[to] != 0 || !CrossingFree(to))
            {
                return false;
            }

            count++;
            from = to;
            var next = RailGeometry.Exits(_rails.LinksAt(from), dir);
            if (next == 0)
            {
                break;
            }

            dir = (next & Dir8.Bit(dir)) != 0 ? dir : System.Numerics.BitOperations.TrailingZeroCount(next);
        }

        if (count == 0)
        {
            return false;
        }

        from = end;
        dir = (exits & Dir8.Bit(heading)) != 0 ? heading : System.Numerics.BitOperations.TrailingZeroCount(exits);
        for (var k = 0; k < count; k++)
        {
            var to = _rails.FollowLink(from, dir);
            train.PushStep(from, dir, _rails.LinkLength(from, dir), to);
            Own(to, train.Id);
            from = to;
            var next = RailGeometry.Exits(_rails.LinksAt(from), dir);
            if (next != 0)
            {
                dir = (next & Dir8.Bit(dir)) != 0 ? dir : System.Numerics.BitOperations.TrailingZeroCount(next);
            }
        }

        train.Mode = TrainMode.Running;
        train.Flags |= TrainBits.PullingForward;
        train.Route.Clear();
        train.RoutePos = 0;
        return true;
    }

    private void Depart(Train train)
    {
        train.Mode = TrainMode.Running;
        train.ServedMask = 0;
        if (train.ServiceInterval > 0 && train.DaysSinceService >= train.ServiceInterval)
        {
            train.Orders |= VehicleOrders.ServiceDue;
        }

        ChooseTarget(train, advance: true);
    }

    private void Notify(VehicleNoticeKind kind, Train train, int stationId)
    {
        var tile = train.StepCount > 0 ? train.StepFrom(train.HeadStep) : DepotTile(train);
        var x = tile < 0 ? 0 : tile % _rails.Width;
        var z = tile < 0 ? 0 : tile / _rails.Width;
        Notices.Add(new VehicleNotice(kind, train.Id, train.Owner, x, z, stationId));
    }
}

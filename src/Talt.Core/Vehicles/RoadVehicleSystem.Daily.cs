using Talt.Core.Construction;
using Talt.Core.Finance;
using Talt.Core.Simulation;

namespace Talt.Core.Vehicles;

/// <summary>
/// Day, month and year work (research R9, R11): ageing, reliability, breakdowns from a stateless hash, service due,
/// running-cost statistics, vehicle values for the company value, depot maintenance and the yearly profit rotation;
/// plus the debug invariant check (FR-032).
/// </summary>
public sealed partial class RoadVehicleSystem
{
    private long[] _depotCarry = new long[1];

    /// <summary>Day work, after finance (contracts/vehicles-api.md).</summary>
    public void AdvanceDay(GameDate date)
    {
        var rules = Rules;
        for (var slot = 0; slot < Count; slot++)
        {
            var model = rules.Models[_model[slot]];
            _ageDays[slot]++;
            _daysSinceService[slot]++;
            var decay = rules.ReliabilityDecayPerDay * (_ageDays[slot] > model.LifeYears * 365 ? rules.OldAgeFactor : 1);
            _reliability[slot] = Math.Max(rules.MinReliability, _reliability[slot] - decay);

            // Running cost as a per-vehicle statistic (finance charges the company total itself).
            var carry = _runningCarry[slot] + model.RunningCostPerYear;
            _profitThisYear[slot] = Money.Add(_profitThisYear[slot], -(carry / 365));
            Lines.RecordProfit(_line[slot], -(carry / 365));
            _runningCarry[slot] = carry % 365;

            ref var h = ref _hot[slot];
            if (h.Lane >= 0 && h.Speed > 0 && (h.Flags & (RoadVehicleBits.BrokenDown | RoadVehicleBits.AtStop)) == 0
                && BreakdownRoll(_id[slot], date.DayNumber) < (1000L - _reliability[slot]) * rules.BreakdownChanceScale)
            {
                BreakDown(slot);
            }
        }
    }

    /// <summary>Month work, before finance's month end: vehicle values and depot maintenance (research R11).</summary>
    public void AdvanceMonth(GameDate date)
    {
        _ = date;
        if (_finance is not null)
        {
            for (var slot = 0; slot < Count; slot++)
            {
                if (_runningHandle[slot] >= 0)
                {
                    _finance.RunningCosts.SetValue(_runningHandle[slot], ValueAt(slot));
                }
            }
        }

        for (var c = 0; c < _state.Companies.Count; c++)
        {
            var company = _state.Companies[c];
            var depots = Depots.CountOwnedBy(company.Id);
            if (company.Id >= _depotCarry.Length)
            {
                Array.Resize(ref _depotCarry, company.Id + 1);
            }

            if (depots == 0 && _depotCarry[company.Id] == 0)
            {
                continue;
            }

            var yearly = _finance?.Inflate(Rules.DepotMaintenancePerYear * depots) ?? Rules.DepotMaintenancePerYear * depots;
            var accrued = _depotCarry[company.Id] + yearly;
            _depotCarry[company.Id] = accrued % 12;
            if (accrued / 12 != 0)
            {
                company.Book(LedgerCategory.PropertyMaintenance, -(accrued / 12));
            }
        }
    }

    /// <summary>Year work: this year's profits become last year's.</summary>
    public void AdvanceYear(GameDate date)
    {
        _ = date;
        for (var slot = 0; slot < Count; slot++)
        {
            PostDistance(slot);
            _profitLastYear[slot] = _profitThisYear[slot];
            _profitThisYear[slot] = 0;
        }

        Lines.CloseYear(); // Shared by both modes; the road system owns the registry.
    }

    /// <summary>0–999 999 from the vehicle id and the day: the same everywhere, no shared random stream (research R7).</summary>
    private long BreakdownRoll(int vehicleId, int dayNumber)
    {
        var x = _seed ^ ((ulong)(uint)vehicleId * 0x9E3779B97F4A7C15UL) ^ ((ulong)(uint)dayNumber * 0xC2B2AE3D27D4EB4FUL);
        x ^= x >> 30;
        x *= 0xBF58476D1CE4E5B9UL;
        x ^= x >> 27;
        x *= 0x94D049BB133111EBUL;
        x ^= x >> 31;
        return (long)(x % 1_000_000UL);
    }

    /// <summary>Starts a breakdown (research R9): crawl at a pulsing speed towards the nearest depot.</summary>
    internal void BreakDown(int slot)
    {
        ref var h = ref _hot[slot];
        h.Flags |= RoadVehicleBits.BrokenDown;
        h.BreakdownTicks = Rules.BreakdownDays * GameClock.TicksPerDay;
        _orders[slot] |= VehicleOrders.Repair;
        Notify(VehicleNoticeKind.BrokenDown, slot, -1);
        if ((h.Flags & RoadVehicleBits.HoldsJunction) == 0 && _targetKind[slot] != VehicleTargetKind.Depot)
        {
            TargetNearestDepot(slot);
        }
    }

    /// <summary>
    /// Debug check (FR-032, SC-002): every lane list is sorted with at least the minimum gap between vehicles, every
    /// vehicle on a lane stands on a road link of its lane, and junction holders are consistent. Null when all hold.
    /// </summary>
    public string? CheckInvariants()
    {
        var listed = 0;
        for (var lane = 0; lane < _laneCount; lane++)
        {
            var key = _laneKey[lane];
            var previous = -1;
            for (var slot = _laneHead[lane]; slot >= 0; slot = _behind[slot])
            {
                listed++;
                ref readonly var h = ref _hot[slot];
                if (h.Lane != key)
                {
                    return $"vehicle {_id[slot]} is listed in lane {key} but drives on {h.Lane}";
                }

                if (previous >= 0)
                {
                    ref readonly var p = ref _hot[previous];
                    if (h.S > p.S - p.Length - Rules.MinGap)
                    {
                        return $"vehicles {_id[previous]} and {_id[slot]} overlap in lane {key} ({p.S}/{p.Length} and {h.S})";
                    }
                }

                previous = slot;
            }
        }

        var onLanes = 0;
        for (var slot = 0; slot < Count; slot++)
        {
            ref readonly var h = ref _hot[slot];
            if (h.Lane < 0)
            {
                if (!h.Has(RoadVehicleBits.InDepot))
                {
                    return $"vehicle {_id[slot]} is neither on a lane nor in a depot";
                }

                continue;
            }

            onLanes++;
            if ((_roads.LinksAt(h.Lane >> 3) & (1 << (h.Lane & 7))) == 0)
            {
                return $"vehicle {_id[slot]} stands on a lane without a road link";
            }

            if (h.HeldJunction >= 0 && (!_holders.TryGetValue(h.HeldJunction, out var holder) || holder != _id[slot]))
            {
                return $"vehicle {_id[slot]} holds junction {h.HeldJunction} but is not its holder";
            }
        }

        return listed == onLanes ? null : $"{listed} vehicles listed in lanes, {onLanes} on lanes";
    }
}

/// <summary>What a notice tells the player.</summary>
public enum VehicleNoticeKind : byte
{
    Stuck = 0,
    NoRoute = 1,
    SkippedStop = 2,
    BrokenDown = 3,
    Serviced = 4,
}

/// <summary>One notice for the client; (X, Z) is the vehicle's tile.</summary>
public readonly record struct VehicleNotice(VehicleNoticeKind Kind, int VehicleId, int CompanyId, int X, int Z, int StationId);

/// <summary>Fixed-size feed of vehicle notices for the client (not state; the oldest is dropped when full).</summary>
public sealed class VehicleNoticeFeed
{
    public const int Capacity = 256;

    private readonly VehicleNotice[] _items = new VehicleNotice[Capacity];
    private int _head;
    private int _count;

    public int Count => _count;

    internal void Add(in VehicleNotice notice)
    {
        _items[(_head + _count) % Capacity] = notice;
        if (_count == Capacity)
        {
            _head = (_head + 1) % Capacity;
        }
        else
        {
            _count++;
        }
    }

    public bool TryDequeue(out VehicleNotice notice)
    {
        if (_count == 0)
        {
            notice = default;
            return false;
        }

        notice = _items[_head];
        _head = (_head + 1) % Capacity;
        _count--;
        return true;
    }

    public void Clear()
    {
        _head = 0;
        _count = 0;
    }
}

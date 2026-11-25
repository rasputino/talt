using Talt.Core.Content;
using Talt.Core.Industries;
using Talt.Core.Simulation;

namespace Talt.Core.Stations;

/// <summary>
/// The daily cargo flow (research R6, R7, R10): production to serving groups with exact rounding, the waiting
/// cap, ratings and losses, and the debug service.
/// </summary>
public sealed partial class StationRegistry
{
    private const int MaxServing = 8;

    private readonly Dictionary<long, int> _carry = [];

    /// <summary>Groups needed before the rating phase runs in parallel (tests lower it to exercise that path).</summary>
    internal int ParallelGroupThreshold { get; set; } = 1024;
    private readonly Action<int> _ratingPhase;
    private readonly CargoHold _debugHold = new();

    /// <summary>
    /// Production sink of <see cref="IndustryFleet.AdvanceDay"/> (FR-014): takes the share of an industry's daily
    /// output set by the best rating among the groups serving it; the rest stays at the industry.
    /// </summary>
    public int TakeProduction(int industryId, int cargoIndex, int amount)
    {
        if (amount <= 0 || _prodRows == 0 || !_industryRow.TryGetValue(industryId, out var row))
        {
            return 0;
        }

        return Distribute(row, cargoIndex, amount, -1);
    }

    /// <summary>
    /// Starts serving a cargo at the station's group (feature 012 calls it when a vehicle first loads there): from
    /// the next day producers in the catchment send that cargo.
    /// </summary>
    public void MarkServed(int stationId, int cargo)
    {
        var slot = SlotOf(stationId);
        if (slot < 0 || (uint)cargo >= (uint)_cargoCount)
        {
            return;
        }

        var cell = (_group[slot] * _cargoCount) + cargo;
        if (_served[cell] == 0)
        {
            _served[cell] = 1;
            _rating[cell] = Rules.Rating.Initial;
        }
    }

    /// <summary>Town buildings' passengers and mail of the day that just ended, to serving groups.</summary>
    private void SupplyFromBuildings(GameDate date)
    {
        if (_prodRows == 0 || (_passengerCargo < 0 && _mailCargo < 0))
        {
            return;
        }

        var ended = DateOnly.FromDayNumber(date.DayNumber - 1);
        var daysInMonth = DateTime.DaysInMonth(ended.Year, ended.Month);
        var dayOfMonth = ended.Day;
        for (var row = 0; row < _prodRows; row++)
        {
            if (_prodKind[row] != ProducerBuilding)
            {
                continue;
            }

            if (_passengerCargo >= 0 && _prodPassengers[row] > 0)
            {
                var daily = SpreadAmount(_prodPassengers[row], daysInMonth, dayOfMonth);
                if (daily > 0)
                {
                    Distribute(row, _passengerCargo, daily, _prodTown[row]);
                }
            }

            if (_mailCargo >= 0 && _prodMail[row] > 0)
            {
                var daily = SpreadAmount(_prodMail[row], daysInMonth, dayOfMonth);
                if (daily > 0)
                {
                    Distribute(row, _mailCargo, daily, _prodTown[row]);
                }
            }
        }
    }

    /// <summary>
    /// The share of a monthly amount produced on one day, spread evenly and exactly: the days of a month add up
    /// to <paramref name="monthly"/> (small amounts arrive on some days instead of all on the last one).
    /// </summary>
    internal static int SpreadAmount(int monthly, int daysInMonth, int dayOfMonth) =>
        (int)(((long)monthly * dayOfMonth / daysInMonth) - ((long)monthly * (dayOfMonth - 1) / daysInMonth));

    /// <summary>
    /// Moves a producer's daily amount to the groups serving it (research R6) and returns how much moved: the
    /// amount times the best rating (with the producer's carried remainder), split among the best groups in
    /// proportion to their ratings, the remainder to the best one.
    /// </summary>
    private int Distribute(int row, int cargo, int amount, int sourceTown)
    {
        var categoryBit = 1 << (int)_cargoCategory[cargo];
        Span<int> groups = stackalloc int[MaxServing];
        Span<int> ratings = stackalloc int[MaxServing];
        Span<int> origins = stackalloc int[MaxServing];
        var n = 0;
        var start = _prodStart[row];
        var end = start + _prodCount[row];
        for (var k = start; k < end; k++)
        {
            var slot = _coverSlots[k];
            if ((_categories[slot] & categoryBit) == 0)
            {
                continue;
            }

            var g = _group[slot];
            var cell = (g * _cargoCount) + cargo;
            if (_served[cell] == 0)
            {
                continue;
            }

            var found = -1;
            for (var i = 0; i < n; i++)
            {
                if (groups[i] == g)
                {
                    found = i;
                    break;
                }
            }

            if (found >= 0)
            {
                origins[found] = Math.Min(origins[found], _id[slot]);
                continue;
            }

            if (n == MaxServing)
            {
                continue;
            }

            // Insert keeping the order: rating descending, then group key ascending.
            var rating = _rating[cell];
            var at = n;
            while (at > 0 && (ratings[at - 1] < rating || (ratings[at - 1] == rating && _groupKey[groups[at - 1]] > _groupKey[g])))
            {
                groups[at] = groups[at - 1];
                ratings[at] = ratings[at - 1];
                origins[at] = origins[at - 1];
                at--;
            }

            groups[at] = g;
            ratings[at] = rating;
            origins[at] = _id[slot];
            n++;
        }

        if (n == 0)
        {
            return 0;
        }

        var count = Math.Min(n, Rules.MaxStationsPerProducer);
        var key = ((long)_prodKind[row] << 56) | ((long)_prodId[row] << 8) | (long)cargo;
        _carry.TryGetValue(key, out var carry);
        var total = ((long)amount * ratings[0]) + carry;
        var moved = (int)(total / 1000);
        var remainder = (int)(total % 1000);
        if (remainder != carry)
        {
            _carry[key] = remainder;
        }

        if (moved == 0)
        {
            return 0;
        }

        long sum = 0;
        for (var i = 0; i < count; i++)
        {
            sum += ratings[i];
        }

        var given = 0;
        for (var i = 1; i < count && sum > 0; i++)
        {
            var part = (int)(moved * (long)ratings[i] / sum);
            if (part > 0)
            {
                Arrive(groups[i], cargo, part, origins[i], sourceTown, -1, -1, supplied: true);
                given += part;
            }
        }

        Arrive(groups[0], cargo, moved - given, origins[0], sourceTown, -1, -1, supplied: true);
        return moved;
    }

    /// <summary>Adds cargo to a group's waiting stock up to the cap; the excess is lost and marks an overflow.</summary>
    private void Arrive(int group, int cargo, int amount, int origin, int sourceTown, int destination, int pickupDay, bool supplied)
    {
        if (amount <= 0)
        {
            return;
        }

        var cell = (group * _cargoCount) + cargo;
        var room = Math.Max(0, Rules.WaitingCap - _waiting[cell]);
        var kept = Math.Min(amount, room);
        if (kept > 0)
        {
            AddPacket(cell, kept, origin, sourceTown, destination, pickupDay);
        }

        if (supplied)
        {
            _totalSupplied += amount;
            _receivedThis[cell] += amount;
        }

        var lost = amount - kept;
        if (lost > 0)
        {
            _lostThis[cell] += lost;
            _totalLost += lost;
            _overflowed[cell] = 1;
        }
    }

    /// <summary>Ratings and neglect losses of every served group and cargo (research R7).</summary>
    private void UpdateRatings(bool parallel)
    {
        if (parallel && _groupCount >= ParallelGroupThreshold)
        {
            Parallel.For(0, _groupCount, _ratingPhase);
        }
        else
        {
            for (var g = 0; g < _groupCount; g++)
            {
                RatingPhase(g);
            }
        }

        // Losses touch the shared packet pool, so they are applied sequentially in group order.
        var cells = _groupCount * _cargoCount;
        for (var cell = 0; cell < cells; cell++)
        {
            var loss = _pendingLoss[cell];
            if (loss > 0)
            {
                _pendingLoss[cell] = 0;
                var removed = RemoveOldest(cell, loss);
                _lostThis[cell] += removed;
                _totalLost += removed;
            }
        }
    }

    /// <summary>One group's ratings: touches only that group's cells (safe in parallel).</summary>
    private void RatingPhase(int group)
    {
        var rules = Rules.Rating;
        var start = group * _cargoCount;
        for (var cell = start; cell < start + _cargoCount; cell++)
        {
            if (_served[cell] == 0)
            {
                continue;
            }

            var rating = _rating[cell];
            if (_overflowed[cell] != 0)
            {
                _overflowed[cell] = 0;
                rating = Math.Max(0, rating - rules.OverflowPenalty);
            }

            var days = _lastPickupDay[cell] < 0 ? -1 : _today - _lastPickupDay[cell];
            var target = rules.Target(days, _lastSpeed[cell], _waiting[cell]);
            rating = rating < target
                ? Math.Min(target, rating + rules.MaxStepPerDay)
                : Math.Max(target, rating - rules.MaxStepPerDay);
            _rating[cell] = rating;

            var waiting = _waiting[cell];
            if (rating < Rules.LossBelowRating && waiting > Rules.LossAboveWaiting)
            {
                var loss = (long)waiting * Rules.LossPerMilleAtZero * (Rules.LossBelowRating - rating) / Rules.LossBelowRating / 1000;
                _pendingLoss[cell] = (int)Math.Max(1, Math.Min(waiting, loss));
            }
        }
    }

    /// <summary>The debug service marks every received cargo of every road station as served (research R10).</summary>
    private void MarkDebugServed()
    {
        for (var slot = 0; slot < _count; slot++)
        {
            var supplies = _supplyMask[slot];
            while (supplies != 0)
            {
                var cargo = System.Numerics.BitOperations.TrailingZeroCount(supplies);
                supplies &= supplies - 1;
                MarkServed(_id[slot], cargo);
            }
        }
    }

    /// <summary>The debug service picks up its share of every group's waiting cargo.</summary>
    private void DebugPickups()
    {
        var percent = _settings.DebugServicePercent;
        for (var g = 0; g < _groupCount; g++)
        {
            for (var cargo = 0; cargo < _cargoCount; cargo++)
            {
                var cell = (g * _cargoCount) + cargo;
                var amount = (int)((long)_waiting[cell] * percent / 100);
                if (_served[cell] == 0 || amount <= 0)
                {
                    continue;
                }

                Load(_groupKey[g], cargo, amount, _debugHold, Rules.DebugSpeedKmh);
                _totalDebugCollected += _debugHold.Total;
                _debugHold.Clear();
            }
        }
    }
}

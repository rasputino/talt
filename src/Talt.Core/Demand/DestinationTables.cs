using Talt.Core.Determinism;

namespace Talt.Core.Demand;

/// <summary>Unserved monthly demand between two towns, both directions together (overlay).</summary>
public readonly record struct DemandPair(int TownA, int TownB, int Unserved);

/// <summary>
/// Where destination cargo wants to go (feature 013, research R1–R2, data-model.md DestinationTables): for every
/// destination cargo, one table per source (a town for building cargo, a producing industry for industry cargo). A
/// table lists up to <see cref="DemandRules.MaxDestinations"/> + 1 destination towns with shares in parts per million
/// (the own town first when it has a share), a carry per entry for exact apportionment, and monthly statistics of the
/// units moved to stations and the units nobody could carry.
/// </summary>
/// <remarks>
/// Weights are integers: population^a × 2^24 / distance^k. Tables are built in full at creation and then refreshed for
/// a slice of sources every day so that every table is refreshed once a month. Kept entries (same destination) keep
/// their carry and statistics. Everything is integer and deterministic; the tables are hashed once the router is
/// active (<see cref="AppendTo"/> is called only then).
/// </remarks>
public sealed partial class DestinationTables : IStateHashable
{
    private const int WeightShift = 24;
    private const int SliceDays = 30;

    private readonly IDemandWorld _world;
    private readonly RuleTables[] _tables;
    private readonly long[] _weightScratch;
    private readonly int[] _townScratch;
    private int _townCursor;

    /// <summary>Creates and fills every table.</summary>
    public DestinationTables(DemandRules rules, IDemandWorld world)
    {
        ArgumentNullException.ThrowIfNull(rules);
        Rules = rules;
        _world = world ?? throw new ArgumentNullException(nameof(world));
        Width = rules.MaxDestinations + 1;
        _weightScratch = new long[Width];
        _townScratch = new int[Width];
        _tables = new RuleTables[rules.Cargo.Count];
        for (var r = 0; r < _tables.Length; r++)
        {
            _tables[r] = new RuleTables(rules.Cargo[r], Width);
        }

        for (var r = 0; r < _tables.Length; r++)
        {
            var t = _tables[r];
            if (t.Rule.Source == DemandSource.Buildings)
            {
                t.EnsureRows(world.TownCount);
                t.Rows = world.TownCount;
                for (var town = 0; town < world.TownCount; town++)
                {
                    Refresh(t, town);
                }
            }
            else
            {
                AddNewIndustries(t);
                for (var row = 0; row < t.Rows; row++)
                {
                    Refresh(t, row);
                }
            }
        }
    }

    public DemandRules Rules { get; }

    /// <summary>Entries per table (destinations + own town).</summary>
    public int Width { get; }

    /// <summary>Bumped whenever a table changes (windows refresh). Not hashed.</summary>
    public int Version { get; private set; }

    /// <summary>The table row of a source: the town index for building cargo, the industry's row for industry cargo, or −1.</summary>
    public int RowOf(int rule, int sourceId)
    {
        var t = _tables[rule];
        if (t.Rule.Source == DemandSource.Buildings)
        {
            return (uint)sourceId < (uint)t.Rows ? sourceId : -1;
        }

        return t.RowOfIndustry.TryGetValue(sourceId, out var row) ? row : -1;
    }

    /// <summary>Number of rows (sources) of a rule.</summary>
    public int RowCount(int rule) => _tables[rule].Rows;

    /// <summary>The source id of a row: a town index or an industry id.</summary>
    public int SourceOf(int rule, int row) => _tables[rule].Source[row];

    /// <summary>Entries of a row.</summary>
    public int CountOf(int rule, int row) => _tables[rule].Count[row];

    /// <summary>Destination towns of a row (own town first when it has a share).</summary>
    public ReadOnlySpan<int> Destinations(int rule, int row)
    {
        var t = _tables[rule];
        return t.Dest.AsSpan(row * Width, t.Count[row]);
    }

    /// <summary>Shares of a row, parts per million.</summary>
    public ReadOnlySpan<int> Shares(int rule, int row)
    {
        var t = _tables[rule];
        return t.Share.AsSpan(row * Width, t.Count[row]);
    }

    /// <summary>Monthly statistics of one entry: units moved to stations and units nobody could carry.</summary>
    public (int MovedLast, int UnservedLast, int MovedThis, int UnservedThis) Statistics(int rule, int row, int index)
    {
        var t = _tables[rule];
        var i = (row * Width) + index;
        return (t.MovedLast[i], t.UnservedLast[i], t.MovedThis[i], t.UnservedThis[i]);
    }

    /// <summary>
    /// Splits <paramref name="amount"/> units of a source over its destinations (research R10): writes the units of
    /// each entry to <paramref name="units"/> (at least <see cref="CountOf"/> long) and returns the entry count.
    /// </summary>
    public int Split(int rule, int row, int amount, Span<int> units)
    {
        var t = _tables[rule];
        var n = t.Count[row];
        Apportion.Split(t.Share.AsSpan(row * Width, n), t.Carry.AsSpan(row * Width, n), amount, units);
        return n;
    }

    /// <summary>Counts units of an entry as moved to a station (served).</summary>
    public void RecordMoved(int rule, int row, int index, int units) => _tables[rule].MovedThis[(row * Width) + index] += units;

    /// <summary>Counts units of an entry as unserved (no route).</summary>
    public void RecordUnserved(int rule, int row, int index, int units) => _tables[rule].UnservedThis[(row * Width) + index] += units;

    /// <summary>
    /// Monthly demand of one entry for a source that produced <paramref name="monthlyUnits"/> last month (window and
    /// overlay): the source's production times the entry's share.
    /// </summary>
    public int MonthlyDemand(int rule, int row, int index, int monthlyUnits)
    {
        var t = _tables[rule];
        return (int)((long)monthlyUnits * t.Share[(row * Width) + index] / Apportion.ShareScale);
    }

    /// <summary>
    /// The town pairs with the largest unserved monthly demand of a building cargo (US6 overlay): for every town,
    /// demand to each other town (<paramref name="monthlyBySource"/>[town] × share) less what was moved to stations
    /// last month, both directions of a pair added. Writes up to <paramref name="count"/> pairs, largest first.
    /// </summary>
    public void TopUnservedPairs(int rule, ReadOnlySpan<int> monthlyBySource, int count, List<DemandPair> into)
    {
        ArgumentNullException.ThrowIfNull(into);
        into.Clear();
        var t = _tables[rule];
        if (t.Rule.Source != DemandSource.Buildings || count <= 0)
        {
            return;
        }

        var sums = new Dictionary<long, int>();
        for (var row = 0; row < t.Rows && row < monthlyBySource.Length; row++)
        {
            for (var i = 0; i < t.Count[row]; i++)
            {
                var c = (row * Width) + i;
                var dest = t.Dest[c];
                if (dest == row || dest < 0)
                {
                    continue;
                }

                var demand = (int)((long)monthlyBySource[row] * t.Share[c] / Apportion.ShareScale);
                var unserved = Math.Max(0, demand - t.MovedLast[c]);
                if (unserved == 0)
                {
                    continue;
                }

                var key = ((long)Math.Min(row, dest) << 32) | (uint)Math.Max(row, dest);
                sums.TryGetValue(key, out var sum);
                sums[key] = sum + unserved;
            }
        }

        foreach (var (key, amount) in sums)
        {
            into.Add(new DemandPair((int)(key >> 32), (int)key, amount));
        }

        into.Sort((a, b) => b.Unserved != a.Unserved ? b.Unserved.CompareTo(a.Unserved) : a.TownA != b.TownA ? a.TownA.CompareTo(b.TownA) : a.TownB.CompareTo(b.TownB));
        if (into.Count > count)
        {
            into.RemoveRange(count, into.Count - count);
        }
    }

    /// <summary>Refreshes the day's slice of tables (every table once a month) and adds rows for new industries.</summary>
    public void AdvanceDay()
    {
        var towns = _world.TownCount;
        var perDay = (towns + SliceDays - 1) / SliceDays;
        foreach (var t in _tables)
        {
            if (t.Rule.Source == DemandSource.Industries)
            {
                AddNewIndustries(t);
                var rows = (t.Rows + SliceDays - 1) / SliceDays;
                for (var k = 0; k < rows && t.Rows > 0; k++)
                {
                    Refresh(t, t.Cursor);
                    t.Cursor = (t.Cursor + 1) % t.Rows;
                }
            }
        }

        for (var k = 0; k < perDay && towns > 0; k++)
        {
            foreach (var t in _tables)
            {
                if (t.Rule.Source == DemandSource.Buildings)
                {
                    Refresh(t, _townCursor);
                }
            }

            _townCursor = (_townCursor + 1) % towns;
        }

        Version++;
    }

    /// <summary>Rotates the monthly statistics.</summary>
    public void AdvanceMonth()
    {
        foreach (var t in _tables)
        {
            var cells = t.Rows * Width;
            Array.Copy(t.MovedThis, t.MovedLast, cells);
            Array.Clear(t.MovedThis, 0, cells);
            Array.Copy(t.UnservedThis, t.UnservedLast, cells);
            Array.Clear(t.UnservedThis, 0, cells);
        }

        Version++;
    }

    public void AppendTo(StateHasher hasher)
    {
        ArgumentNullException.ThrowIfNull(hasher);
        hasher.Append(Rules.Fingerprint);
        hasher.Append(_townCursor);
        foreach (var t in _tables)
        {
            var cells = t.Rows * Width;
            hasher.Append(t.Rows);
            hasher.Append(t.Cursor);
            hasher.Append<int>(t.Source.AsSpan(0, t.Rows));
            hasher.Append<int>(t.Count.AsSpan(0, t.Rows));
            hasher.Append<int>(t.Dest.AsSpan(0, cells));
            hasher.Append<int>(t.Share.AsSpan(0, cells));
            hasher.Append<long>(t.Carry.AsSpan(0, cells));
            hasher.Append<int>(t.MovedThis.AsSpan(0, cells));
            hasher.Append<int>(t.MovedLast.AsSpan(0, cells));
            hasher.Append<int>(t.UnservedThis.AsSpan(0, cells));
            hasher.Append<int>(t.UnservedLast.AsSpan(0, cells));
        }
    }

    /// <summary>The weight of a destination town seen from (x, z): population^a × 2^24 / distance^k.</summary>
    internal static long Weight(int population, int distance, int populationExponent, int distanceExponent)
    {
        if (population <= 0)
        {
            return 0;
        }

        long p = 1;
        for (var i = 0; i < populationExponent; i++)
        {
            p *= population;
        }

        long d = 1;
        var dist = Math.Max(1, distance);
        for (var i = 0; i < distanceExponent; i++)
        {
            d *= dist;
        }

        return (long)(((Int128)p << WeightShift) / d);
    }

    private void AddNewIndustries(RuleTables t)
    {
        for (var slot = 0; slot < _world.IndustryCount; slot++)
        {
            var id = _world.IndustryIdAt(slot);
            if (t.RowOfIndustry.ContainsKey(id) || !_world.IndustryProduces(slot, t.Rule.CargoIndex))
            {
                continue;
            }

            t.EnsureRows(t.Rows + 1);
            var row = t.Rows++;
            t.RowOfIndustry.Add(id, row);
            t.Source[row] = id;
            t.Count[row] = 0;
            var (x, z) = _world.IndustryPosition(slot);
            t.X[row] = x;
            t.Z[row] = z;
        }
    }

    /// <summary>Recomputes one table (research R1), keeping carries and statistics of destinations it keeps.</summary>
    private void Refresh(RuleTables t, int row)
    {
        var rule = t.Rule;
        int x, z, own;
        if (rule.Source == DemandSource.Buildings)
        {
            (x, z) = _world.TownCentre(row);
            t.Source[row] = row;
            own = row;
        }
        else
        {
            x = t.X[row];
            z = t.Z[row];
            own = -1;
        }

        // Top destinations by weight (descending), then town index (ascending).
        var others = rule.Source == DemandSource.Buildings ? Width - 1 : Width;
        var kept = 0;
        var towns = _world.TownCount;
        for (var town = 0; town < towns; town++)
        {
            if (town == own)
            {
                continue;
            }

            var (tx, tz) = _world.TownCentre(town);
            var distance = Math.Abs(tx - x) + Math.Abs(tz - z);
            if (distance > Rules.RangeTiles || (rule.Source == DemandSource.Industries && !_world.TownAccepts(town, rule.CargoIndex)))
            {
                continue;
            }

            var weight = Weight(_world.TownPopulation(town), distance, rule.PopulationExponent, rule.DistanceExponent);
            if (weight <= 0 || (kept == others && weight <= _weightScratch[kept - 1]))
            {
                continue;
            }

            var at = kept < others ? kept++ : kept - 1;
            while (at > 0 && _weightScratch[at - 1] < weight)
            {
                _weightScratch[at] = _weightScratch[at - 1];
                _townScratch[at] = _townScratch[at - 1];
                at--;
            }

            _weightScratch[at] = weight;
            _townScratch[at] = town;
        }

        // Entries: own town (when it has a share) then the others; shares by cumulative floors so they sum exactly.
        Span<int> newDest = stackalloc int[Width];
        Span<int> newShare = stackalloc int[Width];
        var n = 0;
        var ownShare = rule.Source == DemandSource.Buildings ? (kept == 0 ? Apportion.ShareScale : rule.OwnTownPerMille * 1000) : 0;
        if (ownShare > 0)
        {
            newDest[n] = own;
            newShare[n] = ownShare;
            n++;
        }

        var rest = (long)Apportion.ShareScale - ownShare;
        Int128 total = 0;
        for (var i = 0; i < kept; i++)
        {
            total += _weightScratch[i];
        }

        Int128 cumulative = 0;
        long previous = 0;
        for (var i = 0; i < kept && rest > 0; i++)
        {
            cumulative += _weightScratch[i];
            var boundary = (long)(rest * cumulative / total);
            var share = (int)(boundary - previous);
            previous = boundary;
            if (share > 0)
            {
                newDest[n] = _townScratch[i];
                newShare[n] = share;
                n++;
            }
        }

        // Keep carry and statistics of destinations that stay.
        var baseIndex = row * Width;
        Span<long> carry = stackalloc long[Width];
        Span<int> movedThis = stackalloc int[Width];
        Span<int> movedLast = stackalloc int[Width];
        Span<int> unservedThis = stackalloc int[Width];
        Span<int> unservedLast = stackalloc int[Width];
        var oldCount = t.Count[row];
        var changed = oldCount != n;
        for (var i = 0; i < n; i++)
        {
            carry[i] = 0;
            movedThis[i] = movedLast[i] = unservedThis[i] = unservedLast[i] = 0;
            for (var o = 0; o < oldCount; o++)
            {
                if (t.Dest[baseIndex + o] == newDest[i])
                {
                    carry[i] = t.Carry[baseIndex + o];
                    movedThis[i] = t.MovedThis[baseIndex + o];
                    movedLast[i] = t.MovedLast[baseIndex + o];
                    unservedThis[i] = t.UnservedThis[baseIndex + o];
                    unservedLast[i] = t.UnservedLast[baseIndex + o];
                    break;
                }
            }

            changed |= i >= oldCount || t.Dest[baseIndex + i] != newDest[i] || t.Share[baseIndex + i] != newShare[i];
        }

        if (!changed)
        {
            return;
        }

        for (var i = 0; i < Width; i++)
        {
            var c = baseIndex + i;
            var used = i < n;
            t.Dest[c] = used ? newDest[i] : -1;
            t.Share[c] = used ? newShare[i] : 0;
            t.Carry[c] = used ? carry[i] : 0;
            t.MovedThis[c] = used ? movedThis[i] : 0;
            t.MovedLast[c] = used ? movedLast[i] : 0;
            t.UnservedThis[c] = used ? unservedThis[i] : 0;
            t.UnservedLast[c] = used ? unservedLast[i] : 0;
        }

        t.Count[row] = n;
    }

    /// <summary>The tables of one destination cargo, flat by row × width.</summary>
    private sealed partial class RuleTables(DemandCargoRule rule, int width)
    {
        public DemandCargoRule Rule { get; } = rule;

        public Dictionary<int, int> RowOfIndustry { get; } = [];

        public int Rows { get; set; }

        public int Cursor { get; set; }

        public int[] Source { get; private set; } = [];

        public int[] X { get; private set; } = [];

        public int[] Z { get; private set; } = [];

        public int[] Count { get; private set; } = [];

        public int[] Dest { get; private set; } = [];

        public int[] Share { get; private set; } = [];

        public long[] Carry { get; private set; } = [];

        public int[] MovedThis { get; private set; } = [];

        public int[] MovedLast { get; private set; } = [];

        public int[] UnservedThis { get; private set; } = [];

        public int[] UnservedLast { get; private set; } = [];

        public void EnsureRows(int rows)
        {
            if (rows <= Count.Length)
            {
                return;
            }

            var size = Math.Max(rows, Math.Max(8, Count.Length * 2));
            Source = Grow(Source, size);
            X = Grow(X, size);
            Z = Grow(Z, size);
            Count = Grow(Count, size);
            Dest = Grow(Dest, size * width);
            Share = Grow(Share, size * width);
            Carry = Grow(Carry, size * width);
            MovedThis = Grow(MovedThis, size * width);
            MovedLast = Grow(MovedLast, size * width);
            UnservedThis = Grow(UnservedThis, size * width);
            UnservedLast = Grow(UnservedLast, size * width);
        }

        private static T[] Grow<T>(T[] array, int size)
        {
            var copy = new T[size];
            array.CopyTo(copy, 0);
            return copy;
        }
    }
}

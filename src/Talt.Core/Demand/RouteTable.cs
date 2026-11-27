using Talt.Core.Determinism;

namespace Talt.Core.Demand;

/// <summary>
/// Routes of one company and destination cargo (research R8, data-model.md RouteTable): for every station group (by
/// group key) and destination town it can reach, the next group (<c>Hop1</c>), optionally a second next group
/// (<c>Hop2</c>) that takes <c>Share2</c> per mille of the cargo, and the split's carried remainder. Entries are sorted
/// by group key then town; the groups on the company's lines are kept too (nodes), so cargo at a group on no line
/// can be told apart from cargo with no route.
/// </summary>
public sealed class RouteTable : IStateHashable
{
    private int[] _group = new int[16];
    private int[] _town = new int[16];
    private int[] _hop1 = new int[16];
    private int[] _hop2 = new int[16];
    private int[] _share2 = new int[16];
    private int[] _carry = new int[16];
    private int[] _nodes = new int[16];

    public int Company { get; private set; }

    public int Cargo { get; private set; }

    public int Count { get; private set; }

    public int NodeCount { get; private set; }

    public int GroupAt(int index) => _group[index];

    public int TownAt(int index) => _town[index];

    public int Hop1At(int index) => _hop1[index];

    public int Hop2At(int index) => _hop2[index];

    public int Share2At(int index) => _share2[index];

    public int NodeAt(int index) => _nodes[index];

    /// <summary>Clears the table for a new result.</summary>
    public void Reset(int company, int cargo)
    {
        Company = company;
        Cargo = cargo;
        Count = 0;
        NodeCount = 0;
    }

    /// <summary>Adds a node (group key, ascending).</summary>
    public void AddNode(int groupKey)
    {
        if (NodeCount == _nodes.Length)
        {
            Array.Resize(ref _nodes, NodeCount * 2);
        }

        _nodes[NodeCount++] = groupKey;
    }

    /// <summary>Adds an entry; entries must come sorted by (group, town).</summary>
    public void Add(int groupKey, int town, int hop1, int hop2, int share2)
    {
        if (Count == _group.Length)
        {
            var size = Count * 2;
            Array.Resize(ref _group, size);
            Array.Resize(ref _town, size);
            Array.Resize(ref _hop1, size);
            Array.Resize(ref _hop2, size);
            Array.Resize(ref _share2, size);
            Array.Resize(ref _carry, size);
        }

        _group[Count] = groupKey;
        _town[Count] = town;
        _hop1[Count] = hop1;
        _hop2[Count] = hop2 == hop1 ? -1 : hop2;
        _share2[Count] = hop2 < 0 || hop2 == hop1 ? 0 : share2;
        _carry[Count] = 0;
        Count++;
    }

    /// <summary>True when the group is on one of the company's lines for this cargo.</summary>
    public bool HasNode(int groupKey) => Array.BinarySearch(_nodes, 0, NodeCount, groupKey) >= 0;

    /// <summary>The entry of (group, town), or −1 when the town cannot be reached from the group.</summary>
    public int Find(int groupKey, int town)
    {
        int lo = 0, hi = Count - 1;
        while (lo <= hi)
        {
            var mid = (lo + hi) >>> 1;
            var g = _group[mid];
            var cmp = g != groupKey ? g.CompareTo(groupKey) : _town[mid].CompareTo(town);
            if (cmp == 0)
            {
                return mid;
            }

            if (cmp < 0)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }

        return -1;
    }

    /// <summary>
    /// The next group for <paramref name="amount"/> units at an entry: writes how many go to <c>Hop2</c> (the rest go
    /// to <c>Hop1</c>), advancing the entry's carry.
    /// </summary>
    public int SplitToSecond(int index, int amount)
    {
        if (_hop2[index] < 0)
        {
            return 0;
        }

        return Apportion.SplitTwo(amount, _share2[index], ref _carry[index]);
    }

    /// <summary>Copies the carries of entries with the same group, town and hops from an older table.</summary>
    public void InheritCarries(RouteTable older)
    {
        ArgumentNullException.ThrowIfNull(older);
        for (var i = 0; i < Count; i++)
        {
            var o = older.Find(_group[i], _town[i]);
            if (o >= 0 && older._hop1[o] == _hop1[i] && older._hop2[o] == _hop2[i])
            {
                _carry[i] = older._carry[o];
            }
        }
    }

    public void AppendTo(StateHasher hasher)
    {
        ArgumentNullException.ThrowIfNull(hasher);
        hasher.Append(Company);
        hasher.Append(Cargo);
        hasher.Append(NodeCount);
        hasher.Append<int>(_nodes.AsSpan(0, NodeCount));
        hasher.Append(Count);
        hasher.Append<int>(_group.AsSpan(0, Count));
        hasher.Append<int>(_town.AsSpan(0, Count));
        hasher.Append<int>(_hop1.AsSpan(0, Count));
        hasher.Append<int>(_hop2.AsSpan(0, Count));
        hasher.Append<int>(_share2.AsSpan(0, Count));
        hasher.Append<int>(_carry.AsSpan(0, Count));
    }
}

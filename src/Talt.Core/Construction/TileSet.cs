namespace Talt.Core.Construction;

/// <summary>
/// A set of tile indices that maps each member to a dense slot (0, 1, 2... in insertion order), for
/// the planner's scratch data (research R8). Open addressing with linear probing; a generation stamp
/// per bucket makes <see cref="Clear"/> O(1), so reusing the set between previews costs nothing and
/// allocates nothing once its capacity is reached.
/// </summary>
public sealed class TileSet
{
    private int[] _keys = [];
    private int[] _slots = [];
    private int[] _stamps = [];
    private int[] _members = [];
    private int _mask;
    private int _stamp = 1;

    public TileSet(int capacity = 1024) => Grow(Math.Max(16, capacity));

    /// <summary>Number of members.</summary>
    public int Count { get; private set; }

    /// <summary>Tile index of the member in a slot.</summary>
    public int this[int slot] => _members[slot];

    /// <summary>Removes every member without touching the buckets.</summary>
    public void Clear()
    {
        Count = 0;
        if (++_stamp == int.MaxValue)
        {
            Array.Clear(_stamps);
            _stamp = 1;
        }
    }

    /// <summary>Slot of a tile, or -1 when it is not a member.</summary>
    public int Find(int tile)
    {
        var bucket = Hash(tile) & _mask;
        while (_stamps[bucket] == _stamp)
        {
            if (_keys[bucket] == tile)
            {
                return _slots[bucket];
            }

            bucket = (bucket + 1) & _mask;
        }

        return -1;
    }

    /// <summary>Adds a tile if missing and returns its slot; <paramref name="added"/> tells which happened.</summary>
    public int Add(int tile, out bool added)
    {
        if ((Count + 1) * 2 > _keys.Length)
        {
            Grow(_keys.Length * 2);
        }

        var bucket = Hash(tile) & _mask;
        while (_stamps[bucket] == _stamp)
        {
            if (_keys[bucket] == tile)
            {
                added = false;
                return _slots[bucket];
            }

            bucket = (bucket + 1) & _mask;
        }

        _stamps[bucket] = _stamp;
        _keys[bucket] = tile;
        var slot = Count++;
        _slots[bucket] = slot;
        if (slot >= _members.Length)
        {
            Array.Resize(ref _members, Math.Max(16, _members.Length * 2));
        }

        _members[slot] = tile;
        added = true;
        return slot;
    }

    /// <summary>Makes sure <paramref name="members"/> tiles fit without growing later.</summary>
    public void EnsureCapacity(int members)
    {
        var buckets = 16;
        while (buckets < members * 2)
        {
            buckets *= 2;
        }

        if (buckets > _keys.Length)
        {
            Grow(buckets);
        }

        if (_members.Length < members)
        {
            Array.Resize(ref _members, members);
        }
    }

    private static int Hash(int tile) => (int)((uint)tile * 0x9E3779B1u >> 7) ^ tile;

    private void Grow(int buckets)
    {
        var size = 16;
        while (size < buckets)
        {
            size *= 2;
        }

        var oldMembers = _members;
        var count = Count;
        _keys = new int[size];
        _slots = new int[size];
        _stamps = new int[size];
        _mask = size - 1;
        _stamp = 1;
        Count = 0;
        _members = oldMembers.Length >= size / 2 ? oldMembers : new int[Math.Max(16, size / 2)];
        for (var slot = 0; slot < count; slot++)
        {
            // Re-insert in slot order so slots stay the same.
            var tile = oldMembers[slot];
            var bucket = Hash(tile) & _mask;
            while (_stamps[bucket] == _stamp)
            {
                bucket = (bucket + 1) & _mask;
            }

            _stamps[bucket] = _stamp;
            _keys[bucket] = tile;
            _slots[bucket] = slot;
            _members[slot] = tile;
        }

        Count = count;
    }
}

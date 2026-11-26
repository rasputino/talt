namespace Talt.Core.Vehicles;

/// <summary>
/// Lane key → lane index map for the drive step (research R2): open addressing with linear probing over two flat
/// arrays, load factor at most one half. About three times faster than a dictionary for the lookups every vehicle
/// makes each tick, and safe for concurrent readers while nobody writes (the parallel step only reads). Keys are
/// non-negative; entries are never removed. Derived data, never hashed.
/// </summary>
internal sealed class LaneIndexMap
{
    private int[] _keys = new int[1024];
    private int[] _values = new int[1024];
    private int _shift = 32 - 10;

    public LaneIndexMap() => Array.Fill(_keys, -1);

    public int Count { get; private set; }

    /// <summary>The index of a key, or −1.</summary>
    public int Find(int key)
    {
        var mask = _keys.Length - 1;
        var i = (int)((uint)(key * -1640531535) >> _shift);
        while (true)
        {
            var k = _keys[i];
            if (k == key)
            {
                return _values[i];
            }

            if (k < 0)
            {
                return -1;
            }

            i = (i + 1) & mask;
        }
    }

    /// <summary>Adds a key that is not in the map yet.</summary>
    public void Add(int key, int value)
    {
        if ((Count + 1) * 2 > _keys.Length)
        {
            Grow();
        }

        Insert(key, value);
        Count++;
    }

    private void Insert(int key, int value)
    {
        var mask = _keys.Length - 1;
        var i = (int)((uint)(key * -1640531535) >> _shift);
        while (_keys[i] >= 0)
        {
            i = (i + 1) & mask;
        }

        _keys[i] = key;
        _values[i] = value;
    }

    private void Grow()
    {
        var keys = _keys;
        var values = _values;
        _keys = new int[keys.Length * 2];
        _values = new int[keys.Length * 2];
        Array.Fill(_keys, -1);
        _shift--;
        for (var i = 0; i < keys.Length; i++)
        {
            if (keys[i] >= 0)
            {
                Insert(keys[i], values[i]);
            }
        }
    }
}

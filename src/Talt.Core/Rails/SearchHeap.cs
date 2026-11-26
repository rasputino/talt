namespace Talt.Core.Rails;

/// <summary>
/// Binary min-heap of search states for the rail planner and router (reused buffers, no allocation once grown). Ties go
/// to the deeper state (higher cost so far), then to the lower state number, so searches are deterministic.
/// </summary>
internal sealed class SearchHeap
{
    private long[] _f = new long[1024];
    private long[] _g = new long[1024];
    private int[] _s = new int[1024];

    public int Count { get; private set; }

    public void Clear() => Count = 0;

    public void Push(long f, long g, int state)
    {
        if (Count == _f.Length)
        {
            Array.Resize(ref _f, Count * 2);
            Array.Resize(ref _g, Count * 2);
            Array.Resize(ref _s, Count * 2);
        }

        var index = Count++;
        while (index > 0)
        {
            var parent = (index - 1) >> 1;
            if (!Less(f, g, state, _f[parent], _g[parent], _s[parent]))
            {
                break;
            }

            _f[index] = _f[parent];
            _g[index] = _g[parent];
            _s[index] = _s[parent];
            index = parent;
        }

        _f[index] = f;
        _g[index] = g;
        _s[index] = state;
    }

    public (long G, int State) Pop()
    {
        var g = _g[0];
        var s = _s[0];
        var count = --Count;
        if (count > 0)
        {
            var lf = _f[count];
            var lg = _g[count];
            var ls = _s[count];
            var index = 0;
            while (true)
            {
                var child = (index << 1) + 1;
                if (child >= count)
                {
                    break;
                }

                if (child + 1 < count && Less(_f[child + 1], _g[child + 1], _s[child + 1], _f[child], _g[child], _s[child]))
                {
                    child++;
                }

                if (!Less(_f[child], _g[child], _s[child], lf, lg, ls))
                {
                    break;
                }

                _f[index] = _f[child];
                _g[index] = _g[child];
                _s[index] = _s[child];
                index = child;
            }

            _f[index] = lf;
            _g[index] = lg;
            _s[index] = ls;
        }

        return (g, s);
    }

    private static bool Less(long fa, long ga, int sa, long fb, long gb, int sb) =>
        fa < fb || (fa == fb && (ga > gb || (ga == gb && sa < sb)));
}

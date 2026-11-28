
namespace Talt.Core.Saves;

/// <summary>
/// A growable byte buffer made of pooled <see cref="SegmentSize"/> segments (data-model.md SaveBuffer). Section
/// writers append to it during capture without allocating once the pool is warm; one segment is exactly one
/// compressed block of the file (contracts/save-format.md), so reading decompresses each block into one segment.
/// </summary>
public sealed class SaveBuffer : IDisposable
{
    /// <summary>Uncompressed bytes per segment and per file block (4 MiB).</summary>
    public const int SegmentSize = 4 << 20;

    private readonly List<byte[]> _segments = new(16);
    private int _lastUsed;

    /// <summary>Total bytes written.</summary>
    public long Length => _segments.Count == 0 ? 0 : ((long)(_segments.Count - 1) * SegmentSize) + _lastUsed;

    /// <summary>Number of segments in use.</summary>
    public int SegmentCount => _segments.Count;

    /// <summary>The used bytes of segment <paramref name="index"/>.</summary>
    public ReadOnlySpan<byte> Segment(int index) => _segments[index].AsSpan(0, SegmentLength(index));

    /// <summary>The used length of segment <paramref name="index"/>.</summary>
    public int SegmentLength(int index) => index == _segments.Count - 1 ? _lastUsed : SegmentSize;

    /// <summary>The raw array behind segment <paramref name="index"/> (compression workers).</summary>
    internal byte[] SegmentArray(int index) => _segments[index];

    /// <summary>Free space in the current segment, renting a new one when it is full. Never empty.</summary>
    internal Span<byte> GetSpan()
    {
        if (_segments.Count == 0 || _lastUsed == SegmentSize)
        {
            _segments.Add(SaveBufferPool.Rent());
            _lastUsed = 0;
        }

        return _segments[^1].AsSpan(_lastUsed);
    }

    /// <summary>Marks <paramref name="count"/> bytes of the span returned by <see cref="GetSpan"/> as written.</summary>
    internal void Advance(int count)
    {
        if (count < 0 || _lastUsed + count > SegmentSize)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        _lastUsed += count;
    }

    /// <summary>Appends a full or final segment filled by a reader (loading); takes ownership of the array.</summary>
    internal void Append(byte[] segment, int length)
    {
        ArgumentNullException.ThrowIfNull(segment);
        if (_segments.Count > 0 && _lastUsed != SegmentSize)
        {
            throw new InvalidOperationException("Only the last segment may be shorter than a full segment.");
        }

        if (length < 0 || length > SegmentSize || segment.Length != SegmentSize)
        {
            throw new ArgumentOutOfRangeException(nameof(length));
        }

        _segments.Add(segment);
        _lastUsed = length;
    }

    /// <summary>Returns every segment to the pool; the buffer can be written again.</summary>
    public void Clear()
    {
        foreach (var segment in _segments)
        {
            SaveBufferPool.Return(segment);
        }

        _segments.Clear();
        _lastUsed = 0;
    }

    public void Dispose() => Clear();
}

/// <summary>
/// The shared pool of <see cref="SaveBuffer.SegmentSize"/> arrays (data-model.md SaveBufferPool). Saving rents
/// several hundred megabytes for a large map at once; <see cref="Trim"/> releases them after the file is written
/// so the peak does not stay resident.
/// </summary>
public static class SaveBufferPool
{
    // A plain stack under a lock: a concurrent stack allocates a node per push, and capture must not allocate.
    private static readonly Stack<byte[]> Free = new();
    private static readonly Lock Gate = new();

    /// <summary>Segments currently held by the pool (tests).</summary>
    public static int FreeCount
    {
        get
        {
            lock (Gate)
            {
                return Free.Count;
            }
        }
    }

    internal static byte[] Rent()
    {
        lock (Gate)
        {
            if (Free.TryPop(out var segment))
            {
                return segment;
            }
        }

        return GC.AllocateUninitializedArray<byte>(SaveBuffer.SegmentSize);
    }

    internal static void Return(byte[] segment)
    {
        if (segment.Length == SaveBuffer.SegmentSize)
        {
            lock (Gate)
            {
                Free.Push(segment);
            }
        }
    }

    /// <summary>Keeps at most <paramref name="keep"/> free segments and lets the garbage collector have the rest.</summary>
    public static void Trim(int keep = 4)
    {
        lock (Gate)
        {
            while (Free.Count > keep)
            {
                Free.Pop();
            }

            Free.TrimExcess();
        }
    }
}

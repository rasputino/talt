using System.Buffers;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Talt.Core.Commands;
using System.Runtime.InteropServices;
using System.Text;
using Talt.Core.Determinism;

namespace Talt.Core.Saves;

/// <summary>
/// Explicit little-endian primitives every section writes with (data-model.md SaveWriter, research R1). Values of
/// unmanaged types are copied as their in-memory bytes, which is the same on every supported platform (all
/// little-endian, same struct layout; the state fingerprint relies on the same property). Writing allocates
/// nothing once the segment pool is warm.
/// </summary>
public sealed class SaveWriter : IDisposable
{
    private readonly SaveBuffer _buffer;
    private readonly Dictionary<Type, Array> _sortBuffers = [];

    internal T[] SortBuffer<T>(int count)
    {
        if (_sortBuffers.TryGetValue(typeof(T), out var array) && array.Length >= count)
        {
            return (T[])array;
        }

        var buffer = new T[count];
        _sortBuffers[typeof(T)] = buffer;
        return buffer;
    }

    public SaveWriter(SaveBuffer buffer)
    {
        if (!BitConverter.IsLittleEndian)
        {
            throw new PlatformNotSupportedException("Saves are written on little-endian platforms only.");
        }

        _buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
    }

    /// <summary>Bytes written so far.</summary>
    public long Position => _buffer.Length;

    public void WriteBool(bool value) => WriteByte(value ? (byte)1 : (byte)0);

    public void WriteByte(byte value) => Write(value);

    public void WriteInt(int value) => Write(value);

    public void WriteLong(long value) => Write(value);

    public void WriteULong(ulong value) => Write(value);

    /// <summary>Writes one unmanaged value as its bytes.</summary>
    public void Write<T>(T value)
        where T : unmanaged => WriteRaw(MemoryMarshal.AsBytes(new ReadOnlySpan<T>(ref value)));

    private ArrayBufferWriter<byte>? _commandBytes;
    private Utf8JsonWriter? _commandJson;

    public void Dispose() => _commandJson?.Dispose();

    internal void WriteCommand(ISimulationCommand command)
    {
        _commandBytes ??= new ArrayBufferWriter<byte>();
        _commandBytes.ResetWrittenCount();
        if (_commandJson is null)
        {
            _commandJson = new Utf8JsonWriter(_commandBytes);
        }
        else
        {
            _commandJson.Reset(_commandBytes);
        }

        CommandSerializer.Write(_commandJson, command);
        _commandJson.Flush();
        WriteInt(_commandBytes.WrittenCount);
        WriteRaw(_commandBytes.WrittenSpan);
    }

    /// <summary>Writes a string as a UTF-8 byte count (−1 for null) and its bytes.</summary>
    public void WriteString(string? value)
    {
        if (value is null)
        {
            WriteInt(-1);
            return;
        }

        var length = Encoding.UTF8.GetByteCount(value);
        WriteInt(length);
        if (length == 0)
        {
            return;
        }

        var target = _buffer.GetSpan();
        if (target.Length >= length)
        {
            Encoding.UTF8.GetBytes(value, target);
            _buffer.Advance(length);
            return;
        }

        // Rare: the string crosses a segment boundary; encode in pieces of whole characters.
        var chars = value.AsSpan();
        Span<byte> scratch = stackalloc byte[256];
        while (!chars.IsEmpty)
        {
            var take = Math.Min(chars.Length, 64);
            if (char.IsHighSurrogate(chars[take - 1]) && take < chars.Length)
            {
                take--;
            }

            var written = Encoding.UTF8.GetBytes(chars[..take], scratch);
            WriteRaw(scratch[..written]);
            chars = chars[take..];
        }
    }

    /// <summary>Writes an array as its length (−1 for null) and all its elements, full length (research R1).</summary>
    public void WriteArray<T>(T[]? array)
        where T : unmanaged
    {
        if (array is null)
        {
            WriteInt(-1);
            return;
        }

        WriteSpan<T>(array);
    }

    // Map-sized layers often contain only a few occupied pages (rail track, blocks and reservations).
    internal const int PageElements = 4096;

    internal void WritePages<T>(T[] values, T empty) where T : unmanaged, IEquatable<T>
    {
        WriteInt(values.Length);
        for (var offset = 0; offset < values.Length; offset += PageElements)
        {
            var page = values.AsSpan(offset, Math.Min(PageElements, values.Length - offset));
            var occupied = page.ContainsAnyExcept(empty);
            WriteBool(occupied);
            if (occupied)
            {
                WriteRaw(MemoryMarshal.AsBytes(page));
            }
        }
    }

    /// <summary>Writes a span as its length and its elements.</summary>
    public void WriteSpan<T>(ReadOnlySpan<T> values)
        where T : unmanaged
    {
        WriteInt(values.Length);
        WriteRaw(MemoryMarshal.AsBytes(values));
    }

    /// <summary>Writes a list's live elements as a span (its capacity is not state).</summary>
    public void WriteList<T>(List<T> list)
        where T : unmanaged
    {
        ArgumentNullException.ThrowIfNull(list);
        WriteSpan<T>(CollectionsMarshal.AsSpan(list));
    }

    /// <summary>Writes raw bytes without a length.</summary>
    public void WriteRaw(ReadOnlySpan<byte> bytes)
    {
        while (!bytes.IsEmpty)
        {
            var target = _buffer.GetSpan();
            var count = Math.Min(target.Length, bytes.Length);
            bytes[..count].CopyTo(target);
            _buffer.Advance(count);
            bytes = bytes[count..];
        }
    }

    /// <summary>Writes a random stream's four state words.</summary>
    public void WriteRandom(DeterministicRandom random)
    {
        ArgumentNullException.ThrowIfNull(random);
        var (s0, s1, s2, s3) = random.State;
        WriteULong(s0);
        WriteULong(s1);
        WriteULong(s2);
        WriteULong(s3);
    }

    /// <summary>
    /// Writes a lookup dictionary's entries sorted by key (research R3): the simulation never enumerates it, so its
    /// enumeration order is not state, and sorting makes the bytes independent of the holes removals left behind.
    /// </summary>
    public void WriteDictionary<TKey, TValue>(Dictionary<TKey, TValue> dictionary)
        where TKey : unmanaged, IComparable<TKey>
        where TValue : unmanaged
    {
        ArgumentNullException.ThrowIfNull(dictionary);
        WriteInt(dictionary.Count);
        if (dictionary.Count == 0)
        {
            return;
        }

        var keys = SortBuffer<TKey>(dictionary.Count);
        dictionary.Keys.CopyTo(keys, 0);
        Array.Sort(keys, 0, dictionary.Count);
        for (var i = 0; i < dictionary.Count; i++)
        {
            Write(keys[i]);
            Write(dictionary[keys[i]]);
        }
    }

    /// <summary>Writes a lookup dictionary with reference values sorted by key, each value by <paramref name="writeValue"/> (research R3).</summary>
    public void WriteDictionary<TKey, TValue>(Dictionary<TKey, TValue> dictionary, Action<SaveWriter, TValue> writeValue)
        where TKey : unmanaged, IComparable<TKey>
    {
        ArgumentNullException.ThrowIfNull(dictionary);
        ArgumentNullException.ThrowIfNull(writeValue);
        WriteInt(dictionary.Count);
        if (dictionary.Count == 0)
        {
            return;
        }

        var keys = SortBuffer<TKey>(dictionary.Count);
        dictionary.Keys.CopyTo(keys, 0);
        Array.Sort(keys, 0, dictionary.Count);
        for (var i = 0; i < dictionary.Count; i++)
        {
            Write(keys[i]);
            writeValue(this, dictionary[keys[i]]);
        }
    }

    /// <summary>
    /// Writes a dictionary the simulation enumerates in its enumeration order (research R3); reading inserts the entries in
    /// that order, so the loaded dictionary enumerates identically.
    /// </summary>
    public void WriteDictionaryInOrder<TKey, TValue>(Dictionary<TKey, TValue> dictionary)
        where TKey : unmanaged
        where TValue : unmanaged
    {
        ArgumentNullException.ThrowIfNull(dictionary);
        WriteInt(dictionary.Count);
        foreach (var (key, value) in dictionary)
        {
            Write(key);
            Write(value);
        }
    }

    /// <summary>Writes a lookup set's items sorted (research R3).</summary>
    public void WriteSet<T>(HashSet<T> set)
        where T : unmanaged, IComparable<T>
    {
        ArgumentNullException.ThrowIfNull(set);
        WriteInt(set.Count);
        if (set.Count == 0)
        {
            return;
        }

        var items = SortBuffer<T>(set.Count);
        set.CopyTo(items);
        Array.Sort(items, 0, set.Count);
        for (var i = 0; i < set.Count; i++)
        {
            Write(items[i]);
        }
    }

    /// <summary>Writes a section-local marker that the reader checks (catches writer/reader drift early).</summary>
    public void WriteMarker(uint marker) => Write(marker);

    /// <summary>Size of <typeparamref name="T"/> in bytes.</summary>
    internal static int SizeOf<T>()
        where T : unmanaged => Unsafe.SizeOf<T>();
}

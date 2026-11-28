using System.Runtime.InteropServices;
using System.Text;
using Talt.Core.Determinism;

namespace Talt.Core.Saves;

/// <summary>
/// Reads what <see cref="SaveWriter"/> wrote, from a section's decompressed segments (data-model.md SaveReader).
/// Every malformed value (length beyond the section, wrong fixed length, bad marker) throws
/// <see cref="SaveFormatException"/> naming the section and the offset; loading turns it into a "damaged" status.
/// </summary>
public sealed class SaveReader
{
    private readonly SaveBuffer _buffer;
    private int _segment;
    private int _offset;
    private long _position;

    public SaveReader(SaveBuffer buffer, SaveSectionId section, ContentMap? content = null)
    {
        _buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
        Section = section;
        Content = content ?? ContentMap.Identity;
    }

    /// <summary>The section being read (error messages).</summary>
    public SaveSectionId Section { get; }

    /// <summary>Saved content index → installed content index (research R10).</summary>
    public ContentMap Content { get; }

    /// <summary>Bytes read so far.</summary>
    public long Position => _position;

    /// <summary>Bytes left in the section.</summary>
    public long Remaining => _buffer.Length - _position;

    public bool ReadBool()
    {
        var value = ReadByte();
        return value switch
        {
            0 => false,
            1 => true,
            _ => throw Fail($"invalid boolean {value}"),
        };
    }

    public byte ReadByte() => Read<byte>();

    public int ReadInt() => Read<int>();

    public long ReadLong() => Read<long>();

    public ulong ReadULong() => Read<ulong>();

    /// <summary>Reads one unmanaged value.</summary>
    public T Read<T>()
        where T : unmanaged
    {
        T value = default;
        ReadRaw(MemoryMarshal.AsBytes(new Span<T>(ref value)));
        return value;
    }

    /// <summary>Reads an int that must lie in [<paramref name="min"/>, <paramref name="max"/>].</summary>
    public int ReadInt(int min, int max)
    {
        var value = ReadInt();
        if (value < min || value > max)
        {
            throw Fail($"value {value} outside [{min}, {max}]");
        }

        return value;
    }

    public string? ReadString()
    {
        var length = ReadInt();
        if (length == -1)
        {
            return null;
        }

        CheckLength(length, 1);
        if (length == 0)
        {
            return string.Empty;
        }

        Span<byte> bytes = length <= 1024 ? stackalloc byte[length] : new byte[length];
        ReadRaw(bytes);
        try
        {
            return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes);
        }
        catch (DecoderFallbackException e)
        {
            throw new SaveFormatException($"Section {Section} at {_position}: invalid UTF-8 text.", e);
        }
    }

    /// <summary>Reads a string that must not be null.</summary>
    public string ReadRequiredString() => ReadString() ?? throw Fail("unexpected null text");

    /// <summary>Reads an array written by <see cref="SaveWriter.WriteArray{T}"/> (null when −1 was written).</summary>
    public T[]? ReadArray<T>()
        where T : unmanaged
    {
        var length = ReadInt();
        if (length == -1)
        {
            return null;
        }

        CheckLength(length, SaveWriter.SizeOf<T>());
        var array = length == 0 ? [] : GC.AllocateUninitializedArray<T>(length);
        ReadRaw(MemoryMarshal.AsBytes(array.AsSpan()));
        return array;
    }

    /// <summary>Reads an array that must not be null.</summary>
    public T[] ReadRequiredArray<T>()
        where T : unmanaged => ReadArray<T>() ?? throw Fail("unexpected null array");

    /// <summary>Restores a paged map layer or its dense version 1 representation.</summary>
    internal void ReadPages<T>(T[] target, T empty, int version) where T : unmanaged
    {
        if (version == 1)
        {
            ReadInto<T>(target);
            return;
        }

        ReadInt(target.Length, target.Length);
        for (var offset = 0; offset < target.Length; offset += SaveWriter.PageElements)
        {
            var page = target.AsSpan(offset, Math.Min(SaveWriter.PageElements, target.Length - offset));
            if (ReadBool())
            {
                ReadRaw(MemoryMarshal.AsBytes(page));
            }
            else
            {
                page.Fill(empty);
            }
        }
    }

    public void ReadInto<T>(Span<T> target)
        where T : unmanaged
    {
        var length = ReadInt();
        if (length != target.Length)
        {
            throw Fail($"expected {target.Length} elements, found {length}");
        }

        ReadRaw(MemoryMarshal.AsBytes(target));
    }

    /// <summary>Replaces a list's contents with the saved elements.</summary>
    public void ReadListInto<T>(List<T> list)
        where T : unmanaged
    {
        ArgumentNullException.ThrowIfNull(list);
        var length = ReadInt();
        CheckLength(length, SaveWriter.SizeOf<T>());
        list.Clear();
        CollectionsMarshal.SetCount(list, length);
        ReadRaw(MemoryMarshal.AsBytes(CollectionsMarshal.AsSpan(list)));
    }

    /// <summary>Reads raw bytes written by <see cref="SaveWriter.WriteRaw"/>.</summary>
    public void ReadRaw(Span<byte> target)
    {
        if (target.Length > Remaining)
        {
            throw Fail($"needs {target.Length} bytes, {Remaining} left");
        }

        while (!target.IsEmpty)
        {
            var source = _buffer.Segment(_segment)[_offset..];
            if (source.IsEmpty)
            {
                _segment++;
                _offset = 0;
                continue;
            }

            var count = Math.Min(source.Length, target.Length);
            source[..count].CopyTo(target);
            target = target[count..];
            _offset += count;
            _position += count;
        }
    }

    /// <summary>Overwrites a random stream with the saved state words.</summary>
    public void ReadRandomInto(DeterministicRandom random)
    {
        ArgumentNullException.ThrowIfNull(random);
        var s0 = ReadULong();
        var s1 = ReadULong();
        var s2 = ReadULong();
        var s3 = ReadULong();
        if ((s0 | s1 | s2 | s3) == 0)
        {
            throw Fail("all-zero random state");
        }

        random.Restore(s0, s1, s2, s3);
    }

    /// <summary>Reads a new random stream.</summary>
    public DeterministicRandom ReadRandom()
    {
        var random = new DeterministicRandom(1);
        ReadRandomInto(random);
        return random;
    }

    /// <summary>Clears <paramref name="dictionary"/> and inserts the saved entries in their saved order (research R3).</summary>
    public void ReadDictionaryInto<TKey, TValue>(Dictionary<TKey, TValue> dictionary)
        where TKey : unmanaged
        where TValue : unmanaged
    {
        ArgumentNullException.ThrowIfNull(dictionary);
        var count = ReadCount(SaveWriter.SizeOf<TKey>() + SaveWriter.SizeOf<TValue>());
        dictionary.Clear();
        dictionary.EnsureCapacity(count);
        for (var i = 0; i < count; i++)
        {
            var key = Read<TKey>();
            var value = Read<TValue>();
            if (!dictionary.TryAdd(key, value))
            {
                throw Fail("duplicate dictionary key");
            }
        }
    }

    /// <summary>Clears <paramref name="set"/> and inserts the saved items in their saved order (research R3).</summary>
    public void ReadSetInto<T>(HashSet<T> set)
        where T : unmanaged
    {
        ArgumentNullException.ThrowIfNull(set);
        var count = ReadCount(SaveWriter.SizeOf<T>());
        set.Clear();
        set.EnsureCapacity(count);
        for (var i = 0; i < count; i++)
        {
            if (!set.Add(Read<T>()))
            {
                throw Fail("duplicate set item");
            }
        }
    }

    /// <summary>Reads an element count whose elements take at least <paramref name="minElementSize"/> bytes each.</summary>
    public int ReadCount(int minElementSize = 1)
    {
        var count = ReadInt();
        CheckLength(count, Math.Max(1, minElementSize));
        return count;
    }

    /// <summary>Checks a marker written by <see cref="SaveWriter.WriteMarker"/>.</summary>
    public void ExpectMarker(uint marker)
    {
        var found = Read<uint>();
        if (found != marker)
        {
            throw Fail($"expected marker {marker:X8}, found {found:X8}");
        }
    }

    /// <summary>Fails unless the whole section was read (contracts/save-format.md).</summary>
    public void ExpectEnd()
    {
        if (Remaining != 0)
        {
            throw Fail($"{Remaining} unread bytes at the end of the section");
        }
    }

    /// <summary>An exception describing a malformed value at the current position.</summary>
    public SaveFormatException Fail(string reason) => new($"Section {Section} at byte {_position}: {reason}.");

    private void CheckLength(int length, int elementSize)
    {
        if (length < 0 || (long)length * elementSize > Remaining)
        {
            throw Fail($"length {length} does not fit in the section");
        }
    }
}

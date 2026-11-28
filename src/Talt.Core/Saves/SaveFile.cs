using System.Buffers;
using System.Buffers.Binary;
using System.IO.Compression;
using System.IO.Hashing;

namespace Talt.Core.Saves;

/// <summary>Block codecs of the save format (contracts/save-format.md).</summary>
public enum SaveCodec : byte
{
    Stored = 0,
    Deflate = 1,
    Brotli = 2,
}

/// <summary>One section's id, version and uncompressed payload.</summary>
public sealed record SaveSectionData(SaveSectionId Id, int Version, SaveBuffer Payload);

/// <summary>The thumbnail pixels (RGB24, row-major) and size (data-model.md SaveThumbnail).</summary>
public sealed record SaveThumbnailData(int Width, int Height, byte[] Pixels);

/// <summary>What <see cref="SaveFile.Read"/> returns: the details JSON, the thumbnail and every section's payload.</summary>
public sealed class SaveFileContents : IDisposable
{
    internal SaveFileContents(byte[] details, SaveThumbnailData? thumbnail, IReadOnlyList<SaveSectionData> sections)
    {
        DetailsJson = details;
        Thumbnail = thumbnail;
        Sections = sections;
    }

    public byte[] DetailsJson { get; }

    public SaveThumbnailData? Thumbnail { get; }

    public IReadOnlyList<SaveSectionData> Sections { get; }

    public void Dispose()
    {
        foreach (var section in Sections)
        {
            section.Payload.Dispose();
        }
    }
}

/// <summary>
/// Reads and writes the <c>.talt</c> container, version 1 (contracts/save-format.md, research R4–R6): fixed header,
/// details JSON, compressed thumbnail, sections made of independently compressed 4 MiB blocks, and a section table
/// at the end. Every part carries an XxHash3 checksum and <see cref="Read"/> verifies all of them before it
/// decompresses anything.
/// </summary>
public static class SaveFile
{
    /// <summary>ASCII "TALTSAVE", little-endian.</summary>
    public const ulong Magic = 0x4556_4153_544C_4154UL;

    /// <summary>The container version this build writes and the newest it reads.</summary>
    public const int ContainerVersion = 1;

    public const int HeaderLength = 64;

    /// <summary>Largest details JSON accepted (contracts/save-format.md).</summary>
    public const int MaxDetailsLength = 64 * 1024;

    private const int TableEntryHeader = 4 + 2 + 2 + 8 + 4;
    private const int TableBlockEntry = 8 + 4 + 4 + 1 + 3 + 8;

    /// <summary>Writes a complete save to a seekable stream.</summary>
    /// <param name="codec">Codec for the section blocks and the thumbnail.</param>
    public static void Write(Stream stream, ReadOnlySpan<byte> detailsJson, SaveThumbnailData? thumbnail, IReadOnlyList<SaveSectionData> sections, SaveCodec codec = SaveCodec.Deflate)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(sections);
        if (!stream.CanSeek || !stream.CanWrite)
        {
            throw new ArgumentException("Saves are written to seekable streams.", nameof(stream));
        }

        if (detailsJson.Length > MaxDetailsLength)
        {
            throw new ArgumentException("The details block is too large.", nameof(detailsJson));
        }

        var start = stream.Position;
        Span<byte> header = stackalloc byte[HeaderLength];
        header.Clear();
        stream.Write(header);

        // Details.
        var detailsOffset = stream.Position - start;
        Span<byte> number = stackalloc byte[8];
        BinaryPrimitives.WriteInt32LittleEndian(number, detailsJson.Length);
        stream.Write(number[..4]);
        stream.Write(detailsJson);
        BinaryPrimitives.WriteUInt64LittleEndian(number, XxHash3.HashToUInt64(detailsJson));
        stream.Write(number);
        var detailsLength = (int)(stream.Position - start - detailsOffset);

        // Thumbnail.
        long thumbnailOffset = 0;
        var thumbnailLength = 0;
        if (thumbnail is not null)
        {
            thumbnailOffset = stream.Position - start;
            var compressed = Compress(thumbnail.Pixels, codec);
            Span<byte> thumbHeader = stackalloc byte[9];
            BinaryPrimitives.WriteUInt16LittleEndian(thumbHeader, checked((ushort)thumbnail.Width));
            BinaryPrimitives.WriteUInt16LittleEndian(thumbHeader[2..], checked((ushort)thumbnail.Height));
            thumbHeader[4] = (byte)codec;
            BinaryPrimitives.WriteInt32LittleEndian(thumbHeader[5..], compressed.Length);
            stream.Write(thumbHeader);
            stream.Write(compressed);
            BinaryPrimitives.WriteUInt64LittleEndian(number, XxHash3.HashToUInt64(compressed));
            stream.Write(number);
            thumbnailLength = (int)(stream.Position - start - thumbnailOffset);
        }

        // Sections: blocks compressed in parallel, written in order.
        var entries = new List<(SaveSectionData Section, List<BlockEntry> Blocks)>(sections.Count);
        foreach (var section in sections)
        {
            entries.Add((section, new List<BlockEntry>(section.Payload.SegmentCount)));
        }

        var jobs = new List<(int Section, int Segment)>();
        for (var s = 0; s < sections.Count; s++)
        {
            for (var g = 0; g < sections[s].Payload.SegmentCount; g++)
            {
                jobs.Add((s, g));
            }
        }

        var batch = Math.Min(jobs.Count, Math.Max(2, Environment.ProcessorCount * 2));
        var outputs = new byte[batch][];
        var lengths = new int[batch];
        try
        {
            for (var b = 0; b < outputs.Length; b++)
            {
                var maxLength = 0;
                for (var j = b; j < jobs.Count; j += batch)
                {
                    var (section, segment) = jobs[j];
                    maxLength = Math.Max(maxLength, sections[section].Payload.SegmentLength(segment));
                }

                outputs[b] = ArrayPool<byte>.Shared.Rent(MaxCompressedLength(maxLength));
            }

            for (var first = 0; first < jobs.Count; first += batch)
            {
                var count = Math.Min(batch, jobs.Count - first);
                Parallel.For(0, count, i =>
                {
                    var (s, g) = jobs[first + i];
                    lengths[i] = CompressInto(sections[s].Payload.Segment(g), outputs[i], codec);
                });
                for (var i = 0; i < count; i++)
                {
                    var (s, g) = jobs[first + i];
                    var bytes = outputs[i].AsSpan(0, lengths[i]);
                    entries[s].Blocks.Add(new BlockEntry(stream.Position - start, lengths[i], sections[s].Payload.SegmentLength(g), codec, XxHash3.HashToUInt64(bytes)));
                    stream.Write(bytes);
                }
            }
        }
        finally
        {
            foreach (var output in outputs)
            {
                if (output is not null)
                {
                    ArrayPool<byte>.Shared.Return(output);
                }
            }
        }

        // Table.
        var tableOffset = stream.Position - start;
        var tableSize = 4 + 8;
        foreach (var (_, blocks) in entries)
        {
            tableSize += TableEntryHeader + (blocks.Count * TableBlockEntry);
        }

        var table = new byte[tableSize];
        var t = table.AsSpan();
        BinaryPrimitives.WriteInt32LittleEndian(t, entries.Count);
        var at = 4;
        foreach (var (section, blocks) in entries)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(t[at..], section.Id.Value);
            BinaryPrimitives.WriteUInt16LittleEndian(t[(at + 4)..], checked((ushort)section.Version));
            BinaryPrimitives.WriteUInt64LittleEndian(t[(at + 8)..], (ulong)section.Payload.Length);
            BinaryPrimitives.WriteInt32LittleEndian(t[(at + 16)..], blocks.Count);
            at += TableEntryHeader;
            foreach (var block in blocks)
            {
                BinaryPrimitives.WriteInt64LittleEndian(t[at..], block.Offset);
                BinaryPrimitives.WriteInt32LittleEndian(t[(at + 8)..], block.CompressedLength);
                BinaryPrimitives.WriteInt32LittleEndian(t[(at + 12)..], block.UncompressedLength);
                t[at + 16] = (byte)block.Codec;
                BinaryPrimitives.WriteUInt64LittleEndian(t[(at + 20)..], block.Checksum);
                at += TableBlockEntry;
            }
        }

        BinaryPrimitives.WriteUInt64LittleEndian(t[at..], XxHash3.HashToUInt64(t[..at]));
        stream.Write(table);
        var end = stream.Position;

        // Header last, now that every offset is known.
        BinaryPrimitives.WriteUInt64LittleEndian(header, Magic);
        BinaryPrimitives.WriteUInt16LittleEndian(header[8..], ContainerVersion);
        BinaryPrimitives.WriteUInt16LittleEndian(header[10..], 0);
        BinaryPrimitives.WriteInt32LittleEndian(header[12..], HeaderLength);
        BinaryPrimitives.WriteInt64LittleEndian(header[16..], detailsOffset);
        BinaryPrimitives.WriteInt32LittleEndian(header[24..], detailsLength);
        BinaryPrimitives.WriteInt64LittleEndian(header[28..], thumbnailOffset);
        BinaryPrimitives.WriteInt32LittleEndian(header[36..], thumbnailLength);
        BinaryPrimitives.WriteInt64LittleEndian(header[40..], tableOffset);
        BinaryPrimitives.WriteInt32LittleEndian(header[48..], tableSize);
        BinaryPrimitives.WriteUInt64LittleEndian(header[56..], XxHash3.HashToUInt64(header[..56]));
        stream.Position = start;
        stream.Write(header);
        stream.Position = end;
    }

    /// <summary>Reads only the details JSON and the thumbnail (load screen, FR-016). Throws <see cref="SaveFormatException"/>.</summary>
    public static (byte[] Details, SaveThumbnailData? Thumbnail) Peek(Stream stream, bool includeThumbnail = true)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var header = ReadHeader(stream);
        var details = ReadDetails(stream, header);
        var thumbnail = includeThumbnail ? ReadThumbnail(stream, header) : null;
        return (details, thumbnail);
    }

    /// <summary>
    /// Reads a complete save: verifies the header, details, thumbnail, table and every block's checksum, then
    /// decompresses the blocks in parallel. Throws <see cref="SaveFormatException"/> for any problem; nothing is
    /// returned unless the whole file is sound.
    /// </summary>
    public static SaveFileContents Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var header = ReadHeader(stream);
        var details = ReadDetails(stream, header);
        var thumbnail = ReadThumbnail(stream, header);
        var sections = ReadTable(stream, header);

        // Read and verify every compressed block before decompressing any.
        var blockCount = 0;
        foreach (var section in sections)
        {
            blockCount += section.Blocks.Count;
        }

        var compressed = new byte[blockCount][];
        var jobs = new (int Section, int Block)[blockCount];
        var payloads = new List<SaveSectionData>(sections.Count);
        var segments = new byte[blockCount][];
        try
        {
            var j = 0;
            for (var s = 0; s < sections.Count; s++)
            {
                for (var b = 0; b < sections[s].Blocks.Count; b++)
                {
                    var block = sections[s].Blocks[b];
                    var bytes = ArrayPool<byte>.Shared.Rent(block.CompressedLength);
                    compressed[j] = bytes;
                    stream.Position = header.Start + block.Offset;
                    ReadExactly(stream, bytes.AsSpan(0, block.CompressedLength));
                    if (XxHash3.HashToUInt64(bytes.AsSpan(0, block.CompressedLength)) != block.Checksum)
                    {
                        throw new SaveFormatException($"Section {sections[s].Id} block {b} is damaged (checksum).");
                    }

                    jobs[j++] = (s, b);
                }
            }

            Exception? failure = null;
            Parallel.For(0, blockCount, i =>
            {
                var (s, b) = jobs[i];
                var block = sections[s].Blocks[b];
                var segment = SaveBufferPool.Rent();
                segments[i] = segment;
                try
                {
                    var written = Decompress(compressed[i], 0, block.CompressedLength, block.Codec, segment.AsSpan(0, block.UncompressedLength));
                    if (written != block.UncompressedLength)
                    {
                        throw new SaveFormatException($"Section {sections[s].Id} block {b} has the wrong length.");
                    }
                }
                catch (Exception e) when (e is InvalidDataException or SaveFormatException)
                {
                    Interlocked.CompareExchange(ref failure, e, null);
                }
            });
            if (failure is not null)
            {
                throw failure as SaveFormatException ?? new SaveFormatException("A section block is damaged.", failure);
            }

            j = 0;
            for (var s = 0; s < sections.Count; s++)
            {
                var buffer = new SaveBuffer();
                for (var b = 0; b < sections[s].Blocks.Count; b++)
                {
                    buffer.Append(segments[j], sections[s].Blocks[b].UncompressedLength);
                    segments[j++] = null!;
                }

                payloads.Add(new SaveSectionData(sections[s].Id, sections[s].Version, buffer));
            }

            return new SaveFileContents(details, thumbnail, payloads);
        }
        catch
        {
            foreach (var payload in payloads)
            {
                payload.Payload.Dispose();
            }

            foreach (var segment in segments)
            {
                if (segment is not null)
                {
                    SaveBufferPool.Return(segment);
                }
            }

            throw;
        }
        finally
        {
            foreach (var bytes in compressed)
            {
                if (bytes is not null)
                {
                    ArrayPool<byte>.Shared.Return(bytes);
                }
            }
        }
    }

    private static Header ReadHeader(Stream stream)
    {
        var start = stream.Position;
        var length = stream.Length - start;
        Span<byte> header = stackalloc byte[HeaderLength];
        if (length < HeaderLength)
        {
            throw new SaveFormatException(SaveFailure.NotASave, "The file is too short to be a save.");
        }

        ReadExactly(stream, header);
        if (BinaryPrimitives.ReadUInt64LittleEndian(header) != Magic)
        {
            throw new SaveFormatException(SaveFailure.NotASave, "The file is not a save.");
        }

        if (BinaryPrimitives.ReadUInt64LittleEndian(header[56..]) != XxHash3.HashToUInt64(header[..56]))
        {
            throw new SaveFormatException("The save header is damaged.");
        }

        var version = BinaryPrimitives.ReadUInt16LittleEndian(header[8..]);
        if (version > ContainerVersion)
        {
            throw new SaveFormatException(SaveFailure.TooNew, $"The save uses container version {version}; this game reads up to {ContainerVersion}.");
        }

        if (version == 0 || BinaryPrimitives.ReadUInt16LittleEndian(header[10..]) != 0 || BinaryPrimitives.ReadInt32LittleEndian(header[12..]) != HeaderLength)
        {
            throw new SaveFormatException("The save header is inconsistent.");
        }

        var h = new Header(
            start,
            length,
            BinaryPrimitives.ReadInt64LittleEndian(header[16..]),
            BinaryPrimitives.ReadInt32LittleEndian(header[24..]),
            BinaryPrimitives.ReadInt64LittleEndian(header[28..]),
            BinaryPrimitives.ReadInt32LittleEndian(header[36..]),
            BinaryPrimitives.ReadInt64LittleEndian(header[40..]),
            BinaryPrimitives.ReadInt32LittleEndian(header[48..]));
        CheckRange(h, h.DetailsOffset, h.DetailsLength, "details");
        if (h.ThumbnailOffset != 0 || h.ThumbnailLength != 0)
        {
            CheckRange(h, h.ThumbnailOffset, h.ThumbnailLength, "thumbnail");
        }

        CheckRange(h, h.TableOffset, h.TableLength, "section table");
        return h;
    }

    private static byte[] ReadDetails(Stream stream, Header header)
    {
        if (header.DetailsLength < 12 || header.DetailsLength > MaxDetailsLength + 12)
        {
            throw new SaveFormatException("The save details are damaged.");
        }

        var block = new byte[header.DetailsLength];
        stream.Position = header.Start + header.DetailsOffset;
        ReadExactly(stream, block);
        var json = BinaryPrimitives.ReadInt32LittleEndian(block);
        if (json != header.DetailsLength - 12)
        {
            throw new SaveFormatException("The save details are damaged.");
        }

        var bytes = block.AsSpan(4, json);
        if (BinaryPrimitives.ReadUInt64LittleEndian(block.AsSpan(4 + json)) != XxHash3.HashToUInt64(bytes))
        {
            throw new SaveFormatException("The save details are damaged (checksum).");
        }

        return bytes.ToArray();
    }

    private static SaveThumbnailData? ReadThumbnail(Stream stream, Header header)
    {
        if (header.ThumbnailLength == 0)
        {
            return null;
        }

        if (header.ThumbnailLength < 17 || header.ThumbnailLength > MaxCompressedLength(1024 * 1024 * 3) + 17)
        {
            throw new SaveFormatException("The save thumbnail is damaged.");
        }

        var block = new byte[header.ThumbnailLength];
        stream.Position = header.Start + header.ThumbnailOffset;
        ReadExactly(stream, block);
        int width = BinaryPrimitives.ReadUInt16LittleEndian(block);
        int height = BinaryPrimitives.ReadUInt16LittleEndian(block.AsSpan(2));
        var codec = (SaveCodec)block[4];
        var length = BinaryPrimitives.ReadInt32LittleEndian(block.AsSpan(5));
        if (length != header.ThumbnailLength - 17 || width is 0 or > 1024 || height is 0 or > 1024)
        {
            throw new SaveFormatException("The save thumbnail is damaged.");
        }

        CheckCodec(codec);
        if (BinaryPrimitives.ReadUInt64LittleEndian(block.AsSpan(9 + length)) != XxHash3.HashToUInt64(block.AsSpan(9, length)))
        {
            throw new SaveFormatException("The save thumbnail is damaged (checksum).");
        }

        var pixels = new byte[width * height * 3];
        try
        {
            if (Decompress(block, 9, length, codec, pixels) != pixels.Length)
            {
                throw new SaveFormatException("The save thumbnail is damaged.");
            }
        }
        catch (InvalidDataException e)
        {
            throw new SaveFormatException("The save thumbnail is damaged.", e);
        }

        return new SaveThumbnailData(width, height, pixels);
    }

    private static List<TableSection> ReadTable(Stream stream, Header header)
    {
        var table = new byte[header.TableLength];
        stream.Position = header.Start + header.TableOffset;
        ReadExactly(stream, table);
        if (table.Length < 12)
        {
            throw new SaveFormatException("The section table is damaged.");
        }

        var body = table.AsSpan(0, table.Length - 8);
        if (BinaryPrimitives.ReadUInt64LittleEndian(table.AsSpan(table.Length - 8)) != XxHash3.HashToUInt64(body))
        {
            throw new SaveFormatException("The section table is damaged (checksum).");
        }

        var count = BinaryPrimitives.ReadInt32LittleEndian(body);
        if (count < 0 || count > 256)
        {
            throw new SaveFormatException("The section table is damaged.");
        }

        var dataStart = Math.Max(header.DetailsOffset + header.DetailsLength, header.ThumbnailOffset + header.ThumbnailLength);
        var nextFree = dataStart;
        var at = 4;
        var sections = new List<TableSection>(count);
        var seen = new HashSet<uint>();
        for (var s = 0; s < count; s++)
        {
            Need(body, at, TableEntryHeader);
            var id = new SaveSectionId(BinaryPrimitives.ReadUInt32LittleEndian(body[at..]));
            var version = BinaryPrimitives.ReadUInt16LittleEndian(body[(at + 4)..]);
            var total = BinaryPrimitives.ReadInt64LittleEndian(body[(at + 8)..]);
            var blocks = BinaryPrimitives.ReadInt32LittleEndian(body[(at + 16)..]);
            at += TableEntryHeader;
            if (!seen.Add(id.Value) || version == 0 || total < 0 || blocks < 0 || blocks > (body.Length - at) / TableBlockEntry || blocks > (total / SaveBuffer.SegmentSize) + 1)
            {
                throw new SaveFormatException($"The section table entry {id} is damaged.");
            }

            var list = new List<BlockEntry>(blocks);
            long sum = 0;
            for (var b = 0; b < blocks; b++)
            {
                Need(body, at, TableBlockEntry);
                var block = new BlockEntry(
                    BinaryPrimitives.ReadInt64LittleEndian(body[at..]),
                    BinaryPrimitives.ReadInt32LittleEndian(body[(at + 8)..]),
                    BinaryPrimitives.ReadInt32LittleEndian(body[(at + 12)..]),
                    (SaveCodec)body[at + 16],
                    BinaryPrimitives.ReadUInt64LittleEndian(body[(at + 20)..]));
                at += TableBlockEntry;
                CheckCodec(block.Codec);
                var lastBlock = b == blocks - 1;
                if (block.UncompressedLength <= 0 || block.UncompressedLength > SaveBuffer.SegmentSize
                    || (!lastBlock && block.UncompressedLength != SaveBuffer.SegmentSize)
                    || block.CompressedLength < 0 || block.CompressedLength > MaxCompressedLength(SaveBuffer.SegmentSize)
                    || block.Offset < nextFree || block.Offset > header.TableOffset - block.CompressedLength)
                {
                    throw new SaveFormatException($"Section {id} block {b} is out of place.");
                }

                nextFree = block.Offset + block.CompressedLength;
                sum += block.UncompressedLength;
                list.Add(block);
            }

            if (sum != total)
            {
                throw new SaveFormatException($"Section {id} has the wrong length.");
            }

            sections.Add(new TableSection(id, version, total, list));
        }

        if (at != body.Length)
        {
            throw new SaveFormatException("The section table is damaged.");
        }

        return sections;
    }

    private static void Need(ReadOnlySpan<byte> body, int at, int count)
    {
        if (at + count > body.Length)
        {
            throw new SaveFormatException("The section table is truncated.");
        }
    }

    private static void CheckRange(Header header, long offset, int length, string what)
    {
        if (offset < HeaderLength || length < 0 || offset > header.FileLength - length)
        {
            throw new SaveFormatException($"The save {what} lies outside the file.");
        }
    }

    private static void CheckCodec(SaveCodec codec)
    {
        if (codec > SaveCodec.Brotli)
        {
            throw new SaveFormatException(SaveFailure.TooNew, $"The save uses an unknown compression ({(int)codec}).");
        }
    }

    private static void ReadExactly(Stream stream, Span<byte> target)
    {
        try
        {
            stream.ReadExactly(target);
        }
        catch (EndOfStreamException e)
        {
            throw new SaveFormatException("The save is truncated.", e);
        }
    }

    internal static int MaxCompressedLength(int length) => length + (length / 8) + 4096;

    private static byte[] Compress(ReadOnlySpan<byte> source, SaveCodec codec)
    {
        var output = new byte[MaxCompressedLength(source.Length)];
        var length = CompressInto(source, output, codec);
        return output.AsSpan(0, length).ToArray();
    }

    /// <summary>Compresses one block; returns the compressed length.</summary>
    internal static int CompressInto(ReadOnlySpan<byte> source, byte[] output, SaveCodec codec)
    {
        switch (codec)
        {
            case SaveCodec.Stored:
                source.CopyTo(output);
                return source.Length;
            case SaveCodec.Brotli:
                if (!BrotliEncoder.TryCompress(source, output, out var written, quality: 1, window: 22))
                {
                    throw new InvalidOperationException("Brotli output buffer too small.");
                }

                return written;
            case SaveCodec.Deflate:
                using (var memory = new MemoryStream(output, writable: true))
                {
                    using (var deflate = new DeflateStream(memory, CompressionLevel.Fastest, leaveOpen: true))
                    {
                        deflate.Write(source);
                    }

                    return (int)memory.Position;
                }

            default:
                throw new ArgumentOutOfRangeException(nameof(codec));
        }
    }

    /// <summary>
    /// Decompresses one block into <paramref name="target"/>; returns the bytes produced, or <c>target.Length + 1</c>
    /// when the block holds more than the target (a damaged length).
    /// </summary>
    internal static int Decompress(byte[] array, int offset, int length, SaveCodec codec, Span<byte> target)
    {
        var source = array.AsSpan(offset, length);
        switch (codec)
        {
            case SaveCodec.Stored:
                if (source.Length != target.Length)
                {
                    return source.Length;
                }

                source.CopyTo(target);
                return source.Length;
            case SaveCodec.Brotli:
                {
                    using var decoder = new BrotliDecoder();
                    var status = decoder.Decompress(source, target, out var consumed, out var written);
                    if (status != OperationStatus.Done || consumed != source.Length)
                    {
                        throw new InvalidDataException("Brotli block damaged.");
                    }

                    return written;
                }

            case SaveCodec.Deflate:
                {
                    using var memory = new MemoryStream(array, offset, length, writable: false);
                    using var deflate = new DeflateStream(memory, CompressionMode.Decompress);
                    var total = 0;
                    while (total < target.Length)
                    {
                        var read = deflate.Read(target[total..]);
                        if (read == 0)
                        {
                            return total;
                        }

                        total += read;
                    }

                    Span<byte> probe = stackalloc byte[1];
                    return deflate.Read(probe) == 0 ? total : total + 1;
                }

            default:
                throw new SaveFormatException(SaveFailure.TooNew, "Unknown codec.");
        }
    }

    private readonly record struct Header(long Start, long FileLength, long DetailsOffset, int DetailsLength, long ThumbnailOffset, int ThumbnailLength, long TableOffset, int TableLength);

    private readonly record struct BlockEntry(long Offset, int CompressedLength, int UncompressedLength, SaveCodec Codec, ulong Checksum);

    private sealed record TableSection(SaveSectionId Id, int Version, long Length, List<BlockEntry> Blocks);
}

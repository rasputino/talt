using System.Text;
using Talt.Core.Saves;

namespace Talt.Core.Tests.Saves;

public class SaveFileTests
{
    private static SaveSectionData Section(string id, int version, int bytes, byte seed)
    {
        var buffer = new SaveBuffer();
        var writer = new SaveWriter(buffer);
        var data = new byte[bytes];
        for (var i = 0; i < bytes; i++)
        {
            data[i] = (byte)((i % 97 == 0 ? seed : 0) + (i / 100000));
        }

        writer.WriteRaw(data);
        return new SaveSectionData(SaveSectionId.Of(id), version, buffer);
    }

    private static byte[] Write(SaveCodec codec, out List<SaveSectionData> sections)
    {
        sections =
        [
            Section("AAAA", 1, 100, 1),
            Section("BBBB", 3, SaveBuffer.SegmentSize * 2 + 123, 2),
            Section("CCCC", 1, 0, 3),
        ];
        var pixels = new byte[16 * 8 * 3];
        pixels[5] = 77;
        using var stream = new MemoryStream();
        SaveFile.Write(stream, Encoding.UTF8.GetBytes("{\"name\":\"x\"}"), new SaveThumbnailData(16, 8, pixels), sections, codec);
        return stream.ToArray();
    }

    [Theory]
    [InlineData(SaveCodec.Stored)]
    [InlineData(SaveCodec.Deflate)]
    [InlineData(SaveCodec.Brotli)]
    public void Container_round_trips_with_every_codec(SaveCodec codec)
    {
        var bytes = Write(codec, out var sections);
        using var contents = SaveFile.Read(new MemoryStream(bytes));
        Assert.Equal("{\"name\":\"x\"}", Encoding.UTF8.GetString(contents.DetailsJson));
        Assert.Equal(16, contents.Thumbnail!.Width);
        Assert.Equal(77, contents.Thumbnail.Pixels[5]);
        Assert.Equal(sections.Count, contents.Sections.Count);
        for (var s = 0; s < sections.Count; s++)
        {
            Assert.Equal(sections[s].Id, contents.Sections[s].Id);
            Assert.Equal(sections[s].Version, contents.Sections[s].Version);
            Assert.Equal(sections[s].Payload.Length, contents.Sections[s].Payload.Length);
            for (var g = 0; g < sections[s].Payload.SegmentCount; g++)
            {
                Assert.True(sections[s].Payload.Segment(g).SequenceEqual(contents.Sections[s].Payload.Segment(g)));
            }

            sections[s].Payload.Dispose();
        }
    }

    [Fact]
    public void Peek_reads_details_and_thumbnail_only()
    {
        var bytes = Write(SaveCodec.Deflate, out var sections);
        sections.ForEach(s => s.Payload.Dispose());
        var (details, thumbnail) = SaveFile.Peek(new MemoryStream(bytes));
        Assert.Equal("{\"name\":\"x\"}", Encoding.UTF8.GetString(details));
        Assert.Equal(8, thumbnail!.Height);
    }

    [Fact]
    public void Any_changed_byte_is_reported_before_decoding()
    {
        var bytes = Write(SaveCodec.Deflate, out var sections);
        sections.ForEach(s => s.Payload.Dispose());
        // Every byte position class: header, details, thumbnail, blocks, table. Stride keeps the test fast.
        for (var at = 0; at < bytes.Length; at += Math.Max(1, bytes.Length / 4000))
        {
            var damaged = (byte[])bytes.Clone();
            damaged[at] ^= 0x5A;
            Assert.Throws<SaveFormatException>(() => SaveFile.Read(new MemoryStream(damaged)).Dispose());
        }
    }

    [Fact]
    public void Truncated_empty_and_foreign_files_are_reported()
    {
        var bytes = Write(SaveCodec.Deflate, out var sections);
        sections.ForEach(s => s.Payload.Dispose());
        foreach (var length in new[] { 0, 10, 63, 64, 200, bytes.Length / 2, bytes.Length - 1 })
        {
            Assert.Throws<SaveFormatException>(() => SaveFile.Read(new MemoryStream(bytes, 0, length)).Dispose());
        }

        var foreign = Encoding.UTF8.GetBytes(new string('x', 500));
        Assert.Equal(SaveFailure.NotASave, Assert.Throws<SaveFormatException>(() => SaveFile.Read(new MemoryStream(foreign))).Failure);
    }

    [Fact]
    public void Newer_container_versions_are_refused_as_too_new()
    {
        var bytes = Write(SaveCodec.Deflate, out var sections);
        sections.ForEach(s => s.Payload.Dispose());
        bytes[8] = 2; // Container version 2 …
        var hash = System.IO.Hashing.XxHash3.HashToUInt64(bytes.AsSpan(0, 56));
        BitConverter.TryWriteBytes(bytes.AsSpan(56), hash); // … with a valid header checksum.
        Assert.Equal(SaveFailure.TooNew, Assert.Throws<SaveFormatException>(() => SaveFile.Read(new MemoryStream(bytes))).Failure);
    }
}

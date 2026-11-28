using Talt.Core.Determinism;
using Talt.Core.Saves;

namespace Talt.Core.Tests.Saves;

public class SavePrimitivesTests
{
    private static readonly SaveSectionId Test = SaveSectionId.Of("TEST");

    [Fact]
    public void Primitives_round_trip()
    {
        using var buffer = new SaveBuffer();
        var writer = new SaveWriter(buffer);
        writer.WriteBool(true);
        writer.WriteByte(200);
        writer.WriteInt(-123456);
        writer.WriteLong(long.MinValue);
        writer.WriteULong(ulong.MaxValue);
        writer.WriteString(null);
        writer.WriteString(string.Empty);
        writer.WriteString("Estación Ñandú 🚂");
        writer.WriteArray<int>(null);
        writer.WriteArray(Array.Empty<long>());
        writer.WriteArray(new[] { 1, 2, 3 });
        var random = new DeterministicRandom(42);
        random.NextUInt64();
        writer.WriteRandom(random);

        var reader = new SaveReader(buffer, Test);
        Assert.True(reader.ReadBool());
        Assert.Equal(200, reader.ReadByte());
        Assert.Equal(-123456, reader.ReadInt());
        Assert.Equal(long.MinValue, reader.ReadLong());
        Assert.Equal(ulong.MaxValue, reader.ReadULong());
        Assert.Null(reader.ReadString());
        Assert.Equal(string.Empty, reader.ReadString());
        Assert.Equal("Estación Ñandú 🚂", reader.ReadString());
        Assert.Null(reader.ReadArray<int>());
        Assert.Empty(reader.ReadRequiredArray<long>());
        Assert.Equal([1, 2, 3], reader.ReadRequiredArray<int>());
        var copy = reader.ReadRandom();
        Assert.Equal(random.NextUInt64(), copy.NextUInt64());
        reader.ExpectEnd();
    }

    [Fact]
    public void Arrays_and_strings_cross_segment_boundaries()
    {
        using var buffer = new SaveBuffer();
        var writer = new SaveWriter(buffer);
        writer.WriteRaw(new byte[SaveBuffer.SegmentSize - 3]);
        var big = new int[(SaveBuffer.SegmentSize / 4) + 1000];
        for (var i = 0; i < big.Length; i++)
        {
            big[i] = i * 7;
        }

        writer.WriteArray(big);
        var text = new string('é', 3000) + "fin";
        writer.WriteString(text);
        Assert.True(buffer.SegmentCount >= 2);

        var reader = new SaveReader(buffer, Test);
        reader.ReadRaw(new byte[SaveBuffer.SegmentSize - 3]);
        Assert.Equal(big, reader.ReadRequiredArray<int>());
        Assert.Equal(text, reader.ReadString());
        reader.ExpectEnd();
    }

    [Fact]
    public void Dictionaries_keep_enumeration_order_including_holes()
    {
        var dictionary = new Dictionary<int, long>();
        for (var i = 0; i < 20; i++)
        {
            dictionary[i * 3] = i;
        }

        dictionary.Remove(9);
        dictionary.Remove(30);
        dictionary[100] = 1;

        using var buffer = new SaveBuffer();
        var writer = new SaveWriter(buffer);
        writer.WriteDictionaryInOrder(dictionary);
        writer.WriteDictionary(dictionary);
        var reader = new SaveReader(buffer, Test);
        var copy = new Dictionary<int, long> { [5] = 5 };
        reader.ReadDictionaryInto(copy);
        Assert.Equal(dictionary.ToArray(), copy.ToArray()); // Enumeration order kept.
        var sorted = new Dictionary<int, long>();
        reader.ReadDictionaryInto(sorted);
        Assert.Equal(dictionary.OrderBy(p => p.Key).ToArray(), sorted.ToArray()); // Lookups: canonical order.
    }

    [Fact]
    public void Fixed_length_mismatch_and_overruns_fail()
    {
        using var buffer = new SaveBuffer();
        var writer = new SaveWriter(buffer);
        writer.WriteArray(new byte[10]);
        writer.WriteInt(int.MaxValue);

        var reader = new SaveReader(buffer, Test);
        Assert.Throws<SaveFormatException>(() => reader.ReadInto(new byte[11].AsSpan()));
        var second = new SaveReader(buffer, Test);
        second.ReadRequiredArray<byte>();
        Assert.Throws<SaveFormatException>(() => second.ReadArray<long>()); // Length int.MaxValue does not fit.
        var third = new SaveReader(buffer, Test);
        third.ReadRequiredArray<byte>();
        third.ReadInt();
        Assert.Throws<SaveFormatException>(() => third.ReadInt());
        Assert.Throws<SaveFormatException>(() => new SaveReader(buffer, Test).ExpectEnd());
    }

    [Fact]
    public void Section_ids_pack_four_characters()
    {
        var id = SaveSectionId.Of("MAP_");
        Assert.Equal("MAP_", id.ToString());
        Assert.Equal((uint)'M' | ((uint)'A' << 8) | ((uint)'P' << 16) | ((uint)'_' << 24), id.Value);
        Assert.Throws<ArgumentException>(() => SaveSectionId.Of("TOOLONG"));
    }
}

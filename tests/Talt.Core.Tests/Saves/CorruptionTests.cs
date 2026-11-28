using System.Buffers.Binary;
using Talt.Core.Saves;

namespace Talt.Core.Tests.Saves;

public class CorruptionTests
{
    [Fact]
    public void Public_loader_refuses_damage_in_every_block_and_metadata_region()
    {
        var bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Saves", "Fixtures", "rich-catalog.talt"));
        var details = (int)BinaryPrimitives.ReadInt64LittleEndian(bytes.AsSpan(16));
        var thumbnail = (int)BinaryPrimitives.ReadInt64LittleEndian(bytes.AsSpan(28));
        var table = (int)BinaryPrimitives.ReadInt64LittleEndian(bytes.AsSpan(40));
        var offsets = new List<int> { 5, 20, details + 4, thumbnail + 9, table + 4 };
        var at = table + 4;
        var sections = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(table));
        for (var i = 0; i < sections; i++)
        {
            var blocks = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(at + 16));
            at += 20;
            for (var b = 0; b < blocks; b++)
            {
                offsets.Add((int)BinaryPrimitives.ReadInt64LittleEndian(bytes.AsSpan(at)));
                at += 28;
            }
        }

        foreach (var offset in offsets)
        {
            bytes[offset] ^= 0x80;
            var result = SaveGame.Load(new MemoryStream(bytes));
            Assert.False(result.Succeeded);
            Assert.Null(result.Simulation);
            bytes[offset] ^= 0x80;
        }

        foreach (var length in offsets.Append(0).Append(bytes.Length - 1))
        {
            Assert.Null(SaveGame.Load(new MemoryStream(bytes, 0, length)).Simulation);
        }
    }
}

using Talt.Core.Construction;
using Talt.Tests.Support;

namespace Talt.Core.Tests.Construction;

public class TileSetTests
{
    [Fact]
    public void Add_GivesDenseSlotsInInsertionOrder_AndFindsThem()
    {
        var set = new TileSet(4);
        var tiles = Enumerable.Range(0, 5000).Select(i => (i * 7919) % 100_003).ToArray();

        for (var i = 0; i < tiles.Length; i++)
        {
            Assert.Equal(i, set.Add(tiles[i], out var added));
            Assert.True(added);
        }

        for (var i = 0; i < tiles.Length; i++)
        {
            Assert.Equal(i, set.Find(tiles[i]));
            Assert.Equal(tiles[i], set[i]);
            Assert.Equal(i, set.Add(tiles[i], out var added));
            Assert.False(added);
        }

        Assert.Equal(-1, set.Find(100_004));
    }

    [Fact]
    public void Clear_EmptiesTheSet_WithoutAllocating()
    {
        var set = new TileSet(1024);
        set.Add(5, out _);

        AllocationAssert.NoSteadyStateAllocations(
            () =>
            {
                set.Clear();
                for (var t = 0; t < 400; t++)
                {
                    set.Add(t * 3, out _);
                }
            },
            iterationsPerRound: 100,
            "100 clear-and-fill rounds");
        set.Clear();
        Assert.Equal(0, set.Count);
        Assert.Equal(-1, set.Find(3));
    }
}

using Talt.Core.Voxels;

namespace Talt.Core.Tests.Voxels;

public class RegionCoordTests
{
    [Fact]
    public void Region_IsFourChunksPerSide() => Assert.Equal(128, RegionCoord.Size);

    [Theory]
    [InlineData(0, 0, 0, 0, 0, 0)]
    [InlineData(127, 31, 127, 0, 0, 0)]
    [InlineData(128, 0, 300, 1, 0, 2)]
    public void FromVoxel_FindsTheRegion(int x, int y, int z, int rx, int ry, int rz) =>
        Assert.Equal(new RegionCoord(rx, ry, rz), RegionCoord.FromVoxel(x, y, z));

    [Fact]
    public void Chunks_ListsAllSixtyFourChunks_InsideTheRegion()
    {
        var region = new RegionCoord(1, 0, 2);
        var chunks = region.Chunks().ToList();

        Assert.Equal(64, chunks.Distinct().Count());
        Assert.All(chunks, c => Assert.Equal(region, RegionCoord.FromChunk(c)));
    }

    [Fact]
    public void LocalChunk_IsThePositionInsideTheRegion() =>
        Assert.Equal((1, 0, 3), RegionCoord.LocalChunk(new ChunkCoord(5, 0, 7)));
}

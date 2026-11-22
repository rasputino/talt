using Talt.Core.Voxels;

namespace Talt.Core.Tests.Voxels;

public class VoxelWorldTests
{
    [Theory]
    [InlineData(0, 0, 0, 0, 0, 0)]
    [InlineData(31, 31, 31, 0, 0, 0)]
    [InlineData(32, 0, 64, 1, 0, 2)]
    [InlineData(-1, -1, -1, -1, -1, -1)]
    [InlineData(-32, 0, -33, -1, 0, -2)]
    public void ChunkCoord_FromVoxel_UsesFloorDivision(int x, int y, int z, int cx, int cy, int cz)
    {
        Assert.Equal(new ChunkCoord(cx, cy, cz), ChunkCoord.FromVoxel(x, y, z));
    }

    [Fact]
    public void GetVoxel_InMissingChunk_IsAir()
    {
        var world = new VoxelWorld();

        Assert.Equal(VoxelMaterial.Air, world.GetVoxel(100, 5, -7));
        Assert.Equal(0, world.ChunkCount);
    }

    [Fact]
    public void SetVoxel_ThenGetVoxel_RoundTrips_IncludingNegativeCoordinates()
    {
        var world = new VoxelWorld();

        world.SetVoxel(-1, 3, 40, VoxelMaterial.Stone);

        Assert.Equal(VoxelMaterial.Stone, world.GetVoxel(-1, 3, 40));
        Assert.Equal(VoxelMaterial.Stone, world.GetChunk(new ChunkCoord(-1, 0, 1))!.Get(31, 3, 8));
    }

    [Fact]
    public void Chunk_Get_OutsideBounds_Throws()
    {
        var chunk = new Chunk(new ChunkCoord(0, 0, 0));

        Assert.Throws<ArgumentOutOfRangeException>(() => chunk.Get(Chunk.Size, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => chunk.Get(0, -1, 0));
    }

    [Theory]
    [InlineData(10, 10, 10, 1)]  // Interior.
    [InlineData(31, 10, 10, 2)]  // Face of a chunk.
    [InlineData(32, 10, 31, 4)]  // Edge between four chunks.
    [InlineData(31, 31, 32, 8)]  // Corner of eight chunks.
    [InlineData(0, 0, 0, 8)]     // World corner: neighbours below and beside may not exist.
    public void TouchedBy_ListsEveryChunkWithinOneVoxel(int x, int y, int z, int expected)
    {
        var touched = ChunkCoord.TouchedBy(x, y, z);

        Assert.Equal(expected, touched.Count);
        Assert.Contains(ChunkCoord.FromVoxel(x, y, z), touched);
        Assert.Equal(touched.Count, touched.Distinct().Count());
    }
}

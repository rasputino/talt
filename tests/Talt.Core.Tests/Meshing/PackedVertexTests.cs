using Talt.Core.Meshing;
using Talt.Core.Voxels;

namespace Talt.Core.Tests.Meshing;

public class PackedVertexTests
{
    [Theory]
    [InlineData(0, 0, 0, 0, VoxelMaterial.Grass, 0)]
    [InlineData(128, 128, 128, 5, VoxelMaterial.Road, 3)]
    [InlineData(17, 31, 99, 3, VoxelMaterial.Water, 2)]
    public void Create_RoundTrips(int x, int y, int z, int face, VoxelMaterial material, int ao)
    {
        var vertex = PackedVertex.Create(x, y, z, face, material, ao);

        Assert.Equal((x, y, z, face, material, ao), (vertex.X, vertex.Y, vertex.Z, vertex.FaceIndex, vertex.Material, vertex.AmbientOcclusion));
    }

    [Fact]
    public void Layout_IsEightBytes() => Assert.Equal(PackedVertex.SizeInBytes, System.Runtime.InteropServices.Marshal.SizeOf<PackedVertex>());

    [Theory]
    [InlineData(129, 0, 0, 0, VoxelMaterial.Grass, 0)]
    [InlineData(-1, 0, 0, 0, VoxelMaterial.Grass, 0)]
    [InlineData(0, 0, 0, 6, VoxelMaterial.Grass, 0)]
    [InlineData(0, 0, 0, 0, VoxelMaterial.Grass, 4)]
    public void Create_RejectsOutOfRangeValues(int x, int y, int z, int face, VoxelMaterial material, int ao) =>
        Assert.ThrowsAny<ArgumentException>(() => PackedVertex.Create(x, y, z, face, material, ao));

    [Fact]
    public void Create_RejectsAir() =>
        Assert.Throws<ArgumentException>(() => PackedVertex.Create(0, 0, 0, 0, VoxelMaterial.Air, 0));

    [Fact]
    public void PackedMesh_IsAtLeastThreeTimesSmallerPerFace_ThanTheReference()
    {
        // Reference: 4 × 36-byte vertices + 6 × 4-byte indices per face. Packed: 4 × 8 bytes, shared indices.
        const int reference = 4 * 36 + 6 * 4;
        const int packed = 4 * PackedVertex.SizeInBytes;
        Assert.True(reference >= 3 * packed);
    }
}

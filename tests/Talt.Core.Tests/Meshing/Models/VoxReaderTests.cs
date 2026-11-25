using System.Buffers.Binary;
using Talt.Core.Meshing.Models;

namespace Talt.Core.Tests.Meshing.Models;

public class VoxReaderTests
{
    [Fact]
    public void RoundTrip_PreservesVoxelsAndPalette()
    {
        var model = SampleModel();

        var read = VoxReader.Read(VoxWriter.Write(model), "sample.vox");

        Assert.True(model.ContentEquals(read));
    }

    [Fact]
    public void Read_ConvertsZUpToYUp()
    {
        // File: SIZE (2, 3, 4) with one voxel at file (1, 0, 3): x = 1, y (depth) = 0, z (height) = 3.
        var file = Build(sizeX: 2, sizeY: 3, sizeZ: 4, voxels: [(1, 0, 3, 7)]);

        var model = VoxReader.Read(file, "axes.vox");

        Assert.Equal((2, 4, 3), (model.SizeX, model.SizeY, model.SizeZ));
        Assert.Equal(7, model.Get(1, 3, 2)); // game (x, z, sizeY - 1 - y)
        Assert.Equal(1, model.Count);
    }

    [Fact]
    public void Read_MapsFileEntryToColourIndexPlusOne()
    {
        var file = Build(1, 1, 1, [(0, 0, 0, 1)], paletteEntry0: VoxModel.Rgba(10, 20, 30));

        var model = VoxReader.Read(file, "palette.vox");

        Assert.Equal(VoxModel.Rgba(10, 20, 30), model.Palette[1]);
    }

    [Fact]
    public void Read_SkipsUnknownAndSceneChunks()
    {
        var file = Build(2, 2, 2, [(0, 0, 0, 3)], extraChunk: "nTRN");

        var model = VoxReader.Read(file, "scene.vox");

        Assert.Equal(1, model.Count);
    }

    [Fact]
    public void ReadAll_ReturnsEveryModel()
    {
        var file = Build(2, 2, 2, [(0, 0, 0, 3)], secondModel: true);

        var models = VoxReader.ReadAll(file, "two.vox");

        Assert.Equal(2, models.Count);
    }

    [Theory]
    [InlineData("magic")]
    [InlineData("version")]
    [InlineData("truncated")]
    [InlineData("outside")]
    [InlineData("noPalette")]
    [InlineData("empty")]
    [InlineData("tooManyVoxels")]
    [InlineData("size")]
    public void Read_RejectsMalformedFiles(string problem)
    {
        var file = problem switch
        {
            "magic" => [.. "VOK "u8, .. Build(1, 1, 1, [(0, 0, 0, 1)])[4..]],
            "version" => WithVersion(Build(1, 1, 1, [(0, 0, 0, 1)]), 99),
            "truncated" => Build(1, 1, 1, [(0, 0, 0, 1)])[..40],
            "outside" => Build(2, 2, 2, [(5, 0, 0, 1)]),
            "noPalette" => Build(1, 1, 1, [(0, 0, 0, 1)], palette: false),
            "empty" => Build(1, 1, 1, []),
            "tooManyVoxels" => Build(1, 1, 1, [(0, 0, 0, 1), (0, 0, 0, 2)]),
            "size" => Build(300, 1, 1, [(0, 0, 0, 1)]),
            _ => throw new ArgumentOutOfRangeException(nameof(problem)),
        };

        var error = Assert.Throws<VoxFormatException>(() => VoxReader.Read(file, "bad.vox"));
        Assert.StartsWith("bad.vox:", error.Message, StringComparison.Ordinal);
    }

    internal static VoxModel SampleModel()
    {
        var model = new VoxModel(5, 4, 3);
        model.Set(0, 0, 0, 1);
        model.Set(4, 3, 2, 2);
        model.Set(2, 1, 1, 200);
        model.Palette[1] = VoxModel.Rgba(255, 0, 0);
        model.Palette[2] = VoxModel.Rgba(0, 255, 0);
        model.Palette[200] = VoxModel.Rgba(1, 2, 3, 4);
        return model;
    }

    private static byte[] WithVersion(byte[] file, int version)
    {
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(4), version);
        return file;
    }

    /// <summary>Builds a file by hand (file axes, z up), independent of <see cref="VoxWriter"/>.</summary>
    private static byte[] Build(int sizeX, int sizeY, int sizeZ, (int X, int Y, int Z, int C)[] voxels,
        bool palette = true, uint paletteEntry0 = 0xff808080, string? extraChunk = null, bool secondModel = false)
    {
        var children = new List<byte>();
        if (extraChunk is not null)
        {
            children.AddRange(Chunk(extraChunk, [1, 2, 3, 4, 5]));
        }

        for (var m = 0; m < (secondModel ? 2 : 1); m++)
        {
            var size = new byte[12];
            BinaryPrimitives.WriteInt32LittleEndian(size, sizeX);
            BinaryPrimitives.WriteInt32LittleEndian(size.AsSpan(4), sizeY);
            BinaryPrimitives.WriteInt32LittleEndian(size.AsSpan(8), sizeZ);
            children.AddRange(Chunk("SIZE", size));
            var xyzi = new byte[4 + (voxels.Length * 4)];
            BinaryPrimitives.WriteInt32LittleEndian(xyzi, voxels.Length);
            for (var i = 0; i < voxels.Length; i++)
            {
                xyzi[4 + (i * 4)] = (byte)voxels[i].X;
                xyzi[5 + (i * 4)] = (byte)voxels[i].Y;
                xyzi[6 + (i * 4)] = (byte)voxels[i].Z;
                xyzi[7 + (i * 4)] = (byte)voxels[i].C;
            }

            children.AddRange(Chunk("XYZI", xyzi));
        }

        if (palette)
        {
            var rgba = new byte[1024];
            BinaryPrimitives.WriteUInt32LittleEndian(rgba, paletteEntry0);
            children.AddRange(Chunk("RGBA", rgba));
        }

        var file = new List<byte>();
        file.AddRange("VOX "u8.ToArray());
        file.AddRange(BitConverter.GetBytes(150));
        file.AddRange("MAIN"u8.ToArray());
        file.AddRange(BitConverter.GetBytes(0));
        file.AddRange(BitConverter.GetBytes(children.Count));
        file.AddRange(children);
        return [.. file];
    }

    private static byte[] Chunk(string id, byte[] content)
    {
        var bytes = new List<byte>();
        bytes.AddRange(System.Text.Encoding.ASCII.GetBytes(id));
        bytes.AddRange(BitConverter.GetBytes(content.Length));
        bytes.AddRange(BitConverter.GetBytes(0));
        bytes.AddRange(content);
        return [.. bytes];
    }
}

using System.Buffers.Binary;
using System.Text;

namespace Talt.Core.Meshing.Models;

/// <summary>A <c>.vox</c> file that cannot be read; the message names the file and the problem.</summary>
public sealed class VoxFormatException : Exception
{
    public VoxFormatException()
    {
    }

    public VoxFormatException(string message)
        : base(message)
    {
    }

    public VoxFormatException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Reads MagicaVoxel <c>.vox</c> files (feature 018, contracts/vox-format.md), written from the public
/// description of the format: a <c>"VOX "</c> header and version, then a <c>MAIN</c> chunk whose
/// children hold <c>SIZE</c>/<c>XYZI</c> pairs (one per model) and an <c>RGBA</c> palette. Scene, material
/// and other chunks are skipped by their declared size. Converts the file's Z-up axes to the game's
/// Y-up axes: <c>game = (x, z, sizeY − 1 − y)</c>.
/// </summary>
public static class VoxReader
{
    /// <summary>Largest file accepted, far above any model of the game.</summary>
    public const int MaxFileBytes = 8 << 20;

    private const int HeaderBytes = 8;
    private const int ChunkHeaderBytes = 12;

    /// <summary>Reads the first model of a file.</summary>
    public static VoxModel Read(ReadOnlySpan<byte> data, string name) => ReadAll(data, name)[0];

    /// <summary>Reads every model of a file, in file order; all share the file's palette.</summary>
    public static IReadOnlyList<VoxModel> ReadAll(ReadOnlySpan<byte> data, string name)
    {
        if (data.Length > MaxFileBytes)
        {
            throw Error(name, $"the file is larger than {MaxFileBytes >> 20} MiB");
        }

        if (data.Length < HeaderBytes + ChunkHeaderBytes || !data[..4].SequenceEqual("VOX "u8))
        {
            throw Error(name, "not a .vox file (the header \"VOX \" is missing)");
        }

        var version = BinaryPrimitives.ReadInt32LittleEndian(data[4..]);
        if (version is not (150 or 200))
        {
            throw Error(name, $"unsupported version {version} (150 and 200 are supported)");
        }

        var main = ReadChunkHeader(data, HeaderBytes, data.Length, name);
        if (main.Id != "MAIN")
        {
            throw Error(name, $"the first chunk is '{main.Id}', expected 'MAIN'");
        }

        var models = new List<VoxModel>();
        (int X, int Y, int Z)? pendingSize = null;
        uint[]? palette = null;
        var offset = main.ChildrenStart;
        var end = main.End;
        while (offset < end)
        {
            var chunk = ReadChunkHeader(data, offset, end, name);
            var content = data.Slice(chunk.ContentStart, chunk.ContentBytes);
            switch (chunk.Id)
            {
                case "SIZE":
                    if (pendingSize is not null)
                    {
                        throw Error(name, "a SIZE chunk is not followed by its XYZI chunk");
                    }

                    pendingSize = ReadSize(content, name);
                    break;
                case "XYZI":
                    if (pendingSize is not { } size)
                    {
                        throw Error(name, "an XYZI chunk has no SIZE chunk before it");
                    }

                    models.Add(ReadVoxels(content, size, name));
                    pendingSize = null;
                    break;
                case "RGBA":
                    palette = ReadPalette(content, name);
                    break;
                default:
                    break; // PACK, nTRN, nGRP, nSHP, MATL, LAYR, rOBJ, rCAM, NOTE, IMAP and unknown chunks.
            }

            offset = chunk.End;
        }

        if (pendingSize is not null)
        {
            throw Error(name, "the last SIZE chunk has no XYZI chunk");
        }

        if (models.Count == 0)
        {
            throw Error(name, "the file contains no model");
        }

        if (palette is null)
        {
            throw Error(name, "the file has no RGBA palette chunk (save it with a palette)");
        }

        foreach (var model in models)
        {
            palette.CopyTo(model.Palette);
            if (model.Count == 0)
            {
                throw Error(name, "a model has no voxels");
            }
        }

        return models;
    }

    private static (int X, int Y, int Z) ReadSize(ReadOnlySpan<byte> content, string name)
    {
        if (content.Length < 12)
        {
            throw Error(name, "the SIZE chunk is shorter than 12 bytes");
        }

        var x = BinaryPrimitives.ReadInt32LittleEndian(content);
        var y = BinaryPrimitives.ReadInt32LittleEndian(content[4..]);
        var z = BinaryPrimitives.ReadInt32LittleEndian(content[8..]);
        if (x is < 1 or > VoxModel.MaxSize || y is < 1 or > VoxModel.MaxSize || z is < 1 or > VoxModel.MaxSize)
        {
            throw Error(name, $"model size {x} × {y} × {z} is outside 1–{VoxModel.MaxSize}");
        }

        return (x, y, z);
    }

    private static VoxModel ReadVoxels(ReadOnlySpan<byte> content, (int X, int Y, int Z) size, string name)
    {
        if (content.Length < 4)
        {
            throw Error(name, "the XYZI chunk is shorter than 4 bytes");
        }

        var count = BinaryPrimitives.ReadInt32LittleEndian(content);
        if (count < 0 || count > size.X * size.Y * size.Z || count > (content.Length - 4) / 4)
        {
            throw Error(name, $"the XYZI chunk declares {count} voxels, more than the model or the chunk can hold");
        }

        // File axes (x, y, z) with z up → game axes (x, z, sizeY − 1 − y) with y up.
        var model = new VoxModel(size.X, size.Z, size.Y);
        for (var i = 0; i < count; i++)
        {
            var v = content.Slice(4 + (i * 4), 4);
            int x = v[0], y = v[1], z = v[2];
            var color = v[3];
            if (x >= size.X || y >= size.Y || z >= size.Z)
            {
                throw Error(name, $"voxel ({x}, {y}, {z}) is outside the {size.X} × {size.Y} × {size.Z} model");
            }

            if (color == 0)
            {
                throw Error(name, $"voxel ({x}, {y}, {z}) uses colour index 0");
            }

            model.Set(x, z, size.Y - 1 - y, color);
        }

        return model;
    }

    private static uint[] ReadPalette(ReadOnlySpan<byte> content, string name)
    {
        if (content.Length < 256 * 4)
        {
            throw Error(name, "the RGBA chunk is shorter than 1024 bytes");
        }

        // File entry i is colour index i + 1; the last entry has no index.
        var palette = new uint[256];
        for (var i = 0; i < 255; i++)
        {
            palette[i + 1] = BinaryPrimitives.ReadUInt32LittleEndian(content[(i * 4)..]);
        }

        return palette;
    }

    private static Chunk ReadChunkHeader(ReadOnlySpan<byte> data, int offset, int parentEnd, string name)
    {
        if (offset + ChunkHeaderBytes > parentEnd)
        {
            throw Error(name, $"truncated chunk header at byte {offset}");
        }

        var id = Encoding.ASCII.GetString(data.Slice(offset, 4));
        var contentBytes = BinaryPrimitives.ReadInt32LittleEndian(data[(offset + 4)..]);
        var childrenBytes = BinaryPrimitives.ReadInt32LittleEndian(data[(offset + 8)..]);
        var contentStart = offset + ChunkHeaderBytes;
        if (contentBytes < 0 || childrenBytes < 0 || (long)contentStart + contentBytes + childrenBytes > parentEnd)
        {
            throw Error(name, $"chunk '{id}' at byte {offset} does not fit inside its parent");
        }

        return new Chunk(id, contentStart, contentBytes, contentStart + contentBytes, contentStart + contentBytes + childrenBytes);
    }

    private static VoxFormatException Error(string name, string problem) => new($"{name}: {problem}.");

    private readonly record struct Chunk(string Id, int ContentStart, int ContentBytes, int ChildrenStart, int End);
}

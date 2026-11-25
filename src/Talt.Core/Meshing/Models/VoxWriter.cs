using System.Buffers.Binary;

namespace Talt.Core.Meshing.Models;

/// <summary>
/// Writes a <see cref="VoxModel"/> as a MagicaVoxel <c>.vox</c> file (version 150: <c>MAIN</c> with one
/// <c>SIZE</c>, <c>XYZI</c> and <c>RGBA</c> chunk), converting the game's Y-up axes back to the file's Z-up
/// axes. Used by the model generator; <see cref="VoxReader.Read"/> reads the result back unchanged.
/// </summary>
public static class VoxWriter
{
    public static byte[] Write(VoxModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        var count = model.Count;
        var sizeBytes = 12 + 12;
        var xyziBytes = 12 + 4 + (count * 4);
        var rgbaBytes = 12 + 1024;
        var children = sizeBytes + xyziBytes + rgbaBytes;
        var data = new byte[8 + 12 + children];
        var span = data.AsSpan();
        "VOX "u8.CopyTo(span);
        BinaryPrimitives.WriteInt32LittleEndian(span[4..], 150);
        var offset = WriteHeader(span, 8, "MAIN"u8, 0, children);

        // File size (x, y, z) = game (x, z, y).
        offset = WriteHeader(span, offset, "SIZE"u8, 12, 0);
        BinaryPrimitives.WriteInt32LittleEndian(span[offset..], model.SizeX);
        BinaryPrimitives.WriteInt32LittleEndian(span[(offset + 4)..], model.SizeZ);
        BinaryPrimitives.WriteInt32LittleEndian(span[(offset + 8)..], model.SizeY);
        offset += 12;

        offset = WriteHeader(span, offset, "XYZI"u8, 4 + (count * 4), 0);
        BinaryPrimitives.WriteInt32LittleEndian(span[offset..], count);
        offset += 4;
        for (var z = 0; z < model.SizeZ; z++)
        {
            for (var y = 0; y < model.SizeY; y++)
            {
                for (var x = 0; x < model.SizeX; x++)
                {
                    var color = model.Get(x, y, z);
                    if (color == 0)
                    {
                        continue;
                    }

                    // Game (x, y, z) came from file (x, sizeY − 1 − z, y) where the file's sizeY is the game's SizeZ.
                    span[offset] = (byte)x;
                    span[offset + 1] = (byte)(model.SizeZ - 1 - z);
                    span[offset + 2] = (byte)y;
                    span[offset + 3] = color;
                    offset += 4;
                }
            }
        }

        offset = WriteHeader(span, offset, "RGBA"u8, 1024, 0);
        for (var i = 0; i < 255; i++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(span[(offset + (i * 4))..], model.Palette[i + 1]);
        }

        return data;
    }

    private static int WriteHeader(Span<byte> span, int offset, ReadOnlySpan<byte> id, int content, int children)
    {
        id.CopyTo(span[offset..]);
        BinaryPrimitives.WriteInt32LittleEndian(span[(offset + 4)..], content);
        BinaryPrimitives.WriteInt32LittleEndian(span[(offset + 8)..], children);
        return offset + 12;
    }
}

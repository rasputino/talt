namespace Talt.Core.Meshing.Models;

/// <summary>
/// Turns a <see cref="VoxModel"/> into quads (feature 018, research R6): one greedy pass per face direction,
/// merging neighbouring faces with the same colour and the same corner ambient occlusion (the feature 003
/// rule), with per-vertex AO from the two side neighbours and the corner neighbour, and the quad diagonal
/// chosen along the brighter corners. The ground below the model (y &lt; 0) hides bottom faces and
/// darkens the foot of walls. Levels 1, 2 and 3 merge 2³, 4³ and 8³ voxels into one (filled when any voxel is
/// filled, coloured with the most common colour; level 3 is one cell per tile), so far models keep their
/// silhouette at a fraction of the triangles.
/// </summary>
public static class ModelMesher
{
    private const byte Ground = 0xff;

    public static ModelMesh Build(VoxModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        var levels = new ModelVertex[ModelMesh.LevelCount][];
        for (var level = 0; level < ModelMesh.LevelCount; level++)
        {
            var scale = 1 << level;
            var (grid, sx, sy, sz) = Downsample(model, scale);
            levels[level] = Mesh(grid, sx, sy, sz, scale, model.SizeX, model.SizeZ);
        }

        return new ModelMesh(levels, model.SizeX, model.SizeY, model.SizeZ);
    }

    /// <summary>Padded grid (one cell of border; the border below y = 0 is ground) of the model at a scale.</summary>
    private static (byte[] Grid, int X, int Y, int Z) Downsample(VoxModel model, int scale)
    {
        var sx = (model.SizeX + scale - 1) / scale;
        var sy = (model.SizeY + scale - 1) / scale;
        var sz = (model.SizeZ + scale - 1) / scale;
        int px = sx + 2, py = sy + 2, pz = sz + 2;
        var grid = new byte[px * py * pz];
        for (var z = 0; z < pz; z++)
        {
            for (var x = 0; x < px; x++)
            {
                grid[x + (px * (0 + (py * z)))] = Ground;
            }
        }

        Span<int> counts = stackalloc int[256];
        for (var z = 0; z < sz; z++)
        {
            for (var y = 0; y < sy; y++)
            {
                for (var x = 0; x < sx; x++)
                {
                    byte color;
                    if (scale == 1)
                    {
                        color = model.Get(x, y, z);
                    }
                    else
                    {
                        counts.Clear();
                        var best = 0;
                        color = 0;
                        for (var dz = 0; dz < scale; dz++)
                        {
                            for (var dy = 0; dy < scale; dy++)
                            {
                                for (var dx = 0; dx < scale; dx++)
                                {
                                    var c = model.Get((x * scale) + dx, (y * scale) + dy, (z * scale) + dz);
                                    if (c != 0 && ++counts[c] > best)
                                    {
                                        best = counts[c];
                                        color = c;
                                    }
                                }
                            }
                        }
                    }

                    grid[x + 1 + (px * (y + 1 + (py * (z + 1))))] = color;
                }
            }
        }

        return (grid, sx, sy, sz);
    }

    private static ModelVertex[] Mesh(byte[] grid, int sizeX, int sizeY, int sizeZ, int scale, int modelSizeX, int modelSizeZ)
    {
        int px = sizeX + 2, py = sizeY + 2;
        Span<int> sizes = [sizeX, sizeY, sizeZ];
        Span<int> strides = [1, px, px * py];
        Span<int> position = stackalloc int[3];
        Span<int> ao = stackalloc int[4];
        var vertices = new List<ModelVertex>();
        var first = strides[0] + strides[1] + strides[2];

        for (var faceIndex = 0; faceIndex < FaceDirections.Count; faceIndex++)
        {
            var face = FaceDirections.Faces[faceIndex];
            int normalAxis = face.Normal.Axis, uAxis = face.U.Axis, vAxis = face.V.Axis;
            var normalSign = face.Normal.Sign;
            int sizeN = sizes[normalAxis], sizeU = sizes[uAxis], sizeV = sizes[vAxis];
            var normalStep = normalSign * strides[normalAxis];
            int uStep = strides[uAxis], vStep = strides[vAxis];
            var mask = new int[sizeU * sizeV];

            for (var n = 0; n < sizeN; n++)
            {
                var any = false;
                for (var v = 0; v < sizeV; v++)
                {
                    for (var u = 0; u < sizeU; u++)
                    {
                        var cell = first + (n * strides[normalAxis]) + (u * uStep) + (v * vStep);
                        var color = grid[cell];
                        var air = cell + normalStep;
                        if (color == 0 || grid[air] != 0)
                        {
                            mask[u + (v * sizeU)] = 0;
                            continue;
                        }

                        for (var i = 0; i < 4; i++)
                        {
                            var (cu, cv) = FaceDirections.Corners[i];
                            var du = (cu == 1 ? 1 : -1) * uStep;
                            var dv = (cv == 1 ? 1 : -1) * vStep;
                            var side1 = grid[air + du] != 0;
                            var side2 = grid[air + dv] != 0;
                            var corner = grid[air + du + dv] != 0;
                            ao[i] = side1 && side2 ? 0 : 3 - (B(side1) + B(side2) + B(corner));
                        }

                        mask[u + (v * sizeU)] = (1 << 16) | color | (ao[0] << 8) | (ao[1] << 10) | (ao[2] << 12) | (ao[3] << 14);
                        any = true;
                    }
                }

                if (!any)
                {
                    continue;
                }

                for (var v = 0; v < sizeV; v++)
                {
                    for (var u = 0; u < sizeU; u++)
                    {
                        var key = mask[u + (v * sizeU)];
                        if (key == 0)
                        {
                            continue;
                        }

                        int ao0 = (key >> 8) & 3, ao1 = (key >> 10) & 3, ao2 = (key >> 12) & 3, ao3 = (key >> 14) & 3;
                        var width = 1;
                        if (ao0 == ao1 && ao3 == ao2)
                        {
                            while (u + width < sizeU && mask[u + width + (v * sizeU)] == key)
                            {
                                width++;
                            }
                        }

                        var height = 1;
                        if (ao0 == ao3 && ao1 == ao2)
                        {
                            while (v + height < sizeV && RowMatches(mask, u, v + height, width, sizeU, key))
                            {
                                height++;
                            }
                        }

                        for (var dv = 0; dv < height; dv++)
                        {
                            mask.AsSpan(u + ((v + dv) * sizeU), width).Clear();
                        }

                        ao[0] = ao0;
                        ao[1] = ao1;
                        ao[2] = ao2;
                        ao[3] = ao3;
                        var start = ao0 + ao2 >= ao1 + ao3 ? 0 : 1;
                        var color = (uint)(key & 0xff);
                        for (var k = 0; k < 4; k++)
                        {
                            var i = (start + k) & 3;
                            var (cu, cv) = FaceDirections.Corners[i];
                            position[normalAxis] = n + (normalSign > 0 ? 1 : 0);
                            position[uAxis] = u + (cu * width);
                            position[vAxis] = v + (cv * height);

                            // Half-voxel units relative to the pivot at the centre of the base.
                            var x2 = (2 * position[0] * scale) - modelSizeX;
                            var y2 = 2 * position[1] * scale;
                            var z2 = (2 * position[2] * scale) - modelSizeZ;
                            vertices.Add(new ModelVertex((short)x2, (short)y2, (short)z2, (ushort)(faceIndex | (ao[i] << 3)), color));
                        }
                    }
                }
            }
        }

        return [.. vertices];
    }

    private static bool RowMatches(int[] mask, int u, int v, int width, int sizeU, int key)
    {
        foreach (var value in mask.AsSpan(u + (v * sizeU), width))
        {
            if (value != key)
            {
                return false;
            }
        }

        return true;
    }

    private static int B(bool value) => value ? 1 : 0;
}

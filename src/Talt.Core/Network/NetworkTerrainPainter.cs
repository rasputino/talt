using System.Numerics;
using Talt.Core.Voxels;

namespace Talt.Core.Network;

/// <summary>
/// Draws a transport network onto the terrain by turning the top voxel of every tile it crosses into
/// road. Prototype helper: real road building will come with proper construction tools.
/// </summary>
public static class NetworkTerrainPainter
{
    public static void PaintRoads(VoxelWorld world, TransportNetwork network)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(network);

        for (var edge = 0; edge < network.EdgeCount; edge++)
        {
            var from = network.EdgeFrom(edge);
            var to = network.EdgeTo(edge);
            if (from > to)
            {
                continue; // Two-way roads have one edge per direction; paint each road once.
            }

            // Terrain painting is world setup, not simulation state, so float tile positions are fine.
            PaintSegment(world, network.NodePositionTiles(from), network.NodePositionTiles(to));
        }
    }

    private static void PaintSegment(VoxelWorld world, Vector2 a, Vector2 b)
    {
        var steps = (int)MathF.Ceiling(Vector2.Distance(a, b) * 2f);
        for (var i = 0; i <= steps; i++)
        {
            var p = Vector2.Lerp(a, b, i / (float)steps);
            var x = (int)MathF.Floor(p.X);
            var z = (int)MathF.Floor(p.Y);
            var height = world.GetSurfaceHeight(x, z);
            if (height > 0)
            {
                world.SetVoxel(x, height - 1, z, VoxelMaterial.Road);
            }
        }
    }
}

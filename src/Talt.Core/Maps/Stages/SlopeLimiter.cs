using Talt.Core.Terrain;

namespace Talt.Core.Maps.Stages;

/// <summary>
/// Stage 2: keeps land buildable. Neighbouring land tiles may differ by at most
/// <see cref="MapGenerationTuning.MaxStep"/> voxels, except inside cliff zones (high rocky ground)
/// where <see cref="MapGenerationTuning.CliffStep"/> is allowed (research R3). Implemented as a
/// raster-scan distance transform that only lowers ground; sea tiles are never touched, so the
/// water share is preserved.
/// </summary>
internal static class SlopeLimiter
{
    private const float RockScale = 45f;

    public static void Run(MapBuilder b)
    {
        var zone = BuildCliffZones(b);
        var height = b.Height;
        var sea = (byte)b.SeaLevel;
        var width = b.Width;
        var depth = b.Depth;
        var maxStep = b.Tuning.MaxStep;
        var cliffStep = b.Tuning.CliffStep;

        // Passes repeat until stable; uniform costs settle after one round, cliff zones need a few.
        for (var round = 0; round < 64; round++)
        {
            b.CancellationToken.ThrowIfCancellationRequested();
            var changed = false;

            for (var z = 0; z < depth; z++)
            {
                for (var x = 0; x < width; x++)
                {
                    var i = x + z * width;
                    if (height[i] < sea)
                    {
                        continue;
                    }

                    if (x > 0)
                    {
                        changed |= Limit(height, zone, i, i - 1, sea, maxStep, cliffStep);
                    }

                    if (z > 0)
                    {
                        changed |= Limit(height, zone, i, i - width, sea, maxStep, cliffStep);
                    }
                }
            }

            for (var z = depth - 1; z >= 0; z--)
            {
                for (var x = width - 1; x >= 0; x--)
                {
                    var i = x + z * width;
                    if (height[i] < sea)
                    {
                        continue;
                    }

                    if (x < width - 1)
                    {
                        changed |= Limit(height, zone, i, i + 1, sea, maxStep, cliffStep);
                    }

                    if (z < depth - 1)
                    {
                        changed |= Limit(height, zone, i, i + width, sea, maxStep, cliffStep);
                    }
                }
            }

            if (!changed)
            {
                break;
            }
        }
    }

    private static bool Limit(byte[] height, bool[] zone, int i, int neighbour, byte sea, int maxStep, int cliffStep)
    {
        if (height[neighbour] < sea)
        {
            return false;
        }

        var step = zone[i] || zone[neighbour] ? cliffStep : maxStep;
        var allowed = height[neighbour] + step;
        if (height[i] > allowed)
        {
            height[i] = (byte)allowed;
            return true;
        }

        return false;
    }

    private static bool[] BuildCliffZones(MapBuilder b)
    {
        var rock = b.Noise(6);
        var sea = b.SeaLevel;
        var rockLine = sea + (b.Settings.MaxHeight - sea) * b.Tuning.RockLinePercent / 100;
        var zone = new bool[b.TileCount];
        b.ForBands((_, z0, z1) =>
        {
            for (var z = z0; z < z1; z++)
            {
                for (var x = 0; x < b.Width; x++)
                {
                    var i = x + z * b.Width;
                    zone[i] = b.Height[i] >= rockLine && rock.Fractal(x / RockScale, z / RockScale, 2) > 0.5f;
                }
            }
        });
        return zone;
    }
}

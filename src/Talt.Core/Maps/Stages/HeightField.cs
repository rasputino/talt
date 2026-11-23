using Talt.Core.Terrain;

namespace Talt.Core.Maps.Stages;

/// <summary>
/// Stage 1: raw terrain shape. Continents, hills and ridged mountains from seeded noise, then a
/// monotone remap that puts the requested share of the map under the sea and the highest point at
/// the maximum height (research R1, R2). Uses only IEEE-exact float operations.
/// </summary>
internal static class HeightField
{
    private const int HistogramBins = 4096;
    private const float HillScale = 70f;
    private const float MountainScale = 180f;
    private const float WarpScale = 220f;
    private const float WarpStrength = 60f;

    public static void Run(MapBuilder b)
    {
        var settings = b.Settings;
        var continent = b.Noise(1);
        var hills = b.Noise(2);
        var mountains = b.Noise(3);
        var warpX = b.Noise(4);
        var warpZ = b.Noise(5);

        var continentScale = Math.Clamp(Math.Min(b.Width, b.Depth) / 3f, 96f, 640f);
        var octaves = 2 + settings.Roughness / 20;
        var persistence = 0.35f + settings.Roughness * 0.004f;
        var hillAmplitude = 0.10f + settings.Roughness * 0.0055f;
        const float mountainAmplitude = 0.30f;

        var raw = new ushort[b.TileCount];
        var bandCount = RowBands.Count(b.Depth, b.Tuning.BandRows);
        var histograms = new int[bandCount][];
        var minimums = new int[bandCount];
        var maximums = new int[bandCount];

        b.ForBands((band, z0, z1) =>
        {
            var histogram = new int[HistogramBins];
            int min = ushort.MaxValue, max = 0;
            for (var z = z0; z < z1; z++)
            {
                for (var x = 0; x < b.Width; x++)
                {
                    var wx = x + (warpX.Fractal(x / WarpScale, z / WarpScale, 2) - 0.5f) * WarpStrength;
                    var wz = z + (warpZ.Fractal(x / WarpScale, z / WarpScale, 2) - 0.5f) * WarpStrength;

                    var land = continent.Fractal(x / continentScale, z / continentScale, 3);
                    var detail = hills.Fractal(wx / HillScale, wz / HillScale, octaves, persistence);
                    var ridge = 1f - MathF.Abs(2f * mountains.Fractal(wx / MountainScale, wz / MountainScale, 3) - 1f);
                    var mask = SmoothStep((land - 0.45f) / 0.3f);

                    var value = land * 0.6f + (detail - 0.5f) * hillAmplitude + ridge * ridge * mask * mountainAmplitude;
                    var quantized = (int)(Math.Clamp(value, 0f, 1f) * 65535f);
                    raw[x + z * b.Width] = (ushort)quantized;
                    histogram[quantized >> 4]++;
                    min = Math.Min(min, quantized);
                    max = Math.Max(max, quantized);
                }
            }

            histograms[band] = histogram;
            minimums[band] = min;
            maximums[band] = max;
        });

        var total = new int[HistogramBins];
        int low = ushort.MaxValue, high = 0;
        for (var band = 0; band < bandCount; band++)
        {
            for (var i = 0; i < HistogramBins; i++)
            {
                total[i] += histograms[band][i];
            }

            low = Math.Min(low, minimums[band]);
            high = Math.Max(high, maximums[band]);
        }

        var threshold = SeaThreshold(total, b.TileCount, settings.WaterAmount, low);
        Remap(b, raw, low, threshold, high);
    }

    /// <summary>
    /// Raw value at which the sea ends: the smallest value such that at least the requested share of
    /// tiles lies at or below it (the minimum when no sea is wanted).
    /// </summary>
    internal static int SeaThreshold(int[] histogram, int tileCount, int waterAmount, int minimum)
    {
        if (waterAmount == 0)
        {
            return minimum;
        }

        var wanted = (long)tileCount * waterAmount / 100;
        long cumulative = 0;
        for (var bin = 0; bin < histogram.Length; bin++)
        {
            cumulative += histogram[bin];
            if (cumulative >= wanted)
            {
                return Math.Min(ushort.MaxValue, (bin << 4) + 15);
            }
        }

        return ushort.MaxValue;
    }

    private static void Remap(MapBuilder b, ushort[] raw, int low, int threshold, int high)
    {
        var sea = b.Settings.SeaLevel;
        var top = b.Settings.MaxHeight;
        long waterSpan = Math.Max(1, threshold - low);
        long landSpan = Math.Max(1, high - threshold);

        b.ForBands((_, z0, z1) =>
        {
            for (var i = z0 * b.Width; i < z1 * b.Width; i++)
            {
                long v = raw[i];
                int height;
                if (v <= threshold && threshold > low)
                {
                    // Below the sea surface: heights 1 to sea - 1.
                    height = Math.Min(1 + (int)((v - low) * (sea - 2) / waterSpan), sea - 1);
                }
                else
                {
                    height = Math.Clamp(sea + (int)((v - threshold) * (top - sea) / landSpan), sea, top);
                }

                b.Height[i] = (byte)height;
            }
        });
    }

    private static float SmoothStep(float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return t * t * (3f - 2f * t);
    }
}

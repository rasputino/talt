namespace Talt.Core.Terrain;

/// <summary>
/// Deterministic 2D value noise with fractal octaves. Same seed and input always give the same output.
/// </summary>
public sealed class ValueNoise
{
    private readonly uint _seed;

    public ValueNoise(int seed)
    {
        _seed = (uint)seed;
    }

    /// <summary>Single octave of noise in the range [0, 1].</summary>
    public float Sample(float x, float y)
    {
        var x0 = (int)MathF.Floor(x);
        var y0 = (int)MathF.Floor(y);
        var tx = SmoothStep(x - x0);
        var ty = SmoothStep(y - y0);

        var a = Lattice(x0, y0);
        var b = Lattice(x0 + 1, y0);
        var c = Lattice(x0, y0 + 1);
        var d = Lattice(x0 + 1, y0 + 1);

        return Lerp(Lerp(a, b, tx), Lerp(c, d, tx), ty);
    }

    /// <summary>Fractal (fBm) noise in the range [0, 1].</summary>
    public float Fractal(float x, float y, int octaves, float persistence = 0.5f, float lacunarity = 2f)
    {
        var sum = 0f;
        var amplitude = 1f;
        var frequency = 1f;
        var total = 0f;

        for (var i = 0; i < octaves; i++)
        {
            sum += Sample(x * frequency + i * 17.31f, y * frequency + i * 5.73f) * amplitude;
            total += amplitude;
            amplitude *= persistence;
            frequency *= lacunarity;
        }

        return sum / total;
    }

    private float Lattice(int x, int y) => Hash(x, y, _seed) / (float)uint.MaxValue;

    internal static uint Hash(int x, int y, uint seed)
    {
        unchecked
        {
            var h = seed ^ ((uint)x * 0x27d4eb2du) ^ ((uint)y * 0x165667b1u);
            h ^= h >> 15;
            h *= 0x85ebca6bu;
            h ^= h >> 13;
            h *= 0xc2b2ae35u;
            h ^= h >> 16;
            return h;
        }
    }

    private static float SmoothStep(float t) => t * t * (3f - 2f * t);

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;
}

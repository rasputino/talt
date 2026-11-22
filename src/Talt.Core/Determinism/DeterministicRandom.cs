using System.Numerics;

namespace Talt.Core.Determinism;

/// <summary>
/// Seeded, platform-independent random number generator: xoshiro256** seeded with SplitMix64
/// (both public-domain algorithms, implemented from their published descriptions; research R6).
/// </summary>
/// <remarks>
/// Only integer outputs are offered, so simulation code never introduces floating point. The four
/// state words are part of the simulation fingerprint. Not thread safe.
/// </remarks>
public sealed class DeterministicRandom : IStateHashable
{
    private ulong _s0;
    private ulong _s1;
    private ulong _s2;
    private ulong _s3;

    /// <summary>Creates a generator whose state is derived from <paramref name="seed"/> with SplitMix64.</summary>
    public DeterministicRandom(ulong seed)
    {
        var splitMix = seed;
        _s0 = SplitMix64(ref splitMix);
        _s1 = SplitMix64(ref splitMix);
        _s2 = SplitMix64(ref splitMix);
        _s3 = SplitMix64(ref splitMix);
        EnsureNotAllZero();
    }

    /// <summary>Creates a generator from raw state words (tests and reference vectors).</summary>
    internal DeterministicRandom(ulong s0, ulong s1, ulong s2, ulong s3)
    {
        _s0 = s0;
        _s1 = s1;
        _s2 = s2;
        _s3 = s3;
        EnsureNotAllZero();
    }

    /// <summary>Next 64 random bits.</summary>
    public ulong NextUInt64()
    {
        var result = BitOperations.RotateLeft(_s1 * 5, 7) * 9;
        var t = _s1 << 17;

        _s2 ^= _s0;
        _s3 ^= _s1;
        _s1 ^= _s2;
        _s0 ^= _s3;
        _s2 ^= t;
        _s3 = BitOperations.RotateLeft(_s3, 45);

        return result;
    }

    /// <summary>Uniform integer in [0, <paramref name="maxExclusive"/>), without modulo bias.</summary>
    public int NextInt(int maxExclusive)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxExclusive);
        var bound = (ulong)maxExclusive;

        // Reject the top partial range so every value is equally likely.
        var limit = ulong.MaxValue - (ulong.MaxValue % bound);
        ulong value;
        do
        {
            value = NextUInt64();
        }
        while (value >= limit);

        return (int)(value % bound);
    }

    /// <summary>Uniform integer in [<paramref name="minInclusive"/>, <paramref name="maxExclusive"/>).</summary>
    public int NextInt(int minInclusive, int maxExclusive)
    {
        if (maxExclusive <= minInclusive)
        {
            throw new ArgumentOutOfRangeException(nameof(maxExclusive), "The range must not be empty.");
        }

        return minInclusive + NextInt(maxExclusive - minInclusive);
    }

    /// <summary>
    /// Creates an independent generator for a sub-system. Does not consume values from this
    /// generator, so adding a stream never changes the sequence of others.
    /// </summary>
    public DeterministicRandom Fork(ulong streamId)
    {
        var seed = _s0 ^ BitOperations.RotateLeft(_s1, 13) ^ BitOperations.RotateLeft(_s2, 29) ^
                   BitOperations.RotateLeft(_s3, 43) ^ (streamId * 0x9E3779B97F4A7C15UL);
        return new DeterministicRandom(seed);
    }

    public void AppendTo(StateHasher hasher)
    {
        ArgumentNullException.ThrowIfNull(hasher);
        hasher.Append(_s0);
        hasher.Append(_s1);
        hasher.Append(_s2);
        hasher.Append(_s3);
    }

    private static ulong SplitMix64(ref ulong state)
    {
        state += 0x9E3779B97F4A7C15UL;
        var z = state;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    private void EnsureNotAllZero()
    {
        // xoshiro must never be in the all-zero state (it would only produce zeros).
        if ((_s0 | _s1 | _s2 | _s3) == 0)
        {
            _s0 = 0x9E3779B97F4A7C15UL;
        }
    }
}

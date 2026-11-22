using Talt.Core.Determinism;

namespace Talt.Core.Tests.Determinism;

public class DeterministicRandomTests
{
    [Fact]
    public void Xoshiro256StarStar_MatchesReferenceVector()
    {
        // Reference: xoshiro256** with state {1, 2, 3, 4} (worked out from the published algorithm).
        var random = new DeterministicRandom(1, 2, 3, 4);

        Assert.Equal(11520UL, random.NextUInt64());
        Assert.Equal(0UL, random.NextUInt64());
        Assert.Equal(1509978240UL, random.NextUInt64());
    }

    [Fact]
    public void SameSeed_GivesSameSequence()
    {
        var a = new DeterministicRandom(2026);
        var b = new DeterministicRandom(2026);

        for (var i = 0; i < 1000; i++)
        {
            Assert.Equal(a.NextUInt64(), b.NextUInt64());
        }
    }

    [Fact]
    public void DifferentSeeds_GiveDifferentSequences()
    {
        Assert.NotEqual(new DeterministicRandom(1).NextUInt64(), new DeterministicRandom(2).NextUInt64());
    }

    [Fact]
    public void NextInt_StaysInRangeAndCoversIt()
    {
        var random = new DeterministicRandom(7);
        var seen = new bool[10];

        for (var i = 0; i < 10_000; i++)
        {
            var value = random.NextInt(10);
            Assert.InRange(value, 0, 9);
            seen[value] = true;
        }

        Assert.All(seen, Assert.True);
    }

    [Fact]
    public void NextInt_WithRange_IsRoughlyUniform()
    {
        var random = new DeterministicRandom(11);
        var counts = new int[4];
        for (var i = 0; i < 40_000; i++)
        {
            counts[random.NextInt(5, 9) - 5]++;
        }

        Assert.All(counts, c => Assert.InRange(c, 9_000, 11_000));
    }

    [Fact]
    public void NextInt_RejectsEmptyRanges()
    {
        var random = new DeterministicRandom(1);
        Assert.Throws<ArgumentOutOfRangeException>(() => random.NextInt(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => random.NextInt(5, 5));
    }

    [Fact]
    public void Fork_IsReproducible_IndependentAndDoesNotConsume()
    {
        var parent = new DeterministicRandom(99);
        var reference = new DeterministicRandom(99);

        var first = parent.Fork(1);
        var again = parent.Fork(1);
        var other = parent.Fork(2);

        Assert.Equal(first.NextUInt64(), again.NextUInt64());
        Assert.NotEqual(parent.Fork(1).NextUInt64(), other.NextUInt64());
        Assert.Equal(reference.NextUInt64(), parent.NextUInt64()); // Forking consumed nothing.
    }

    [Fact]
    public void AllZeroState_IsReplaced()
    {
        var random = new DeterministicRandom(0, 0, 0, 0);
        Assert.NotEqual(0UL, random.NextUInt64() | random.NextUInt64());
    }

    [Fact]
    public void StateIsPartOfTheFingerprint()
    {
        var random = new DeterministicRandom(5);
        var before = Fingerprint(random);
        random.NextUInt64();
        Assert.NotEqual(before, Fingerprint(random));
    }

    private static ulong Fingerprint(DeterministicRandom random)
    {
        var hasher = new StateHasher();
        hasher.Append(random);
        return hasher.GetFingerprint();
    }
}

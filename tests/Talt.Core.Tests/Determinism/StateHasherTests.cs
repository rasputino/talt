using Talt.Core.Determinism;

namespace Talt.Core.Tests.Determinism;

public class StateHasherTests
{
    [Fact]
    public void EqualInputs_GiveEqualFingerprints()
    {
        Assert.Equal(Hash(1, 2, [3, 4, 5]), Hash(1, 2, [3, 4, 5]));
    }

    [Fact]
    public void AnyChange_GivesDifferentFingerprint()
    {
        var reference = Hash(1, 2, [3, 4, 5]);
        Assert.NotEqual(reference, Hash(1, 2, [3, 4, 6]));
        Assert.NotEqual(reference, Hash(1, 3, [3, 4, 5]));
        Assert.NotEqual(reference, Hash(1, 2, [3, 4]));
    }

    [Fact]
    public void OrderMatters()
    {
        Assert.NotEqual(Hash(1, 2, []), Hash(2, 1, []));
    }

    [Fact]
    public void SpanLengthIsHashed_SoSplitsDiffer()
    {
        var a = new StateHasher();
        a.Append<int>([1, 2]);
        a.Append<int>([3]);
        var b = new StateHasher();
        b.Append<int>([1]);
        b.Append<int>([2, 3]);
        Assert.NotEqual(a.GetFingerprint(), b.GetFingerprint());
    }

    [Fact]
    public void Format_RoundTrips()
    {
        const ulong value = 0x0C11E2A9F07B3D45;
        Assert.Equal("0C11E2A9F07B3D45", StateHasher.Format(value));
        Assert.Equal(value, StateHasher.Parse("0C11E2A9F07B3D45"));
    }

    [Fact]
    public void RunsOnLittleEndianPlatforms()
    {
        Assert.True(BitConverter.IsLittleEndian);
        _ = new StateHasher();
    }

    private static ulong Hash(int a, long b, int[] values)
    {
        var hasher = new StateHasher();
        hasher.Append(a);
        hasher.Append(b);
        hasher.Append<int>(values);
        return hasher.GetFingerprint();
    }
}

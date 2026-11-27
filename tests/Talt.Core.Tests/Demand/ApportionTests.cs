using Talt.Core.Demand;

namespace Talt.Core.Tests.Demand;

public sealed class ApportionTests
{
    [Fact]
    public void EveryCall_HandsOutExactlyTheAmount_AndLongRunFollowsShares()
    {
        int[] shares = [500_000, 300_000, 150_000, 50_000];
        var carry = new long[4];
        var units = new int[4];
        var totals = new long[4];
        long given = 0;
        for (var call = 0; call < 5_000; call++)
        {
            var amount = (call * 7) % 5; // 0..4 units, often less than the number of parts
            Apportion.Split(shares, carry, amount, units);
            var sum = 0;
            for (var i = 0; i < 4; i++)
            {
                Assert.True(units[i] >= 0);
                sum += units[i];
                totals[i] += units[i];
            }

            Assert.Equal(amount, sum);
            given += amount;
        }

        for (var i = 0; i < 4; i++)
        {
            var expected = given * shares[i] / Apportion.ShareScale;
            Assert.InRange(totals[i], expected - 1, expected + 1);
        }
    }

    [Fact]
    public void SharesSummingToLessThanTheScale_StillHandOutEverything()
    {
        int[] shares = [100, 300];
        var carry = new long[2];
        var units = new int[2];
        Apportion.Split(shares, carry, 1000, units);
        Assert.Equal(250, units[0]);
        Assert.Equal(750, units[1]);
    }

    [Fact]
    public void SplitTwo_IsExactOverTime()
    {
        var carry = 0;
        var second = 0;
        for (var i = 0; i < 1000; i++)
        {
            second += Apportion.SplitTwo(3, 500, ref carry);
        }

        Assert.Equal(1500, second);
        Assert.Equal(0, Apportion.SplitTwo(5, 0, ref carry));
        Assert.Equal(5, Apportion.SplitTwo(5, 1000, ref carry));
    }
}

using Talt.Tests.Support;
using Xunit.Sdk;

namespace Talt.Core.Tests.TestSupport;

public class AllocationAssertTests
{
    [Fact]
    public void CodeThatAllocatesEveryIteration_Fails()
    {
        object? sink = null;

        var failure = Assert.Throws<FailException>(() =>
            AllocationAssert.NoSteadyStateAllocations(() => sink = new byte[16], iterationsPerRound: 10, "allocating code"));

        Assert.Contains("allocating code", failure.Message, StringComparison.Ordinal);
        Assert.NotNull(sink);
    }

    [Fact]
    public void CodeThatDoesNotAllocate_Passes()
    {
        var buffer = new int[64];
        var i = 0;

        AllocationAssert.NoSteadyStateAllocations(() => buffer[i++ % buffer.Length]++, iterationsPerRound: 100, "array writes");
    }
}

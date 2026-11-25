namespace Talt.Tests.Support;

/// <summary>
/// Checks that code allocates nothing once warm, without failing on the runtime's own one-off
/// allocations. Shared by the Core and Client test projects (linked into Talt.Client.Tests).
/// </summary>
/// <remarks>
/// Measuring a single window with <see cref="GC.GetAllocatedBytesForCurrentThread"/> proved fragile in CI:
/// tiered compilation, lazy runtime initialisation or test parallelism occasionally add a few hundred
/// bytes inside the window on one platform (496 bytes on macOS arm64, 4 272 on Windows arm64), with
/// no allocation in the code under test. So the code is warmed up with as many iterations as one
/// window, and up to <see cref="Rounds"/> windows are measured: the check passes as soon as one window
/// allocates nothing. Code that really allocates does so in every window and still fails.
/// </remarks>
internal static class AllocationAssert
{
    /// <summary>Measured windows before giving up.</summary>
    public const int Rounds = 3;

    /// <summary>Asserts that <paramref name="iteration"/> allocates nothing in steady state.</summary>
    /// <param name="iteration">One unit of the work under test (one frame, one tick, one preview...).</param>
    /// <param name="iterationsPerRound">Iterations per measured window, also used for the warm-up.</param>
    /// <param name="what">Description for the failure message, for example "200 road previews".</param>
    public static void NoSteadyStateAllocations(Action iteration, int iterationsPerRound, string what)
    {
        ArgumentNullException.ThrowIfNull(iteration);
        ArgumentOutOfRangeException.ThrowIfLessThan(iterationsPerRound, 1);

        for (var i = 0; i < iterationsPerRound; i++)
        {
            iteration();
        }

        Span<long> allocated = stackalloc long[Rounds];
        for (var round = 0; round < Rounds; round++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < iterationsPerRound; i++)
            {
                iteration();
            }

            allocated[round] = GC.GetAllocatedBytesForCurrentThread() - before;
            if (allocated[round] == 0)
            {
                return;
            }
        }

        Assert.Fail($"{what} allocated in each of {Rounds} measured windows: {allocated[0]}, {allocated[1]} and {allocated[2]} bytes.");
    }
}

using Talt.Core.Simulation;

namespace Talt.Core.Tests.Simulation;

/// <summary>
/// Controllable time source: each <see cref="GetTimestamp"/> call can advance time by a fixed cost,
/// which simulates how long a tick takes.
/// </summary>
internal sealed class FakeTimeSource : ITimeSource
{
    private long _now;

    /// <summary>Microseconds added on every timestamp read (simulated work).</summary>
    public long CostPerReadMicroseconds { get; set; }

    public long Frequency => 1_000_000;

    public long GetTimestamp()
    {
        _now += CostPerReadMicroseconds;
        return _now;
    }
}

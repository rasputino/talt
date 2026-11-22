using System.Diagnostics;
using Talt.Core.Simulation;

namespace Talt.Client;

/// <summary>Real time for the simulation runner, from the high-resolution system timer.</summary>
internal sealed class StopwatchTimeSource : ITimeSource
{
    public long Frequency => Stopwatch.Frequency;

    public long GetTimestamp() => Stopwatch.GetTimestamp();
}

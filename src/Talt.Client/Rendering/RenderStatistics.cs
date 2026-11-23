using System.Diagnostics;

namespace Talt.Client.Rendering;

/// <summary>Milliseconds spent in each phase of one drawn frame.</summary>
/// <param name="Acquire">Waiting for the next swap-chain image (vertical sync waits here).</param>
/// <param name="Prepare">Uploads, culling, level-of-detail choice and job scheduling.</param>
/// <param name="Encode">Recording the render pass.</param>
/// <param name="WaitGpu">Waiting for the GPU (at most two frames in flight).</param>
/// <param name="Submit">Submitting and presenting.</param>
internal readonly record struct DrawTimings(double Acquire, double Prepare, double Encode, double WaitGpu, double Submit);

/// <summary>Counters of the last drawn frame (FR-010).</summary>
internal struct RenderStatistics
{
    public int DrawCalls;
    public long Triangles;
    public int RegionsLod0;
    public int RegionsLod1;
    public int RegionsLod2;
    public int RegionsLod3;
    public int RegionsCulled;
    public long WorldGpuBytes;

    /// <summary>Full-detail region buffers released since start-up (research R3).</summary>
    public long Lod0Released;
    public int PendingJobs;
    public int JobsInFlight;

    /// <summary>Regions drawn at a level of detail.</summary>
    public readonly int RegionsAt(int lod) => lod switch
    {
        0 => RegionsLod0,
        1 => RegionsLod1,
        2 => RegionsLod2,
        _ => RegionsLod3,
    };

    public void CountRegion(int lod)
    {
        switch (lod)
        {
            case 0: RegionsLod0++; break;
            case 1: RegionsLod1++; break;
            case 2: RegionsLod2++; break;
            default: RegionsLod3++; break;
        }
    }
}

/// <summary>
/// Frame-time average and worst frame over a window of time (one second in the game), without
/// allocating.
/// </summary>
internal sealed class FrameTimeTracker
{
    private readonly double _windowSeconds;
    private double _sumMs;
    private double _worstMs;
    private int _frames;
    private double _elapsedSeconds;

    public FrameTimeTracker(double windowSeconds = 1.0)
    {
        _windowSeconds = windowSeconds;
    }

    /// <summary>Average frame time of the last completed window, in milliseconds.</summary>
    public double AverageMs { get; private set; }

    /// <summary>Longest frame of the last completed window, in milliseconds.</summary>
    public double WorstMs { get; private set; }

    /// <summary>Frames per second of the last completed window.</summary>
    public double FramesPerSecond { get; private set; }

    /// <summary>Records one frame. Returns true when a window completed and the results changed.</summary>
    public bool Record(double frameMs)
    {
        _sumMs += frameMs;
        _worstMs = Math.Max(_worstMs, frameMs);
        _frames++;
        _elapsedSeconds += frameMs / 1000.0;
        if (_elapsedSeconds < _windowSeconds)
        {
            return false;
        }

        AverageMs = _sumMs / _frames;
        WorstMs = _worstMs;
        FramesPerSecond = _frames / _elapsedSeconds;
        _sumMs = 0;
        _worstMs = 0;
        _frames = 0;
        _elapsedSeconds = 0;
        return true;
    }

    /// <summary>Milliseconds since <paramref name="startTimestamp"/> (<see cref="Stopwatch.GetTimestamp"/>).</summary>
    public static double ElapsedMs(long startTimestamp) => Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;
}

using System.Numerics;

namespace Talt.Core.Simulation;

/// <summary>
/// Rendering only: blends vehicle poses between the last two ticks so motion looks smooth at any
/// frame rate (research R4). Uses floating point, but never touches simulation state; it lives in
/// Core only so it can be unit-tested without a window.
/// </summary>
public static class PoseInterpolator
{
    /// <summary>
    /// Pose between <paramref name="previous"/> and <paramref name="current"/>. The position moves along
    /// the straight segment between the two (never past either end); the heading is the current one.
    /// </summary>
    public static VehiclePose Interpolate(VehiclePose previous, VehiclePose current, float factor)
    {
        var t = Math.Clamp(factor, 0f, 1f);
        return new VehiclePose(Vector2.Lerp(previous.Position, current.Position, t), current.Direction);
    }

    /// <summary>
    /// Interpolates every vehicle. A slot is only interpolated when it holds the same vehicle id in
    /// both snapshots; vehicles added, removed or moved to another slot are drawn at their current pose.
    /// </summary>
    public static void Interpolate(
        ReadOnlySpan<VehiclePose> previous,
        ReadOnlySpan<int> previousIds,
        ReadOnlySpan<VehiclePose> current,
        ReadOnlySpan<int> currentIds,
        float factor,
        Span<VehiclePose> output)
    {
        if (output.Length < current.Length || currentIds.Length < current.Length)
        {
            throw new ArgumentException("Output and id spans must be at least as long as the current poses.");
        }

        for (var i = 0; i < current.Length; i++)
        {
            output[i] = i < previous.Length && i < previousIds.Length && previousIds[i] == currentIds[i]
                ? Interpolate(previous[i], current[i], factor)
                : current[i];
        }
    }
}

using System.Numerics;
using Talt.Core.Simulation;

namespace Talt.Core.Tests.Simulation;

/// <summary>SC-006: smooth motion, never backwards, never jumping.</summary>
public class PoseInterpolatorTests
{
    private static readonly VehiclePose Previous = new(new Vector2(10, 5), new Vector2(1, 0));
    private static readonly VehiclePose Current = new(new Vector2(10.5f, 5), new Vector2(1, 0));

    [Theory]
    [InlineData(0f, 10f)]
    [InlineData(0.5f, 10.25f)]
    [InlineData(1f, 10.5f)]
    public void Interpolate_MovesAlongTheSegment(float factor, float expectedX)
    {
        Assert.Equal(expectedX, PoseInterpolator.Interpolate(Previous, Current, factor).Position.X, 4);
    }

    [Fact]
    public void Interpolate_NeverGoesBackwardsOrPastTheCurrentPose()
    {
        var last = float.MinValue;
        for (var f = -0.5f; f <= 1.5f; f += 0.05f)
        {
            var x = PoseInterpolator.Interpolate(Previous, Current, f).Position.X;
            Assert.True(x >= last - 1e-6f);
            Assert.InRange(x, Previous.Position.X, Current.Position.X); // No jump beyond one tick of travel.
            last = x;
        }
    }

    [Fact]
    public void Interpolate_UsesTheCurrentHeading()
    {
        var turned = Current with { Direction = new Vector2(0, 1) };
        Assert.Equal(new Vector2(0, 1), PoseInterpolator.Interpolate(Previous, turned, 0.3f).Direction);
    }

    [Fact]
    public void Batch_SkipsSlotsWhoseVehicleChanged()
    {
        VehiclePose[] previous = [Previous, Previous];
        VehiclePose[] current = [Current, Current];
        var output = new VehiclePose[2];

        PoseInterpolator.Interpolate(previous, [1, 2], current, [1, 7], 0.5f, output);

        Assert.Equal(10.25f, output[0].Position.X, 4); // Same vehicle: interpolated.
        Assert.Equal(Current, output[1]);              // Different vehicle in the slot: current pose.
    }

    [Fact]
    public void Batch_DrawsNewVehiclesAtTheirCurrentPose()
    {
        var output = new VehiclePose[2];

        PoseInterpolator.Interpolate([Previous], [1], [Current, Current], [1, 2], 0.5f, output);

        Assert.Equal(Current, output[1]);
    }
}

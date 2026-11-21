using System.Numerics;

namespace Talt.Client.Rendering;

/// <summary>
/// Camera orbiting around a target point on the ground, like in classic management games.
/// </summary>
internal sealed class OrbitCamera
{
    private const float MinPitch = 0.25f;
    private const float MaxPitch = 1.45f;
    private const float MinDistance = 10f;
    private const float MaxDistance = 2000f;

    private float _pitch = 0.85f;
    private float _distance = 350f;

    public Vector3 Target { get; set; }

    /// <summary>Rotation around the vertical axis, in radians.</summary>
    public float Yaw { get; set; } = MathF.PI / 4f;

    /// <summary>Angle above the horizon, in radians.</summary>
    public float Pitch
    {
        get => _pitch;
        set => _pitch = Math.Clamp(value, MinPitch, MaxPitch);
    }

    public float Distance
    {
        get => _distance;
        set => _distance = Math.Clamp(value, MinDistance, MaxDistance);
    }

    public float FieldOfView { get; set; } = 35f * MathF.PI / 180f;

    public Vector3 Position => Target + new Vector3(
        MathF.Cos(Pitch) * MathF.Sin(Yaw),
        MathF.Sin(Pitch),
        MathF.Cos(Pitch) * MathF.Cos(Yaw)) * Distance;

    public Matrix4x4 ViewProjection(float aspectRatio)
    {
        var view = Matrix4x4.CreateLookAt(Position, Target, Vector3.UnitY);
        var far = Distance * 4f + 1000f;
        // System.Numerics produces a [0, 1] depth range, which is what WebGPU expects.
        var projection = Matrix4x4.CreatePerspectiveFieldOfView(FieldOfView, aspectRatio, 1f, far);
        return view * projection;
    }

    /// <summary>Moves the target on the ground plane, relative to the current view direction.</summary>
    /// <param name="right">Movement to the right of the screen, in world units.</param>
    /// <param name="forward">Movement towards the top of the screen, in world units.</param>
    public void Pan(float right, float forward)
    {
        var forwardDir = new Vector3(-MathF.Sin(Yaw), 0f, -MathF.Cos(Yaw));
        var rightDir = new Vector3(MathF.Cos(Yaw), 0f, -MathF.Sin(Yaw));
        Target += rightDir * right + forwardDir * forward;
    }
}

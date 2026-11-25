using System.Numerics;

namespace Talt.Client.Rendering;

/// <summary>One shadow cascade: the light's view-projection, its far view depth and its texel size.</summary>
internal readonly record struct ShadowCascade(Matrix4x4 ViewProjection, float FarDepth, float TexelSize);

/// <summary>
/// Fits the cascades of the sun's shadow map to the camera (feature 018, research R1). Cascade k covers
/// view depths up to its split (practical split scheme); its orthographic light projection encloses the
/// bounding sphere of that slice of the view frustum, so the projection's size depends only on the zoom and
/// the field of view, not on where the camera looks; its centre is snapped to whole shadow texels, so
/// shadows do not shimmer while the camera moves. Pure maths, tested without a GPU.
/// </summary>
internal static class ShadowCascades
{
    public const int MaxCascades = 3;

    /// <summary>Depth kept in front of and behind each sphere for casters outside it (mountains, towers).</summary>
    public const float CasterMargin = 192f;

    /// <summary>Blend between uniform (0) and logarithmic (1) split distances.</summary>
    private const float SplitLambda = 0.75f;

    /// <summary>
    /// Far view depth of each cascade between <paramref name="near"/> and <paramref name="distance"/>.
    /// </summary>
    public static void Splits(float near, float distance, Span<float> splits)
    {
        var count = splits.Length;
        for (var i = 1; i <= count; i++)
        {
            var f = i / (float)count;
            var logarithmic = near * MathF.Pow(distance / near, f);
            var uniform = near + ((distance - near) * f);
            splits[i - 1] = (SplitLambda * logarithmic) + ((1f - SplitLambda) * uniform);
        }

        splits[count - 1] = distance;
    }

    /// <summary>
    /// Bounding sphere (centre depth along the view axis, radius) of the view frustum between view depths
    /// <paramref name="near"/> and <paramref name="far"/>; <paramref name="tanHalfVertical"/> is tan(fov / 2).
    /// </summary>
    public static (float CentreDepth, float Radius) SliceSphere(float near, float far, float tanHalfVertical, float aspect)
    {
        var tanHalfHorizontal = tanHalfVertical * aspect;
        var k2 = (tanHalfVertical * tanHalfVertical) + (tanHalfHorizontal * tanHalfHorizontal);
        var centre = (far + near) * (1f + k2) / 2f;
        if (centre > far)
        {
            return (far, far * MathF.Sqrt(k2));
        }

        var radius = MathF.Sqrt(((far - centre) * (far - centre)) + (k2 * far * far));
        return (centre, radius);
    }

    /// <summary>
    /// Light view-projection for a sphere: orthographic, looking along <paramref name="lightDirection"/>, with
    /// the sphere's centre snapped to the texel grid of a <paramref name="mapSize"/>-texel shadow map.
    /// </summary>
    public static Matrix4x4 Fit(Vector3 centre, float radius, Vector3 lightDirection, int mapSize)
    {
        var direction = Vector3.Normalize(lightDirection);
        var up = MathF.Abs(direction.Y) > 0.99f ? Vector3.UnitZ : Vector3.UnitY;

        // Snap in light space: a fixed light orientation, so moving the centre by whole texels moves the
        // whole shadow image by whole texels.
        var lightView = Matrix4x4.CreateLookAt(Vector3.Zero, direction, up);
        var texel = 2f * radius / mapSize;
        var local = Vector3.Transform(centre, lightView);
        local.X = MathF.Round(local.X / texel) * texel;
        local.Y = MathF.Round(local.Y / texel) * texel;
        Matrix4x4.Invert(lightView, out var inverse);
        var snapped = Vector3.Transform(local, inverse);

        var depth = radius + CasterMargin;
        var eye = snapped - (direction * depth);
        var view = Matrix4x4.CreateLookAt(eye, snapped, up);
        var projection = Matrix4x4.CreateOrthographicOffCenter(-radius, radius, -radius, radius, 0f, 2f * depth);
        return view * projection;
    }

    /// <summary>
    /// Fits <paramref name="cascades"/>.Length cascades for a camera; returns the number filled.
    /// </summary>
    public static int Compute(
        Vector3 cameraPosition, Vector3 cameraForward, float fieldOfView, float aspect, float near, float shadowDistance,
        Vector3 lightDirection, int mapSize, Span<ShadowCascade> cascades)
    {
        var count = Math.Clamp(cascades.Length, 1, MaxCascades);
        Span<float> splits = stackalloc float[count];
        Splits(near, shadowDistance, splits);
        var tanHalf = MathF.Tan(fieldOfView / 2f);
        var forward = Vector3.Normalize(cameraForward);
        var previous = near;
        for (var i = 0; i < count; i++)
        {
            var (centreDepth, radius) = SliceSphere(previous, splits[i], tanHalf, aspect);

            // Quantise the radius so tiny zoom changes do not rescale the texel grid every frame.
            radius = MathF.Ceiling(radius / 4f) * 4f;
            var centre = cameraPosition + (forward * centreDepth);
            cascades[i] = new ShadowCascade(Fit(centre, radius, lightDirection, mapSize), splits[i], 2f * radius / mapSize);
            previous = splits[i];
        }

        return count;
    }
}

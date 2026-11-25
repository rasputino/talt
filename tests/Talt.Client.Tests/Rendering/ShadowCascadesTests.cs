using System.Numerics;
using Talt.Client.Rendering;

namespace Talt.Client.Tests.Rendering;

public class ShadowCascadesTests
{
    private static readonly Vector3 Light = Vector3.Normalize(new Vector3(-0.45f, -1f, -0.3f));

    [Fact]
    public void Splits_IncreaseAndEndAtTheShadowDistance()
    {
        Span<float> splits = stackalloc float[3];

        ShadowCascades.Splits(1f, 300f, splits);

        Assert.True(splits[0] < splits[1] && splits[1] < splits[2]);
        Assert.Equal(300f, splits[2]);
        Assert.True(splits[0] > 1f);
    }

    [Fact]
    public void SliceSphere_ContainsTheSliceCorners()
    {
        var tan = MathF.Tan(0.3f);
        var (centre, radius) = ShadowCascades.SliceSphere(10f, 80f, tan, 16f / 9f);

        foreach (var depth in new[] { 10f, 80f })
        {
            var corner = new Vector3(tan * 16f / 9f * depth, tan * depth, depth);
            Assert.True(Vector3.Distance(corner, new Vector3(0, 0, centre)) <= radius + 0.01f);
        }
    }

    [Fact]
    public void Fit_ProjectsTheSphereInsideClipSpace()
    {
        var centre = new Vector3(500f, 20f, 700f);
        var matrix = ShadowCascades.Fit(centre, 50f, Light, 2048);

        foreach (var offset in new[] { Vector3.UnitX, -Vector3.UnitX, Vector3.UnitY, -Vector3.UnitY, Vector3.UnitZ, -Vector3.UnitZ })
        {
            var clip = Vector4.Transform(new Vector4(centre + (offset * 49f), 1f), matrix);
            Assert.InRange(clip.X, -1f, 1f);
            Assert.InRange(clip.Y, -1f, 1f);
            Assert.InRange(clip.Z, 0f, 1f);
        }
    }

    [Fact]
    public void Fit_MovesByWholeTexels_SoShadowsDoNotShimmer()
    {
        const int size = 1024;
        const float radius = 64f;
        var texelClip = 2f / size;
        var a = ShadowCascades.Fit(new Vector3(300f, 10f, 300f), radius, Light, size);
        var b = ShadowCascades.Fit(new Vector3(300.37f, 10f, 301.11f), radius, Light, size);

        // A fixed world point lands on the same sub-texel position in both cascades.
        var point = new Vector4(310f, 12f, 290f, 1f);
        var pa = Vector4.Transform(point, a);
        var pb = Vector4.Transform(point, b);
        var fractionA = (pa.X / texelClip) - MathF.Floor(pa.X / texelClip);
        var fractionB = (pb.X / texelClip) - MathF.Floor(pb.X / texelClip);
        Assert.Equal(fractionA, fractionB, 2);
    }

    [Fact]
    public void Compute_FillsCascadesWithGrowingTexels()
    {
        Span<ShadowCascade> cascades = stackalloc ShadowCascade[3];
        var count = ShadowCascades.Compute(
            new Vector3(0, 200, 0), Vector3.Normalize(new Vector3(0.5f, -1f, 0.5f)), 0.6f, 16f / 9f, 4f, 400f, Light, 2048, cascades);

        Assert.Equal(3, count);
        Assert.True(cascades[0].TexelSize < cascades[1].TexelSize);
        Assert.True(cascades[1].TexelSize < cascades[2].TexelSize);
        Assert.Equal(400f, cascades[2].FarDepth);
    }
}

using System.Numerics;

namespace Talt.Client.Rendering;

/// <summary>
/// The light of a frame (feature 018, research R2/R10): sun direction and colour (intensity included),
/// ambient light from the sky and from the ground, the fog/sky colour, exposure and how much of night it
/// is (lit windows). Render-only floats, never simulation state.
/// </summary>
internal readonly record struct LightState(
    Vector3 SunDirection,
    Vector3 SunColor,
    Vector3 SkyAmbient,
    Vector3 GroundAmbient,
    Vector3 SkyColor,
    float Exposure,
    float Night)
{
    /// <summary>The fixed daylight used when the day/night cycle is off.</summary>
    /// <remarks>
    /// The sun shines from over the default camera's left shoulder (the camera looks towards −X −Z), so the
    /// two visible walls of a building differ in brightness and shadows fall to the right where they show.
    /// </remarks>
    public static LightState Day { get; } = new(
        Vector3.Normalize(new Vector3(0.25f, -1f, -0.65f)),
        new Vector3(0.70f, 0.65f, 0.57f),
        new Vector3(0.25f, 0.29f, 0.37f),
        new Vector3(0.19f, 0.17f, 0.13f),
        new Vector3(0.40f, 0.66f, 1.25f),
        1f,
        0f);

    /// <summary>Linear interpolation of every value (the direction is renormalized).</summary>
    public static LightState Lerp(in LightState a, in LightState b, float t) => new(
        Vector3.Normalize(Vector3.Lerp(a.SunDirection, b.SunDirection, t)),
        Vector3.Lerp(a.SunColor, b.SunColor, t),
        Vector3.Lerp(a.SkyAmbient, b.SkyAmbient, t),
        Vector3.Lerp(a.GroundAmbient, b.GroundAmbient, t),
        Vector3.Lerp(a.SkyColor, b.SkyColor, t),
        a.Exposure + ((b.Exposure - a.Exposure) * t),
        a.Night + ((b.Night - a.Night) * t));
}

/// <summary>Colour maths shared with lighting.wgsl.</summary>
internal static class Lighting
{
    /// <summary>
    /// Same curve as <c>tone_map</c> in lighting.wgsl (Narkowicz's ACES fit after exposure), so the clear
    /// colour equals the fully fogged sky drawn by the shaders.
    /// </summary>
    public static Vector3 ToneMap(Vector3 color, float exposure) =>
        new(Curve(color.X * exposure), Curve(color.Y * exposure), Curve(color.Z * exposure));

    private static float Curve(float x) => Math.Clamp(x * ((2.51f * x) + 0.03f) / ((x * ((2.43f * x) + 0.59f)) + 0.14f), 0f, 1f);
}

/// <summary>
/// The presentation clock of the day/night cycle (feature 018, data-model.md TimeOfDay, research R10): one
/// cycle lasts <see cref="CycleSeconds"/> real seconds, it advances only while the game is not paused, and it
/// starts at mid-morning. Phase 0 is midnight, 0.25 sunrise, 0.5 noon, 0.75 sunset; night is about a
/// quarter of the cycle. Not simulation state.
/// </summary>
internal sealed class TimeOfDay
{
    public const double CycleSeconds = 720;
    public const double StartPhase = 0.35;

    private static readonly (float Phase, LightState Light)[] Keys =
    [
        (0.00f, Night()),
        (0.17f, Night()),
        (0.24f, Dawn()),
        (0.32f, Morning()),
        (0.42f, LightState.Day),
        (0.58f, LightState.Day),
        (0.68f, Morning()),
        (0.76f, Dusk()),
        (0.83f, Night()),
        (1.00f, Night()),
    ];

    /// <summary>Position in the cycle, 0–1.</summary>
    public double Phase { get; set; } = StartPhase;

    /// <summary>Advances the clock unless <paramref name="paused"/>.</summary>
    public void Advance(double seconds, bool paused)
    {
        if (!paused && seconds > 0)
        {
            Phase = (Phase + (seconds / CycleSeconds)) % 1.0;
        }
    }

    /// <summary>The light at the current phase.</summary>
    public LightState Light => At((float)Phase);

    /// <summary>The light at a phase: colours from keyframes, sun (or moon) direction from the phase.</summary>
    public static LightState At(float phase)
    {
        phase -= MathF.Floor(phase);
        var i = 0;
        while (i < Keys.Length - 2 && phase > Keys[i + 1].Phase)
        {
            i++;
        }

        var (p0, a) = Keys[i];
        var (p1, b) = Keys[i + 1];
        var t = p1 > p0 ? (phase - p0) / (p1 - p0) : 0f;
        t = t * t * (3f - (2f * t)); // Smooth between keys.
        var light = LightState.Lerp(a, b, t);
        return light with { SunDirection = Direction(phase) };
    }

    /// <summary>
    /// Direction the light travels: the sun's arc from east (sunrise) through south (noon, about 60° high)
    /// to west, and at night the moon opposite it; the switch happens at the horizon, where the light is dim.
    /// </summary>
    public static Vector3 Direction(float phase)
    {
        var angle = 2f * MathF.PI * (phase - 0.25f);
        var height = MathF.Sin(angle);
        var source = new Vector3(-MathF.Cos(angle), height, 0.35f + (0.25f * MathF.Abs(height)));
        if (height < 0f)
        {
            source = new Vector3(-source.X, -source.Y, source.Z); // The moon, above the horizon.
        }

        // Never quite horizontal: very long shadows are unreadable.
        source.Y = MathF.Max(source.Y, 0.25f);
        return -Vector3.Normalize(source);
    }

    private static LightState Night() => new(
        -Vector3.UnitY,
        new Vector3(0.07f, 0.09f, 0.17f),
        new Vector3(0.055f, 0.07f, 0.14f),
        new Vector3(0.02f, 0.02f, 0.035f),
        new Vector3(0.025f, 0.04f, 0.10f),
        1.25f,
        1f);

    private static LightState Dawn() => new(
        -Vector3.UnitY,
        new Vector3(0.62f, 0.34f, 0.19f),
        new Vector3(0.19f, 0.18f, 0.24f),
        new Vector3(0.12f, 0.09f, 0.07f),
        new Vector3(0.95f, 0.55f, 0.45f),
        1.05f,
        0.35f);

    private static LightState Morning() => new(
        -Vector3.UnitY,
        new Vector3(0.72f, 0.61f, 0.47f),
        new Vector3(0.23f, 0.25f, 0.32f),
        new Vector3(0.17f, 0.14f, 0.11f),
        new Vector3(0.62f, 0.75f, 1.15f),
        1f,
        0f);

    private static LightState Dusk() => new(
        -Vector3.UnitY,
        new Vector3(0.60f, 0.27f, 0.13f),
        new Vector3(0.18f, 0.14f, 0.22f),
        new Vector3(0.11f, 0.07f, 0.06f),
        new Vector3(1.05f, 0.45f, 0.32f),
        1.1f,
        0.5f);
}

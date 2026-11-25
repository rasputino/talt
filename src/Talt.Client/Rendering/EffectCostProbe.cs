using System.Globalization;
using System.Text;

namespace Talt.Client.Rendering;

/// <summary>
/// Measures the frame cost of each graphics effect on the real game (feature 018, research R12, SC-004):
/// with vertical sync off and the camera still, it runs the chosen preset, then the same preset with one
/// effect turned off at a time, each for a warm-up and a measuring period, and reports the average and
/// 99th-percentile frame time of each configuration and its difference from the full preset. Needs no input,
/// so it works although synthetic key presses do not reach the window.
/// </summary>
internal sealed class EffectCostProbe
{
    private const double WarmUpSeconds = 1.5;
    private const double FirstWarmUpSeconds = 6;

    private readonly List<(string Name, GraphicsSettings Settings, bool Models)> _steps = [];
    private readonly double _seconds;
    private readonly double[] _samples = new double[20_000];
    private readonly List<(string Name, double Average, double P99)> _results = [];
    private int _step;
    private double _elapsed;
    private int _sampleCount;

    public EffectCostProbe(GraphicsSettings preset, double secondsPerStep)
    {
        ArgumentNullException.ThrowIfNull(preset);
        _seconds = Math.Max(1, secondsPerStep);
        var full = preset.Clone();
        full.DayNight = false;
        _steps.Add(("full preset", full, true));
        Add("shadows off", full, s => s.Shadows = false);
        Add("anti-aliasing off", full, s => s.AntiAliasing = AntiAliasingMode.Off);
        Add("water effects off", full, s => s.WaterEffects = false);
        Add("scenery (bushes, rocks) off", full, s => s.SceneryDensity = SceneryDensityLevel.Off);
        Add("ambient occlusion off", full, s => s.AmbientOcclusion = AmbientOcclusionLevel.Off);
        Add("model detail low", full, s => s.ModelDetail = QualityLevel.Low);
        _steps.Add(("models not drawn", full, false));
        var dayNight = full.Clone();
        dayNight.DayNight = true;
        _steps.Add(("day/night on", dayNight, true));
    }

    /// <summary>Settings of the current step.</summary>
    public GraphicsSettings Current => _steps[Math.Min(_step, _steps.Count - 1)].Settings;

    /// <summary>Whether models are drawn in the current step.</summary>
    public bool ModelsEnabled => _steps[Math.Min(_step, _steps.Count - 1)].Models;

    public bool Finished => _step >= _steps.Count;

    /// <summary>Records one frame; returns true when the probe has just finished.</summary>
    public bool Record(double deltaSeconds, double frameMilliseconds)
    {
        if (Finished)
        {
            return false;
        }

        _elapsed += deltaSeconds;
        var warmUp = _step == 0 ? FirstWarmUpSeconds : WarmUpSeconds;
        if (_elapsed > warmUp && _sampleCount < _samples.Length)
        {
            _samples[_sampleCount++] = frameMilliseconds;
        }

        if (_elapsed < warmUp + _seconds)
        {
            return false;
        }

        var span = _samples.AsSpan(0, Math.Max(1, _sampleCount));
        var average = 0.0;
        foreach (var s in span)
        {
            average += s;
        }

        average /= span.Length;
        span.Sort();
        _results.Add((_steps[_step].Name, average, span[(int)Math.Min(span.Length - 1, span.Length * 0.99)]));
        _step++;
        _elapsed = 0;
        _sampleCount = 0;
        return Finished;
    }

    /// <summary>The result table (Markdown, ready for docs/ARCHITECTURE.md).</summary>
    public string Report(string scenario)
    {
        var text = new StringBuilder();
        text.AppendLine(CultureInfo.InvariantCulture, $"Effect costs — {scenario} (vertical sync off, camera still)");
        text.AppendLine("| Configuration | Avg frame (ms) | 99th pct (ms) | FPS | Effect cost (ms) |");
        text.AppendLine("|---|---|---|---|---|");
        var baseline = _results.Count > 0 ? _results[0].Average : 0;
        foreach (var (name, average, p99) in _results)
        {
            var cost = name == "full preset" ? "—"
                : name.EndsWith(" on", StringComparison.Ordinal) ? (average - baseline).ToString("+0.00;-0.00", CultureInfo.InvariantCulture)
                : (baseline - average).ToString("+0.00;-0.00", CultureInfo.InvariantCulture);
            text.AppendLine(CultureInfo.InvariantCulture, $"| {name} | {average:F2} | {p99:F2} | {1000 / average:F0} | {cost} |");
        }

        return text.ToString();
    }

    private void Add(string name, GraphicsSettings full, Action<GraphicsSettings> change)
    {
        var settings = full.Clone();
        change(settings);
        settings.Preset = GraphicsPreset.Custom;
        _steps.Add((name, settings, true));
    }
}

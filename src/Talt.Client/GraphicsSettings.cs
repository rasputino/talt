using System.Text.Json;

namespace Talt.Client;

public enum GraphicsPreset
{
    Low,
    Medium,
    High,
    Custom,
}

public enum QualityLevel
{
    Low,
    Medium,
    High,
}

public enum AntiAliasingMode
{
    Off,
    Msaa4,
}

public enum SceneryDensityLevel
{
    Off,
    Low,
    Medium,
    High,
}

public enum AmbientOcclusionLevel
{
    Off,
    Soft,
    Full,
}

/// <summary>
/// The player's graphics choices (feature 018, data-model.md GraphicsSettings): a preset and the value of
/// each effect. Choosing a preset copies its values from <see cref="GraphicsPresets"/>; changing an effect
/// makes the preset <see cref="GraphicsPreset.Custom"/> unless the values match a preset again. The day/night
/// cycle is not part of the presets.
/// </summary>
internal sealed class GraphicsSettings
{
    public GraphicsPreset Preset { get; set; } = GraphicsPreset.Medium;

    public bool Shadows { get; set; } = true;

    public QualityLevel ShadowQuality { get; set; } = QualityLevel.Medium;

    public AntiAliasingMode AntiAliasing { get; set; } = AntiAliasingMode.Msaa4;

    public bool WaterEffects { get; set; } = true;

    public SceneryDensityLevel SceneryDensity { get; set; } = SceneryDensityLevel.Medium;

    public QualityLevel ModelDetail { get; set; } = QualityLevel.Medium;

    public AmbientOcclusionLevel AmbientOcclusion { get; set; } = AmbientOcclusionLevel.Full;

    public bool DayNight { get; set; }

    /// <summary>Settings of a preset (Medium for <see cref="GraphicsPreset.Custom"/>).</summary>
    public static GraphicsSettings For(GraphicsPreset preset)
    {
        var settings = new GraphicsSettings();
        settings.ApplyPreset(preset == GraphicsPreset.Custom ? GraphicsPreset.Medium : preset);
        return settings;
    }

    /// <summary>Copies the effect values of <paramref name="preset"/> (not the day/night choice).</summary>
    public void ApplyPreset(GraphicsPreset preset)
    {
        if (preset == GraphicsPreset.Custom)
        {
            Preset = GraphicsPreset.Custom;
            return;
        }

        var values = GraphicsPresets.Default.Values(preset);
        Shadows = values.Shadows;
        ShadowQuality = values.ShadowQuality;
        AntiAliasing = values.AntiAliasing;
        WaterEffects = values.WaterEffects;
        SceneryDensity = values.SceneryDensity;
        ModelDetail = values.ModelDetail;
        AmbientOcclusion = values.AmbientOcclusion;
        Preset = preset;
    }

    /// <summary>Sets <see cref="Preset"/> to the preset whose values match, or to Custom.</summary>
    public void UpdatePreset()
    {
        foreach (var preset in new[] { GraphicsPreset.Low, GraphicsPreset.Medium, GraphicsPreset.High })
        {
            if (SameEffects(GraphicsPresets.Default.Values(preset)))
            {
                Preset = preset;
                return;
            }
        }

        Preset = GraphicsPreset.Custom;
    }

    public GraphicsSettings Clone() => (GraphicsSettings)MemberwiseClone();

    /// <summary>Whether every effect (not the preset name or day/night) equals the other's.</summary>
    public bool SameEffects(GraphicsSettings other) =>
        other.Shadows == Shadows && other.ShadowQuality == ShadowQuality && other.AntiAliasing == AntiAliasing
        && other.WaterEffects == WaterEffects && other.SceneryDensity == SceneryDensity && other.ModelDetail == ModelDetail
        && other.AmbientOcclusion == AmbientOcclusion;

    public bool SameAs(GraphicsSettings other) => SameEffects(other) && other.Preset == Preset && other.DayNight == DayNight;

    /// <summary>
    /// Reads the <c>graphics</c> object of the settings file property by property (contracts/graphics-settings.md);
    /// invalid values keep their defaults and are reported in <paramref name="problems"/>.
    /// </summary>
    public static GraphicsSettings Read(JsonElement value, List<string> problems)
    {
        ArgumentNullException.ThrowIfNull(problems);
        var settings = new GraphicsSettings();
        if (value.ValueKind != JsonValueKind.Object)
        {
            problems.Add("ignored invalid value for 'graphics'");
            return For(GraphicsPreset.Medium);
        }

        foreach (var property in value.EnumerateObject())
        {
            var ok = property.Name.ToLowerInvariant() switch
            {
                "preset" => TryEnum<GraphicsPreset>(property.Value, v => settings.Preset = v),
                "daynight" => TryBool(property.Value, v => settings.DayNight = v),
                _ => settings.TryReadEffect(property.Name, property.Value),
            };
            if (!ok)
            {
                problems.Add($"ignored invalid value for 'graphics.{property.Name}'");
            }
        }

        // A named preset always uses the current preset table, so balance changes reach the player.
        if (settings.Preset != GraphicsPreset.Custom)
        {
            settings.ApplyPreset(settings.Preset);
        }

        return settings;
    }

    /// <summary>Reads one effect value by its JSON name; false when the name or the value is invalid.</summary>
    internal bool TryReadEffect(string name, JsonElement value) => name.ToLowerInvariant() switch
    {
        "shadows" => TryBool(value, v => Shadows = v),
        "shadowquality" => TryEnum<QualityLevel>(value, v => ShadowQuality = v),
        "antialiasing" => TryEnum<AntiAliasingMode>(value, v => AntiAliasing = v),
        "watereffects" => TryBool(value, v => WaterEffects = v),
        "scenerydensity" => TryEnum<SceneryDensityLevel>(value, v => SceneryDensity = v),
        "modeldetail" => TryEnum<QualityLevel>(value, v => ModelDetail = v),
        "ambientocclusion" => TryEnum<AmbientOcclusionLevel>(value, v => AmbientOcclusion = v),
        _ => false,
    };

    private static bool TryEnum<T>(JsonElement value, Action<T> set)
        where T : struct, Enum
    {
        if (value.ValueKind == JsonValueKind.String && Enum.TryParse<T>(value.GetString(), ignoreCase: true, out var parsed)
            && Enum.IsDefined(parsed) && !int.TryParse(value.GetString(), out _))
        {
            set(parsed);
            return true;
        }

        return false;
    }

    private static bool TryBool(JsonElement value, Action<bool> set)
    {
        if (value.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            set(value.GetBoolean());
            return true;
        }

        return false;
    }
}

/// <summary>Numbers behind the current graphics settings, read by the renderer every frame.</summary>
internal readonly record struct GraphicsParameters(
    int ShadowCascades,
    int ShadowMapSize,
    float ShadowDistance,
    int ShadowTaps,
    int SampleCount,
    bool WaterEffects,
    float SceneryDensity,
    float ModelLevel1Distance,
    float ModelLevel2Distance,
    float ModelLevel3Distance,
    float AmbientOcclusion,
    bool DayNight);

/// <summary>
/// The preset table (<c>Rendering/Data/graphics-presets.json</c>, data-model.md Graphics presets), validated at
/// load: effect values of each preset and the numbers behind each quality level.
/// </summary>
internal sealed class GraphicsPresets
{
    public const string FileName = "graphics-presets.json";

    private static readonly Lazy<GraphicsPresets> Lazy = new(() => Parse(LoadText()));

    private readonly Dictionary<GraphicsPreset, GraphicsSettings> _presets;
    private readonly Dictionary<QualityLevel, (int Cascades, int Size, float Distance, int Taps)> _shadows;
    private readonly Dictionary<SceneryDensityLevel, float> _density;
    private readonly Dictionary<QualityLevel, (float Level1, float Level2, float Level3)> _detail;
    private readonly Dictionary<AmbientOcclusionLevel, float> _ambient;

    private GraphicsPresets(
        Dictionary<GraphicsPreset, GraphicsSettings> presets,
        Dictionary<QualityLevel, (int, int, float, int)> shadows,
        Dictionary<SceneryDensityLevel, float> density,
        Dictionary<QualityLevel, (float, float, float)> detail,
        Dictionary<AmbientOcclusionLevel, float> ambient)
    {
        _presets = presets;
        _shadows = shadows;
        _density = density;
        _detail = detail;
        _ambient = ambient;
    }

    /// <summary>The shipped table.</summary>
    public static GraphicsPresets Default => Lazy.Value;

    public GraphicsSettings Values(GraphicsPreset preset) => _presets[preset];

    /// <summary>Resolves the numbers for <paramref name="settings"/>.</summary>
    public GraphicsParameters Resolve(GraphicsSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var shadow = _shadows[settings.ShadowQuality];
        var detail = _detail[settings.ModelDetail];
        return new GraphicsParameters(
            settings.Shadows ? shadow.Cascades : 0,
            shadow.Size,
            shadow.Distance,
            shadow.Taps,
            settings.AntiAliasing == AntiAliasingMode.Msaa4 ? 4 : 1,
            settings.WaterEffects,
            _density[settings.SceneryDensity],
            detail.Level1,
            detail.Level2,
            detail.Level3,
            _ambient[settings.AmbientOcclusion],
            settings.DayNight);
    }

    /// <summary>Parses and validates a preset table (tests use their own text).</summary>
    public static GraphicsPresets Parse(string json)
    {
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        var root = document.RootElement;

        var presets = new Dictionary<GraphicsPreset, GraphicsSettings>();
        var presetsElement = Section(root, "presets");
        foreach (var preset in new[] { GraphicsPreset.Low, GraphicsPreset.Medium, GraphicsPreset.High })
        {
            var element = Section(presetsElement, Name(preset));
            var settings = new GraphicsSettings();
            foreach (var property in element.EnumerateObject())
            {
                if (!settings.TryReadEffect(property.Name, property.Value))
                {
                    throw DataFiles.Invalid(FileName, $"preset '{Name(preset)}' has an invalid value for '{property.Name}'.");
                }
            }

            settings.Preset = preset;
            presets[preset] = settings;
        }

        var shadows = new Dictionary<QualityLevel, (int, int, float, int)>();
        var shadowElement = Section(root, "shadowQuality");
        foreach (var level in Enum.GetValues<QualityLevel>())
        {
            var e = Section(shadowElement, Name(level));
            var cascades = Int(e, "cascades", 1, 3, $"shadowQuality.{Name(level)}");
            var size = Int(e, "size", 512, 4096, $"shadowQuality.{Name(level)}");
            if ((size & (size - 1)) != 0)
            {
                throw DataFiles.Invalid(FileName, $"shadowQuality.{Name(level)}.size must be a power of two.");
            }

            var distance = Int(e, "distance", 32, 4000, $"shadowQuality.{Name(level)}");
            var taps = Int(e, "taps", 1, 9, $"shadowQuality.{Name(level)}");
            if (taps is not (1 or 4 or 9))
            {
                throw DataFiles.Invalid(FileName, $"shadowQuality.{Name(level)}.taps must be 1, 4 or 9.");
            }

            shadows[level] = (cascades, size, distance, taps);
        }

        var density = new Dictionary<SceneryDensityLevel, float>();
        var densityElement = Section(root, "sceneryDensity");
        foreach (var level in Enum.GetValues<SceneryDensityLevel>())
        {
            density[level] = Fraction(densityElement, Name(level), "sceneryDensity");
        }

        var detail = new Dictionary<QualityLevel, (float, float, float)>();
        var detailElement = Section(root, "modelDetail");
        foreach (var level in Enum.GetValues<QualityLevel>())
        {
            if (!detailElement.TryGetProperty(Name(level), out var list) || list.ValueKind != JsonValueKind.Array || list.GetArrayLength() != 3
                || !list[0].TryGetSingle(out var level1) || !list[1].TryGetSingle(out var level2) || !list[2].TryGetSingle(out var level3)
                || level1 <= 0 || level2 <= level1 || level3 <= level2)
            {
                throw DataFiles.Invalid(FileName, $"modelDetail.{Name(level)} must be three increasing distances.");
            }

            detail[level] = (level1, level2, level3);
        }

        var ambient = new Dictionary<AmbientOcclusionLevel, float>();
        var ambientElement = Section(root, "ambientOcclusion");
        foreach (var level in Enum.GetValues<AmbientOcclusionLevel>())
        {
            ambient[level] = Fraction(ambientElement, Name(level), "ambientOcclusion");
        }

        return new GraphicsPresets(presets, shadows, density, detail, ambient);
    }

    private static string LoadText()
    {
        using var stream = DataFiles.Open("data/" + FileName);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static string Name<T>(T value)
        where T : struct, Enum
    {
        var text = value.ToString();
        return char.ToLowerInvariant(text[0]) + text[1..];
    }

    private static JsonElement Section(JsonElement parent, string name) =>
        parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Object
            ? value
            : throw DataFiles.Invalid(FileName, $"missing section '{name}'.");

    private static int Int(JsonElement parent, string name, int min, int max, string where) =>
        parent.TryGetProperty(name, out var value) && value.TryGetInt32(out var number) && number >= min && number <= max
            ? number
            : throw DataFiles.Invalid(FileName, $"{where}.{name} must be a whole number from {min} to {max}.");

    private static float Fraction(JsonElement parent, string name, string where) =>
        parent.TryGetProperty(name, out var value) && value.TryGetSingle(out var number) && number is >= 0 and <= 1
            ? number
            : throw DataFiles.Invalid(FileName, $"{where}.{name} must be a number from 0 to 1.");
}

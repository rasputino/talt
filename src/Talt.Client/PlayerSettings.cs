using System.Text.Json;
using System.Text.Json.Serialization;

namespace Talt.Client;

/// <summary>
/// Player preferences persisted between sessions (data-model.md PlayerSettings, FR-026). Not
/// simulation state and not part of recorded sessions. Each property is read on its own, so one
/// invalid value falls back to its default without losing the others; the file is saved after every
/// change (settings apply immediately) by writing a temporary file and moving it over the old one.
/// </summary>
internal sealed class PlayerSettings
{
    public const int CurrentVersion = 2;
    public const int MinimumWindowWidth = 800;
    public const int MinimumWindowHeight = 600;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Format version of the file (1 = feature 001, only the fast-forward flag).</summary>
    public int Version { get; set; } = CurrentVersion;

    /// <summary>"en" or "es"; null until chosen (the system language is used).</summary>
    public string? Language { get; set; }

    /// <summary>ISO currency code; null until chosen (the language default is used).</summary>
    public string? Currency { get; set; }

    /// <summary>Interface scale 0.75–2 in steps of 0.25; null until chosen (a suggestion is used).</summary>
    public float? UiScale { get; set; }

    public bool Fullscreen { get; set; }

    public int WindowWidth { get; set; } = 1600;

    public int WindowHeight { get; set; } = 900;

    public bool VSync { get; set; } = true;

    /// <summary>Unlocks the separate fast-forward control (false by default, feature 001).</summary>
    public bool FastForwardUnlocked { get; set; }

    /// <summary>Key binding overrides: action id → key names per slot ("" = empty slot).</summary>
    public Dictionary<string, string[]> KeyBindings { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Problems found while loading (shown once to the player).</summary>
    [JsonIgnore]
    public List<string> LoadProblems { get; } = [];

    /// <summary>Default location: the user's application data folder.</summary>
    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TALT", "settings.json");

    /// <summary>Whether a scale is one of the allowed steps.</summary>
    public static bool IsValidScale(float scale) => Ui.UiContext.Scales.Contains(scale);

    /// <summary>Loads settings; a missing file gives the defaults, invalid values are skipped one by one.</summary>
    public static PlayerSettings Load(string path)
    {
        var settings = new PlayerSettings();
        string text;
        try
        {
            if (!File.Exists(path))
            {
                return settings;
            }

            text = File.ReadAllText(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            settings.LoadProblems.Add($"could not read {path}: {exception.Message}");
            return settings;
        }

        settings.ReadFrom(text);
        foreach (var problem in settings.LoadProblems)
        {
            Console.Error.WriteLine($"Settings: {problem}. Using the default for it.");
        }

        return settings;
    }

    /// <summary>Reads values from JSON text (testable without files).</summary>
    internal void ReadFrom(string json)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        }
        catch (JsonException exception)
        {
            LoadProblems.Add($"the file is not valid JSON ({exception.Message})");
            return;
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                LoadProblems.Add("the file does not contain an object");
                return;
            }

            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (!TryRead(property))
                {
                    LoadProblems.Add($"ignored invalid value for '{property.Name}'");
                }
            }
        }

        Version = CurrentVersion; // Older files are upgraded on the next save.
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(this, JsonOptions));
        File.Move(temporary, path, overwrite: true);
    }

    private bool TryRead(JsonProperty property)
    {
        var value = property.Value;
        switch (property.Name.ToLowerInvariant())
        {
            case "version":
                return value.TryGetInt32(out _);
            case "language":
                if (value.ValueKind == JsonValueKind.Null)
                {
                    return true;
                }

                if (value.ValueKind == JsonValueKind.String && Localization.Strings.SupportedLanguages.Contains(value.GetString()))
                {
                    Language = value.GetString();
                    return true;
                }

                return false;
            case "currency":
                if (value.ValueKind == JsonValueKind.String && value.GetString() is { Length: 3 } code)
                {
                    Currency = code; // Checked against the currency list when used.
                    return true;
                }

                return value.ValueKind == JsonValueKind.Null;
            case "uiscale":
                if (value.ValueKind == JsonValueKind.Number && value.TryGetSingle(out var scale) && IsValidScale(scale))
                {
                    UiScale = scale;
                    return true;
                }

                return value.ValueKind == JsonValueKind.Null;
            case "fullscreen":
                return TryBool(value, b => Fullscreen = b);
            case "vsync":
                return TryBool(value, b => VSync = b);
            case "fastforwardunlocked":
                return TryBool(value, b => FastForwardUnlocked = b);
            case "windowwidth":
                if (value.TryGetInt32(out var width) && width >= MinimumWindowWidth)
                {
                    WindowWidth = width;
                    return true;
                }

                return false;
            case "windowheight":
                if (value.TryGetInt32(out var height) && height >= MinimumWindowHeight)
                {
                    WindowHeight = height;
                    return true;
                }

                return false;
            case "keybindings":
                return TryReadBindings(value);
            default:
                return false; // Unknown property: reported and dropped on the next save.
        }
    }

    private bool TryReadBindings(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        foreach (var action in value.EnumerateObject())
        {
            if (action.Value.ValueKind != JsonValueKind.Array)
            {
                LoadProblems.Add($"ignored binding '{action.Name}'");
                continue;
            }

            KeyBindings[action.Name] = action.Value.EnumerateArray()
                .Select(k => k.ValueKind == JsonValueKind.String ? k.GetString() ?? string.Empty : string.Empty)
                .ToArray();
        }

        return true;
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

using System.Text.Json;

namespace Talt.Client;

/// <summary>
/// Player preferences persisted between sessions (data-model.md PlayerSettings.FastForwardUnlocked).
/// Not simulation state and not part of recorded sessions. Feature 005 turns this into the full
/// settings system with a settings screen.
/// </summary>
internal sealed class PlayerSettings
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>Unlocks the separate fast-forward control (false by default).</summary>
    public bool FastForwardUnlocked { get; set; }

    /// <summary>Default location: the user's application data folder.</summary>
    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TALT", "settings.json");

    /// <summary>Loads settings; a missing or unreadable file gives the defaults.</summary>
    public static PlayerSettings Load(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                return JsonSerializer.Deserialize<PlayerSettings>(File.ReadAllText(path)) ?? new PlayerSettings();
            }
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Could not read settings from {path}: {exception.Message}. Using defaults.");
        }

        return new PlayerSettings();
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions));
    }
}

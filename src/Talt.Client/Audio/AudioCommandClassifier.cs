namespace Talt.Client.Audio;

/// <summary>
/// Command type names that should use the construction cue. Feature 006 adds the real construction
/// commands; until then this also covers the terrain and demolition actions that exist. The check is
/// case-insensitive and allocation-free, and AddVehicle/RemoveVehicle do not match.
/// </summary>
internal static class AudioCommandClassifier
{
    private static readonly string[] ConstructionMarkers =
    [
        "build",
        "construct",
        "terraform",
        "road",
        "track",
        "rail",
        "station",
        "demolish",
        "bulldoze",
        "clear",
        "land",
    ];

    public static bool IsConstruction(string typeName)
    {
        ArgumentNullException.ThrowIfNull(typeName);
        foreach (var marker in ConstructionMarkers)
        {
            if (typeName.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}

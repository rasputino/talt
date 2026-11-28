using System.Text.Json;
using Talt.Core.Simulation;

namespace Talt.Core.Saves;

/// <summary>What the caller supplies for a save's details: the name, the kind and the real time (Core never reads the clock).</summary>
/// <param name="Name">The player's name for the save (data-model.md SaveDetails.Name: 1–80 characters after trimming).</param>
/// <param name="Kind">Manual, autosave or quick save (data-model.md SaveDetails.Kind).</param>
/// <param name="SavedAtUtc">Real time of the save, from the client's clock.</param>
/// <param name="GameVersion">Informational version of the build.</param>
/// <param name="IncludeFingerprint">Also compute the state fingerprint (headless tool and tests; costs a hash of the whole state).</param>
public readonly record struct SaveDetailsInput(string Name, SaveKind Kind, DateTimeOffset SavedAtUtc, string GameVersion, bool IncludeFingerprint = false)
{
    /// <summary>Longest save name (data-model.md SaveDetails.Name).</summary>
    public const int MaxNameLength = 80;

    /// <summary>Throws <see cref="ArgumentException"/> when the name is empty or too long.</summary>
    public void Validate()
    {
        var trimmed = Name?.Trim() ?? string.Empty;
        if (trimmed.Length == 0 || trimmed.Length > MaxNameLength)
        {
            throw new ArgumentException($"A save name has 1 to {MaxNameLength} characters.", nameof(Name));
        }
    }
}

/// <summary>
/// The details block of a save (data-model.md SaveDetails): what the load screen shows without loading the game. Stored
/// as camelCase JSON; unknown properties are ignored so later versions can add fields (contracts/save-format.md).
/// </summary>
public sealed record SaveDetails
{
    public string Name { get; set; } = string.Empty;

    public SaveKind Kind { get; set; }

    public DateTimeOffset SavedAtUtc { get; set; }

    public int Year { get; set; }

    public int Month { get; set; }

    public int Day { get; set; }

    /// <summary>Simulation tick at the save (data-model.md SaveDetails.Tick, never negative).</summary>
    public long Tick { get; set; }

    /// <summary>Company name; empty while companies have no names (the client shows "your company").</summary>
    public string CompanyName { get; set; } = string.Empty;

    /// <summary>Player cash (data-model.md SaveDetails.Money, may be negative).</summary>
    public long Money { get; set; }

    /// <summary>Map width in tiles, 0 for map-less scenarios (data-model.md SaveDetails.MapWidth).</summary>
    public int MapWidth { get; set; }

    /// <summary>Map depth in tiles, 0 for map-less scenarios (data-model.md SaveDetails.MapDepth).</summary>
    public int MapDepth { get; set; }

    public string GameVersion { get; set; } = string.Empty;

    public int FormatVersion { get; set; } = SaveFile.ContainerVersion;

    /// <summary>Content fingerprint, for the "data changed" hint on the load screen.</summary>
    public ulong ContentFingerprint { get; set; }

    /// <summary>State fingerprint at the save tick, or 0 when not computed.</summary>
    public ulong Fingerprint { get; set; }

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    public byte[] ToJson() => JsonSerializer.SerializeToUtf8Bytes(this, Options);

    /// <summary>Parses the details; throws <see cref="SaveFormatException"/> when the JSON is malformed or invalid.</summary>
    public static SaveDetails FromJson(ReadOnlySpan<byte> json)
    {
        SaveDetails? details;
        try
        {
            details = JsonSerializer.Deserialize<SaveDetails>(json, Options);
        }
        catch (JsonException e)
        {
            throw new SaveFormatException("The save details cannot be read.", e);
        }

        if (details is null || details.Tick < 0 || details.MapWidth < 0 || details.MapDepth < 0 || string.IsNullOrWhiteSpace(details.Name) || details.CompanyName is null || details.Name.Length > SaveDetailsInput.MaxNameLength)
        {
            throw new SaveFormatException("The save details are invalid.");
        }

        return details;
    }

    /// <summary>Details of <paramref name="simulation"/> as it is now.</summary>
    internal static SaveDetails Of(GameSimulation simulation, SaveDetailsInput input, SaveDetails? reuse)
    {
        var date = simulation.Date;
        var map = simulation.Construction?.Map;
        var money = simulation.Construction?.Player.Balance ?? 0;
        var details = reuse ?? new SaveDetails();
        details.Name = input.Name.Trim();
        details.Kind = input.Kind;
        details.SavedAtUtc = input.SavedAtUtc;
        details.Year = date.Year;
        details.Month = date.Month;
        details.Day = date.Day;
        details.Tick = simulation.CurrentTick;
        details.Money = money;
        details.MapWidth = map?.Width ?? 0;
        details.MapDepth = map?.Depth ?? 0;
        details.GameVersion = input.GameVersion;
        details.ContentFingerprint = simulation.Content.Fingerprint();
        details.Fingerprint = input.IncludeFingerprint ? simulation.ComputeFingerprint() : 0;
        return details;
    }
}

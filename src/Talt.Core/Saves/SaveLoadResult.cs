using Talt.Core.Construction;
using Talt.Core.Content;
using Talt.Core.Simulation;

namespace Talt.Core.Saves;

/// <summary>Outcome of <see cref="SaveGame.Load(string, SaveLoadOptions?, IProgress{float}?)"/> (data-model.md SaveLoadResult.Status).</summary>
public enum SaveLoadStatus
{
    /// <summary>Loaded; it continues exactly like the game that was saved.</summary>
    Loaded = 0,

    /// <summary>Loaded, but the installed game data differs from the save's: the game may play differently (FR-018).</summary>
    LoadedWithChangedData = 1,

    /// <summary>The file is damaged or truncated (FR-019).</summary>
    Damaged = 2,

    /// <summary>Written by a newer version of the game (FR-017).</summary>
    TooNew = 3,

    /// <summary>The save refers to game data (cargo, industries, models) that is not installed (FR-018).</summary>
    MissingContent = 4,

    /// <summary>The file is not a save.</summary>
    NotASave = 5,

    /// <summary>The file could not be opened or read.</summary>
    IoError = 6,
}

/// <summary>Options for loading: the game data to load into (null: the installed data files).</summary>
public sealed record SaveLoadOptions(GameContent? Content = null, ConstructionRules? Rules = null)
{
    /// <summary>Ids of sections the caller owns (client <c>CAMR</c>): returned untouched in <see cref="SaveLoadResult.ExtraSections"/>.</summary>
    public IReadOnlyList<SaveSectionId> ExtraSectionIds { get; init; } = [];

    /// <summary>Additional sections read after the built-in ones (tests of the migration chain, research R11).</summary>
    internal IReadOnlyList<SaveSection> AdditionalSections { get; init; } = [];
}

/// <summary>An opaque section owned by the caller (for example the client's camera).</summary>
public readonly record struct ExtraSection(SaveSectionId Id, int Version, ReadOnlyMemory<byte> Payload);

/// <summary>The result of loading a save (data-model.md SaveLoadResult). Loading never throws for bad files.</summary>
public sealed record SaveLoadResult(SaveLoadStatus Status, GameSimulation? Simulation, SaveDetails? Details, string? Problem)
{
    /// <summary>True when <see cref="Simulation"/> holds the loaded game.</summary>
    public bool Succeeded => Status is SaveLoadStatus.Loaded or SaveLoadStatus.LoadedWithChangedData;

    /// <summary>Content ids the save needs that are not installed (<see cref="SaveLoadStatus.MissingContent"/>).</summary>
    public IReadOnlyList<string> MissingContent { get; init; } = [];

    /// <summary>The caller's opaque sections found in the save.</summary>
    public IReadOnlyList<ExtraSection> ExtraSections { get; init; } = [];

    /// <summary>A failed load with a reason.</summary>
    public static SaveLoadResult Failed(SaveLoadStatus status, string problem) => new(status, null, null, problem);
}

/// <summary>Status of <see cref="SaveGame.Peek"/>.</summary>
public enum SavePeekStatus
{
    Ok = 0,
    Damaged = 1,
    TooNew = 2,
    NotASave = 3,
    IoError = 4,
}

/// <summary>A save's details and thumbnail, read without loading the game (FR-016).</summary>
public sealed record SavePeek(SavePeekStatus Status, SaveDetails? Details, SaveThumbnailData? Thumbnail, string? Problem);

/// <summary>Outcome of writing a save.</summary>
public sealed record SaveWriteResult(bool Succeeded, string Path, long Bytes, string? Problem, Exception? Error = null);

using Talt.Core.Simulation;

namespace Talt.Core.Saves;

/// <summary>
/// One save section: its id, the version this build writes, whether the game has it, and how it is written and read
/// (data-model.md ISaveSection). <paramref name="Read"/> receives the stored version and must migrate older layouts
/// (research R11).
/// </summary>
internal sealed record SaveSection(
    SaveSectionId Id,
    int Version,
    Func<GameSimulation, bool> IsPresent,
    Action<GameSimulation, SaveWriter> Write,
    Action<GameSimulation, SaveReader, int> Read)
{
    public SaveSection(string id, int version, Func<GameSimulation, bool> isPresent, Action<GameSimulation, SaveWriter> write, Action<GameSimulation, SaveReader, int> read)
        : this(SaveSectionId.Of(id), version, isPresent, write, read)
    {
    }
}

/// <summary>
/// The registry of save sections in restore order (data-model.md "Sections in version 1", contracts/save-sections.md).
/// <c>CORE</c> and <c>MAP_</c> are read before the game is constructed (they hold the settings and the map the empty
/// game is built on), the others afterwards, in the order of this list, which mirrors the wiring order of
/// <see cref="GameSimulation"/>.
/// </summary>
internal static class SaveSections
{
    public static readonly SaveSectionId Core = SaveSectionId.Of("CORE");
    public static readonly SaveSectionId Map = SaveSectionId.Of("MAP_");
    public static readonly SaveSectionId Content = SaveSectionId.Of("CNTN");
    public static readonly SaveSectionId Camera = SaveSectionId.Of("CAMR");

    /// <summary>Current version of <c>CORE</c>.</summary>
    public const int CoreVersion = 1;

    /// <summary>Current version of <c>MAP_</c>.</summary>
    public const int MapVersion = 1;

    /// <summary>Current version of <c>CNTN</c>.</summary>
    public const int ContentVersion = 1;

    /// <summary>Sections read after the game is constructed, in restore order (after <c>CORE</c>'s state and <c>CNTN</c>).</summary>
    public static IReadOnlyList<SaveSection> Systems { get; } = SaveSystemSections.Create();

    /// <summary>Every id this build knows, with the version it writes.</summary>
    public static int? KnownVersion(SaveSectionId id)
    {
        if (id == Core)
        {
            return CoreVersion;
        }

        if (id == Map)
        {
            return MapVersion;
        }

        if (id == Content)
        {
            return ContentVersion;
        }

        foreach (var section in Systems)
        {
            if (section.Id == id)
            {
                return section.Version;
            }
        }

        return null;
    }
}

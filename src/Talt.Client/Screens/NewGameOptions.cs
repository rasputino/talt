using Talt.Core.Voxels;

namespace Talt.Client.Screens;

/// <summary>How an option is edited on the new-game screen.</summary>
internal enum NewGameOptionKind
{
    /// <summary>One of a list of values (drop-down).</summary>
    Choice,

    /// <summary>A whole number typed by the player (map seed).</summary>
    Seed,
}

/// <summary>One value of a <see cref="NewGameOptionKind.Choice"/> option and its label.</summary>
/// <param name="Value">The option value.</param>
/// <param name="LabelKey">Localization key of the label; <c>{value}</c> receives the value.</param>
internal sealed record NewGameChoice(int Value, string LabelKey);

/// <summary>
/// One row of the new-game screen (data-model.md NewGameOptionDescriptor). The screen is built only
/// from these descriptors, so feature 004's map generation options can be added without changing it.
/// </summary>
internal sealed record NewGameOptionDescriptor(
    string Id,
    string LabelKey,
    NewGameOptionKind Kind,
    IReadOnlyList<NewGameChoice> Choices,
    int Default,
    string? TooltipKey = null,
    bool GroupDigits = false)
{
    /// <summary>Whether <paramref name="value"/> is allowed for this option.</summary>
    public bool Accepts(int value) => Kind == NewGameOptionKind.Seed ? value >= 0 : Choices.Any(c => c.Value == value);
}

/// <summary>Supplies the options of the new-game screen and turns the chosen values into a game.</summary>
internal interface INewGameOptionsSource
{
    IReadOnlyList<NewGameOptionDescriptor> Options { get; }

    /// <summary>Builds the game options from the values chosen (option id → value).</summary>
    NewGameOptions Create(IReadOnlyDictionary<string, int> values);
}

/// <summary>
/// The options this branch supports (what <see cref="Talt.Core.Scenarios.ScenarioSettings"/> takes):
/// map size, vehicles, start year and seed. Follow-up after feature 004: a source bound to its map
/// generation settings record (roughness, maximum height, water, sea level).
/// </summary>
internal sealed class DefaultNewGameOptionsSource : INewGameOptionsSource
{
    public const string MapSize = "mapSize";
    public const string Vehicles = "vehicles";
    public const string StartYear = "startYear";
    public const string Seed = "seed";

    public IReadOnlyList<NewGameOptionDescriptor> Options { get; } =
    [
        new(MapSize, "newGame.mapSize", NewGameOptionKind.Choice,
            [new(256, "newGame.mapSize.value"), new(512, "newGame.mapSize.value"), new(1024, "newGame.mapSize.value"),
             new(2048, "newGame.mapSize.value"), new(4096, "newGame.mapSize.value")],
            512, "newGame.mapSize.tooltip"),
        new(Vehicles, "newGame.vehicles", NewGameOptionKind.Choice,
            [new(0, "newGame.vehicles.none"), new(1000, "newGame.vehicles.value"), new(5000, "newGame.vehicles.value"),
             new(20000, "newGame.vehicles.value"), new(50000, "newGame.vehicles.value")],
            20000, "newGame.vehicles.tooltip", GroupDigits: true),
        new(StartYear, "newGame.startYear", NewGameOptionKind.Choice,
            [new(1920, "newGame.year.value"), new(1950, "newGame.year.value"), new(1980, "newGame.year.value"), new(2010, "newGame.year.value")],
            1950),
        new(Seed, "newGame.seed", NewGameOptionKind.Seed, [], 2026, "newGame.seed.tooltip"),
    ];

    public NewGameOptions Create(IReadOnlyDictionary<string, int> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        int Value(string id)
        {
            var option = Options.First(o => o.Id == id);
            return values.TryGetValue(id, out var v) && option.Accepts(v) ? v : option.Default;
        }

        return new NewGameOptions(
            ChunksPerSide: Value(MapSize) / Chunk.Size,
            VehicleCount: Value(Vehicles),
            Seed: Value(Seed),
            StartYear: Value(StartYear));
    }
}

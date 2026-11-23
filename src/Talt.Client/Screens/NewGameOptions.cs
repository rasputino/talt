using Talt.Core.Maps;
using Talt.Core.Voxels;

namespace Talt.Client.Screens;

/// <summary>How an option is edited on the new-game screen.</summary>
internal enum NewGameOptionKind
{
    /// <summary>One of a list of values (drop-down).</summary>
    Choice,

    /// <summary>A whole number typed by the player (map seed).</summary>
    Seed,

    /// <summary>A number in a range, with a slider.</summary>
    Slider,
}

/// <summary>One value of a <see cref="NewGameOptionKind.Choice"/> option and its label.</summary>
/// <param name="Value">The option value.</param>
/// <param name="LabelKey">Localization key of the label; <c>{value}</c> receives the value.</param>
internal sealed record NewGameChoice(int Value, string LabelKey);

/// <summary>
/// One row of the new-game screen (data-model.md NewGameOptionDescriptor). The screen is built only
/// from these descriptors, so new map options can be added without changing it.
/// </summary>
internal sealed record NewGameOptionDescriptor(
    string Id,
    string LabelKey,
    NewGameOptionKind Kind,
    IReadOnlyList<NewGameChoice> Choices,
    int Default,
    string? TooltipKey = null,
    bool GroupDigits = false,
    int Minimum = 0,
    int Maximum = 0,
    int Step = 1)
{
    /// <summary>Whether <paramref name="value"/> is allowed for this option.</summary>
    public bool Accepts(int value) => Kind switch
    {
        NewGameOptionKind.Seed => value >= 0,
        NewGameOptionKind.Slider => value >= Minimum && value <= Maximum,
        _ => Choices.Any(c => c.Value == value),
    };
}

/// <summary>Supplies the options of the new-game screen and turns the chosen values into a game.</summary>
internal interface INewGameOptionsSource
{
    IReadOnlyList<NewGameOptionDescriptor> Options { get; }

    /// <summary>Adjusts values that depend on each other (called after every change).</summary>
    void Normalize(IDictionary<string, int> values);

    /// <summary>Builds the game options from the values chosen (option id → value).</summary>
    NewGameOptions Create(IReadOnlyDictionary<string, int> values);
}

/// <summary>
/// The new-game options: the map generation settings of feature 004 (<see cref="MapSettings"/>: width,
/// depth, seed, roughness, maximum height, water, sea level) plus vehicles and start year
/// (<see cref="Talt.Core.Scenarios.ScenarioSettings"/>). Defaults are <see cref="MapSettings"/>'s,
/// with a 512-tile map so a first game starts quickly.
/// </summary>
internal sealed class DefaultNewGameOptionsSource : INewGameOptionsSource
{
    public const string Width = "width";
    public const string Depth = "depth";
    public const string Seed = "seed";
    public const string Roughness = "roughness";
    public const string MaxHeight = "maxHeight";
    public const string Water = "water";
    public const string SeaLevel = "seaLevel";
    public const string Vehicles = "vehicles";
    public const string StartYear = "startYear";

    private static readonly MapSettings Defaults = new(Width: 512, Depth: 512, Seed: 2026);

    private static readonly NewGameChoice[] Sizes =
    [
        new(256, "newGame.side.value"), new(512, "newGame.side.value"), new(1024, "newGame.side.value"),
        new(2048, "newGame.side.value"), new(4096, "newGame.side.value"),
    ];

    public IReadOnlyList<NewGameOptionDescriptor> Options { get; } =
    [
        new(Width, "newGame.width", NewGameOptionKind.Choice, Sizes, Defaults.Width, "newGame.mapSize.tooltip"),
        new(Depth, "newGame.depth", NewGameOptionKind.Choice, Sizes, Defaults.Depth, "newGame.mapSize.tooltip"),
        new(Roughness, "newGame.roughness", NewGameOptionKind.Slider, [], Defaults.Roughness, "newGame.roughness.tooltip", Minimum: 0, Maximum: 100, Step: 5),
        new(MaxHeight, "newGame.maxHeight", NewGameOptionKind.Slider, [], Defaults.MaxHeight, "newGame.maxHeight.tooltip",
            Minimum: MapSettings.MinMaxHeight, Maximum: MapSettings.MaxMaxHeight, Step: 1),
        new(Water, "newGame.water", NewGameOptionKind.Slider, [], Defaults.WaterAmount, "newGame.water.tooltip", Minimum: 0, Maximum: MapSettings.MaxWaterAmount, Step: 5),
        new(SeaLevel, "newGame.seaLevel", NewGameOptionKind.Slider, [], Defaults.SeaLevel, "newGame.seaLevel.tooltip",
            Minimum: MapSettings.MinSeaLevel, Maximum: MapSettings.MaxMaxHeight - 2, Step: 1),
        new(Seed, "newGame.seed", NewGameOptionKind.Seed, [], (int)Defaults.Seed, "newGame.seed.tooltip"),
        new(Vehicles, "newGame.vehicles", NewGameOptionKind.Choice,
            [new(0, "newGame.vehicles.none"), new(1000, "newGame.vehicles.value"), new(5000, "newGame.vehicles.value"),
             new(20000, "newGame.vehicles.value"), new(50000, "newGame.vehicles.value")],
            20000, "newGame.vehicles.tooltip", GroupDigits: true),
        new(StartYear, "newGame.startYear", NewGameOptionKind.Choice,
            [new(1920, "newGame.year.value"), new(1950, "newGame.year.value"), new(1980, "newGame.year.value"), new(2010, "newGame.year.value")],
            1950),
    ];

    /// <summary>The sea must stay at least 2 voxels below the highest terrain (<see cref="MapSettings.Validate"/>).</summary>
    public void Normalize(IDictionary<string, int> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.TryGetValue(MaxHeight, out var max) && values.TryGetValue(SeaLevel, out var sea) && sea > max - 2)
        {
            values[SeaLevel] = Math.Max(MapSettings.MinSeaLevel, max - 2);
        }
    }

    public NewGameOptions Create(IReadOnlyDictionary<string, int> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        int Value(string id)
        {
            var option = Options.First(o => o.Id == id);
            return values.TryGetValue(id, out var v) && option.Accepts(v) ? v : option.Default;
        }

        var maxHeight = Value(MaxHeight);
        var map = new MapSettings(
            Width: Value(Width),
            Depth: Value(Depth),
            Seed: Value(Seed),
            Roughness: Value(Roughness),
            MaxHeight: maxHeight,
            WaterAmount: Value(Water),
            SeaLevel: Math.Min(Value(SeaLevel), maxHeight - 2));
        map.Validate();

        // The road network covers the square part of the map (feature 004 keeps ScenarioSettings square).
        return new NewGameOptions(
            ChunksPerSide: Math.Min(map.Width, map.Depth) / Chunk.Size,
            VehicleCount: Value(Vehicles),
            Seed: Value(Seed),
            StartYear: Value(StartYear),
            Map: map);
    }
}

using Talt.Core.Maps;

namespace Talt.Core.Scenarios;

/// <summary>
/// Everything needed to create a game deterministically (data-model.md, ScenarioSettings).
/// All values are integers so the settings themselves are exact on every platform.
/// </summary>
/// <param name="Seed">Drives network and traffic generation.</param>
/// <param name="StartYear">Calendar year of the first in-game day (1 to 9000).</param>
/// <param name="MapSizeTiles">Side of the square map in tiles; multiple of 32, 64 to 16 384.</param>
/// <param name="RoadSpacing">Tiles between road junctions; 2 to half the map size.</param>
/// <param name="RoadRemovalChancePerMille">Chance (0 to 900 per mille) that each grid road is left out.</param>
/// <param name="VehicleCount">Number of vehicles at start (0 or more).</param>
/// <param name="LineCount">Number of lines; null means one line per 10 vehicles (at least 1).</param>
/// <param name="Map">
/// Generated map of the game (feature 004); null for map-less scenarios (benchmarks, the CI goldens).
/// With a map the simulation owns its tile data and can be built on (feature 006, research R1).
/// </param>
/// <param name="Towns">Town options of a game with a map (feature 008); null means the defaults.</param>
/// <param name="Stations">Station options of a game with a map (feature 010); null means the defaults.</param>
public sealed record ScenarioSettings(
    int Seed = 2026,
    int StartYear = 1950,
    int MapSizeTiles = 512,
    int RoadSpacing = 16,
    int RoadRemovalChancePerMille = 150,
    int VehicleCount = 20_000,
    int? LineCount = null,
    MapSettings? Map = null,
    Talt.Core.Towns.TownSettings? Towns = null,
    Talt.Core.Stations.StationSettings? Stations = null)
{
    /// <summary>The station options in effect (<see cref="Stations"/> or the defaults).</summary>
    public Talt.Core.Stations.StationSettings EffectiveStations => Stations ?? Talt.Core.Stations.StationSettings.Default;

    /// <summary>The town options in effect (<see cref="Towns"/> or the defaults).</summary>
    public Talt.Core.Towns.TownSettings EffectiveTowns => Towns ?? Talt.Core.Towns.TownSettings.Default;

    public const int MinStartYear = 1;
    public const int MaxStartYear = 9000;

    /// <summary>Lines actually created: <see cref="LineCount"/> or one per 10 vehicles.</summary>
    public int EffectiveLineCount => LineCount ?? Math.Max(1, VehicleCount / 10);

    /// <summary>Throws <see cref="ArgumentException"/> describing the first invalid value.</summary>
    public void Validate()
    {
        if (StartYear is < MinStartYear or > MaxStartYear)
        {
            throw new ArgumentException($"Start year must be between {MinStartYear} and {MaxStartYear} (was {StartYear}).");
        }

        if (MapSizeTiles is < 64 or > 16_384 || MapSizeTiles % 32 != 0)
        {
            throw new ArgumentException($"Map size must be a multiple of 32 between 64 and 16384 tiles (was {MapSizeTiles}).");
        }

        if (RoadSpacing < 2 || RoadSpacing > MapSizeTiles / 2)
        {
            throw new ArgumentException($"Road spacing must be between 2 and half the map size (was {RoadSpacing}).");
        }

        if (RoadRemovalChancePerMille is < 0 or > 900)
        {
            throw new ArgumentException($"Road removal chance must be between 0 and 900 per mille (was {RoadRemovalChancePerMille}).");
        }

        if (VehicleCount < 0)
        {
            throw new ArgumentException($"Vehicle count cannot be negative (was {VehicleCount}).");
        }

        if (LineCount is < 1)
        {
            throw new ArgumentException($"Line count must be at least 1 when given (was {LineCount}).");
        }

        Towns?.Validate();
        Stations?.Validate();
        if (Map is not null)
        {
            Map.Validate();
            if (MapSizeTiles > Math.Min(Map.Width, Map.Depth))
            {
                throw new ArgumentException($"Map size {MapSizeTiles} must fit inside the {Map.Width}×{Map.Depth} map.");
            }
        }
    }
}

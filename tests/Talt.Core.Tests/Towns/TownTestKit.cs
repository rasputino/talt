using Talt.Core.Construction;
using Talt.Core.Maps;
using Talt.Core.Network;
using Talt.Core.Scenarios;
using Talt.Core.Simulation;
using Talt.Core.Towns;

namespace Talt.Core.Tests.Towns;

/// <summary>Scenarios and invariant checks shared by the town tests (feature 008).</summary>
internal static class TownTestKit
{
    public static ScenarioSettings Scenario(int seed = 8, int size = 512, TownSettings? towns = null, int startYear = 1950) => new(
        Seed: seed,
        StartYear: startYear,
        MapSizeTiles: size,
        VehicleCount: 0,
        Map: new MapSettings(size, size, Seed: seed, Roughness: 45, MaxHeight: 48, WaterAmount: 20, SeaLevel: 6),
        Towns: towns);

    /// <summary>Advances whole months (40 ticks per day).</summary>
    public static void RunMonths(GameSimulation simulation, int months, bool parallel = true)
    {
        var start = simulation.Date;
        var targetMonth = (start.Year * 12) + start.Month - 1 + months;
        while ((simulation.Date.Year * 12) + simulation.Date.Month - 1 < targetMonth)
        {
            simulation.Tick(parallel);
        }
    }

    public static int TotalPopulation(TownRegistry towns)
    {
        var total = 0;
        for (var town = 0; town < towns.Count; town++)
        {
            total += towns.PopulationOf(town);
        }

        return total;
    }

    /// <summary>Returns the first broken town invariant (FR-006, FR-007, FR-008), or null.</summary>
    public static string? CheckInvariants(GameSimulation simulation, bool checkNetwork = true)
    {
        var state = simulation.Construction!;
        var towns = simulation.Towns!;
        var map = state.Map;
        var buildings = towns.Buildings;
        var population = new int[towns.Count];
        var counted = 0;
        for (var slot = 0; slot < buildings.Count; slot++)
        {
            var definition = buildings.TypeDefinitionAt(slot);
            var town = buildings.TownAt(slot);
            population[town] += definition.Residents;
            var min = int.MaxValue;
            var max = int.MinValue;
            for (var dz = 0; dz < definition.Depth; dz++)
            {
                for (var dx = 0; dx < definition.Width; dx++)
                {
                    var x = buildings.XAt(slot) + dx;
                    var z = buildings.ZAt(slot) + dz;
                    if (!map.Contains(x, z))
                    {
                        return $"building {buildings.IdAt(slot)} leaves the map";
                    }

                    var tile = map.Index(x, z);
                    if (state.Objects.KindAt(tile) != TileObjectKind.TownBuilding || state.Objects.OwnerAt(tile) != TileOwner.Town(town))
                    {
                        return $"building {buildings.IdAt(slot)} tile ({x},{z}) is not a town building tile of town {town}";
                    }

                    if (!buildings.TryGetAt(tile, out var id) || id != buildings.IdAt(slot))
                    {
                        return $"tile ({x},{z}) does not map back to building {buildings.IdAt(slot)}";
                    }

                    if (map.IsWater(tile) || state.Roads.HasRoad(tile) || state.IsIndustry(x, z))
                    {
                        return $"building {buildings.IdAt(slot)} stands on water, road or industry at ({x},{z})";
                    }

                    min = Math.Min(min, map.Heights[tile]);
                    max = Math.Max(max, map.Heights[tile]);
                    counted++;
                }
            }

            if (max - min > 1)
            {
                return $"building {buildings.IdAt(slot)} spans a slope of {max - min}";
            }
        }

        var objectTiles = 0;
        for (var tile = 0; tile < map.Width * map.Depth; tile++)
        {
            if (state.Objects.KindAt(tile) == TileObjectKind.TownBuilding)
            {
                objectTiles++;
            }
        }

        if (objectTiles != counted)
        {
            return $"{objectTiles} town building tiles but buildings cover {counted}";
        }

        for (var town = 0; town < towns.Count; town++)
        {
            if (population[town] != towns.PopulationOf(town))
            {
                return $"town {town} population {towns.PopulationOf(town)} but residents sum {population[town]}";
            }
        }

        var roadError = state.Roads.Validate();
        if (roadError is not null)
        {
            return roadError;
        }

        if (checkNetwork)
        {
            var fresh = RoadNetwork.Build(state.Roads);
            fresh.SetTypeSpeeds(state.RoadRules.Types.Select(t => t.SpeedKmh).ToArray());
            fresh.BuildAll();
            var expected = fresh.Canonical();
            var actual = state.Network.Canonical();
            if (!expected.SequenceEqual(actual))
            {
                return $"incremental network ({actual.Count} edges) differs from a fresh build ({expected.Count} edges)";
            }
        }

        return null;
    }
}

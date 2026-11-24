using Talt.Core.Construction;
using Talt.Core.Roads;

namespace Talt.Core.Towns;

/// <summary>Building placement and replacement (research R5, R7; FR-006, FR-011).</summary>
public sealed partial class TownRegistry
{
    // Scratch buffers of the sequential growth phase (no allocation per month).
    private readonly int[] _candidates;
    private bool _founding;

    /// <summary>Runs this month's growth actions of one town (phase 2, sequential).</summary>
    private void GrowMonth(int town)
    {
        var failures = 0;
        var actions = 0;
        while (_growthPoints[town] >= 1000 && actions < MaxActionsPerMonth)
        {
            _growthPoints[town] -= 1000;
            actions++;
            if (TryGrowOnce(town))
            {
                continue;
            }

            failures++;
            if (failures >= Rules.Growth.FailuresBeforeStreet)
            {
                failures = 0;
                if (TryExtendStreet(town))
                {
                    TryGrowOnce(town);
                }
            }
        }
    }

    /// <summary>One growth action: a replacement in larger towns, otherwise a new building on a free plot.</summary>
    private bool TryGrowOnce(int town)
    {
        var growth = Rules.Growth;
        var random = _townRandom[town];
        if (_population[town] >= growth.ReplaceMinPopulation && random.NextInt(100) < growth.ReplaceChancePercent && TryReplace(town))
        {
            return true;
        }

        var radius = RadiusOf(town);
        for (var attempt = 0; attempt < growth.AttemptsPerAction; attempt++)
        {
            var x = _centreX[town] + CentreBiased(random, radius);
            var z = _centreZ[town] + CentreBiased(random, radius);
            if (TryPlace(town, x, z))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>An offset in −r..r that favours small values: the smaller of two uniform draws (research R5).</summary>
    private static int CentreBiased(Determinism.DeterministicRandom random, int radius)
    {
        var a = random.NextInt(-radius, radius + 1);
        var b = random.NextInt(-radius, radius + 1);
        return Math.Abs(a) <= Math.Abs(b) ? a : b;
    }

    /// <summary>
    /// Placement weight of a type in a zone: the data weight, multiplied by the level squared in the centre and by
    /// the level in the inner zone, so larger towns fill their centres with taller buildings (US5).
    /// </summary>
    private static int Weight(BuildingType definition, TownZones zone) => zone switch
    {
        TownZones.Centre => definition.Weight * definition.Level * definition.Level,
        TownZones.Inner => definition.Weight * definition.Level,
        _ => definition.Weight,
    };

    /// <summary>The zone of a tile for a town: centre, inner or outer, by distance as a share of the radius.</summary>
    internal TownZones ZoneAt(int town, int x, int z)
    {
        var dx = (long)(x - _centreX[town]);
        var dz = (long)(z - _centreZ[town]);
        var distance2 = ((dx * dx) + (dz * dz)) * 10_000;
        var radius = (long)RadiusOf(town);
        var centre = Rules.Growth.CentreZonePercent * radius;
        var inner = Rules.Growth.InnerZonePercent * radius;
        return distance2 <= centre * centre ? TownZones.Centre
            : distance2 <= inner * inner ? TownZones.Inner
            : TownZones.Outer;
    }

    /// <summary>Places a building with its origin at (x, z) when a type fits there.</summary>
    private bool TryPlace(int town, int x, int z)
    {
        if (!TileFree(x, z) || Reserved(town, x, z))
        {
            return false;
        }

        var zone = ZoneAt(town, x, z);
        var population = _population[town];
        var count = 0;
        var total = 0;
        var buildings = Rules.Buildings;
        for (var type = 0; type < buildings.Count; type++)
        {
            var definition = buildings[type];
            if ((definition.Zones & zone) == 0 || !definition.InEra(_year) || definition.MinTownPopulation > population
                || !FootprintFits(town, x, z, definition.Width, definition.Depth))
            {
                continue;
            }

            _candidates[count++] = type;
            total += Weight(definition, zone);
        }

        if (count == 0)
        {
            return false;
        }

        var pick = _townRandom[town].NextInt(total);
        var chosen = _candidates[count - 1];
        for (var i = 0; i < count; i++)
        {
            var weight = Weight(buildings[_candidates[i]], zone);
            if (pick < weight)
            {
                chosen = _candidates[i];
                break;
            }

            pick -= weight;
        }

        PlaceBuilding(town, chosen, x, z);
        return true;
    }

    /// <summary>Replaces a smaller building near the centre with a larger one of the same footprint (US5).</summary>
    private bool TryReplace(int town)
    {
        var random = _townRandom[town];
        var radius = Math.Max(1, RadiusOf(town) * Rules.Growth.InnerZonePercent / 100);
        var population = _population[town];
        var buildings = Rules.Buildings;
        for (var attempt = 0; attempt < Rules.Growth.AttemptsPerAction / 2 + 1; attempt++)
        {
            var x = _centreX[town] + CentreBiased(random, radius);
            var z = _centreZ[town] + CentreBiased(random, radius);
            if (!_map.Contains(x, z) || !Buildings.TryGetAt(_map.Index(x, z), out var id))
            {
                continue;
            }

            var slot = Buildings.SlotOf(id);
            if (Buildings.TownAt(slot) != town)
            {
                continue;
            }

            var old = Buildings.TypeDefinitionAt(slot);
            var ox = Buildings.XAt(slot);
            var oz = Buildings.ZAt(slot);
            var zone = ZoneAt(town, ox, oz);
            var count = 0;
            var total = 0;
            for (var type = 0; type < buildings.Count; type++)
            {
                var definition = buildings[type];
                if (definition.Level <= old.Level || definition.Width != old.Width || definition.Depth != old.Depth
                    || (definition.Zones & zone) == 0 || !definition.InEra(_year)
                    || definition.MinTownPopulation > population)
                {
                    continue;
                }

                _candidates[count++] = type;
                total += Weight(definition, zone);
            }

            if (count == 0)
            {
                continue;
            }

            var pick = random.NextInt(total);
            var chosen = _candidates[count - 1];
            for (var i = 0; i < count; i++)
            {
                var weight = Weight(buildings[_candidates[i]], zone);
                if (pick < weight)
                {
                    chosen = _candidates[i];
                    break;
                }

                pick -= weight;
            }

            RemoveBuilding(slot, clearObjects: false);
            PlaceBuilding(town, chosen, ox, oz);
            return true;
        }

        return false;
    }

    /// <summary>
    /// A tile a building may use (FR-006): inside the map, buildable land, no object, no road piece or bridge or
    /// tunnel passing, no industry.
    /// </summary>
    internal bool TileFree(int x, int z)
    {
        if (!_map.Contains(x, z))
        {
            return false;
        }

        var tile = _map.Index(x, z);
        return !_map.IsWater(tile)
            && _map.IsBuildable(x, z)
            && _state.Objects.KindAt(tile) == TileObjectKind.None
            && !_state.Roads.HasRoad(tile)
            && (_state.Roads.FlagsAt(tile) & RoadFeatures.Passing) == 0
            && !_state.IsIndustry(x, z);
    }

    /// <summary>
    /// Tiles kept free for future streets (research R6): grid lines of grid towns, and the straight continuation
    /// of a dead-end street in organic towns.
    /// </summary>
    private bool Reserved(int town, int x, int z)
    {
        var spacing = Rules.Growth.GridSpacing;
        if ((StreetPattern)_pattern[town] == StreetPattern.Grid)
        {
            return (x - _centreX[town]) % spacing == 0 || (z - _centreZ[town]) % spacing == 0;
        }

        var roads = _state.Roads;
        for (var d = 0; d < 8; d += 2)
        {
            var nx = x + Dir8.OffsetX(d);
            var nz = z + Dir8.OffsetZ(d);
            if (!_map.Contains(nx, nz))
            {
                continue;
            }

            var n = _map.Index(nx, nz);
            // The neighbour is a dead end whose only link points away from this tile: this tile continues it.
            if (roads.HasRoad(n) && Dir8.Count(roads.LinksAt(n)) == 1 && (roads.LinksAt(n) & Dir8.Bit(d)) != 0)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Footprint check (FR-006): every tile free and not reserved, heights within one step, and at least one tile
    /// next to a road piece owned by the town.
    /// </summary>
    private bool FootprintFits(int town, int x, int z, int width, int depth)
    {
        var minHeight = int.MaxValue;
        var maxHeight = int.MinValue;
        var nextToRoad = false;
        var owner = TileOwner.Town(town);
        for (var dz = 0; dz < depth; dz++)
        {
            for (var dx = 0; dx < width; dx++)
            {
                var tx = x + dx;
                var tz = z + dz;
                if ((dx | dz) != 0 && (!TileFree(tx, tz) || Reserved(town, tx, tz)))
                {
                    return false;
                }

                var h = _map.HeightAt(tx, tz);
                minHeight = Math.Min(minHeight, h);
                maxHeight = Math.Max(maxHeight, h);
                if (!nextToRoad)
                {
                    nextToRoad = NextToOwnRoad(tx, tz, owner);
                }
            }
        }

        return nextToRoad && maxHeight - minHeight <= 1;
    }

    private bool NextToOwnRoad(int x, int z, TileOwner owner)
    {
        for (var d = 0; d < 8; d += 2)
        {
            var nx = x + Dir8.OffsetX(d);
            var nz = z + Dir8.OffsetZ(d);
            if (!_map.Contains(nx, nz))
            {
                continue;
            }

            var n = _map.Index(nx, nz);
            if (_state.Roads.HasRoad(n) && _state.Objects.KindAt(n) == TileObjectKind.Road && _state.Objects.OwnerAt(n) == owner)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Writes a building: objects and owner on its footprint, trees removed, town totals updated.</summary>
    private void PlaceBuilding(int town, int type, int x, int z)
    {
        var definition = Rules.Buildings[type];
        var baseHeight = 0;
        var owner = TileOwner.Town(town);
        for (var dz = 0; dz < definition.Depth; dz++)
        {
            for (var dx = 0; dx < definition.Width; dx++)
            {
                var tile = _map.Index(x + dx, z + dz);
                baseHeight = Math.Max(baseHeight, _map.Heights[tile]);
                _state.PlaceTownObject(tile, TileObjectKind.TownBuilding, owner, notify: !_founding);
            }
        }

        Buildings.Add(type, x, z, town, _year, baseHeight);
        _population[town] += definition.Residents;
        _buildingCount[town]++;
        _passengerProduction[town] += definition.PassengersPerMonth;
        _mailProduction[town] += definition.MailPerMonth;
        _goodsAcceptance[town] += definition.GoodsAcceptance;
        _foodAcceptance[town] += definition.FoodAcceptance;
        Version++;
    }

    /// <summary>Removes a building from the store and its town's totals; optionally clears its tiles.</summary>
    private void RemoveBuilding(int slot, bool clearObjects)
    {
        var town = Buildings.TownAt(slot);
        var definition = Buildings.TypeDefinitionAt(slot);
        if (clearObjects)
        {
            for (var dz = 0; dz < definition.Depth; dz++)
            {
                for (var dx = 0; dx < definition.Width; dx++)
                {
                    _state.PlaceTownObject(_map.Index(Buildings.XAt(slot) + dx, Buildings.ZAt(slot) + dz), TileObjectKind.None, TileOwner.None, notify: true);
                }
            }
        }

        _population[town] -= definition.Residents;
        _buildingCount[town]--;
        _passengerProduction[town] -= definition.PassengersPerMonth;
        _mailProduction[town] -= definition.MailPerMonth;
        _goodsAcceptance[town] -= definition.GoodsAcceptance;
        _foodAcceptance[town] -= definition.FoodAcceptance;
        Buildings.RemoveAt(slot);
        Version++;
    }
}

using Talt.Core.Determinism;
using Talt.Core.Maps;
using Talt.Core.Network;

namespace Talt.Core.Construction;

/// <summary>
/// Everything construction reads and changes (data-model.md ConstructionState, research R1): the
/// map's tile data, the object/owner layer, the companies and the undo record. Owned by
/// <see cref="Simulation.GameSimulation"/> when the scenario has a map; changed only by construction
/// commands, so it is deterministic and part of the fingerprint.
/// </summary>
public sealed class ConstructionState : IStateHashable
{
    private readonly List<Company> _companies = [];
    private readonly List<int> _changedTiles = [];
    private readonly TileSet _touched = new(1024);

    public ConstructionState(ConstructionRules rules, MapData map, TransportNetwork? publicRoads = null)
    {
        Rules = rules ?? throw new ArgumentNullException(nameof(rules));
        Map = map ?? throw new ArgumentNullException(nameof(map));
        Objects = new TileObjects(map.Width, map.Depth);
        Player = new Company(Company.PlayerId, rules.StartingBalance);
        _companies.Add(Player);
        if (publicRoads is not null)
        {
            MarkPublicRoads(publicRoads);
        }

        Plan.EnsureCapacity(Math.Min(rules.MaxTilesPerAction, 4096));
    }

    public ConstructionRules Rules { get; }

    /// <summary>The map's tile data, changed in place by construction.</summary>
    public MapData Map { get; }

    public TileObjects Objects { get; }

    /// <summary>The player's company.</summary>
    public Company Player { get; }

    public IReadOnlyList<Company> Companies => _companies;

    public UndoRecord Undo { get; } = new();

    /// <summary>Bumped by every applied action (lets previews know they must be planned again). Not hashed.</summary>
    public int Version { get; private set; }

    /// <summary>Planner and plan used by command validation (the client's preview keeps its own).</summary>
    internal ConstructionPlanner Planner { get; } = new();

    internal ConstructionPlan Plan { get; } = new();

    public Company? FindCompany(int id)
    {
        foreach (var company in _companies)
        {
            if (company.Id == id)
            {
                return company;
            }
        }

        return null;
    }

    /// <summary>
    /// Moves the tiles changed since the last call (height, scenery or objects) into
    /// <paramref name="into"/>, for the client to update voxels and trees. Not simulation state.
    /// </summary>
    public bool TryTakeChangedTiles(List<int> into)
    {
        ArgumentNullException.ThrowIfNull(into);
        if (_changedTiles.Count == 0)
        {
            return false;
        }

        into.AddRange(_changedTiles);
        _changedTiles.Clear();
        return true;
    }

    public void AppendTo(StateHasher hasher)
    {
        ArgumentNullException.ThrowIfNull(hasher);
        hasher.Append(_companies.Count);
        foreach (var company in _companies)
        {
            hasher.Append(company.Id);
            hasher.Append(company.Balance);
        }

        Map.AppendStateTo(hasher);
        hasher.Append(Objects);
        hasher.Append(Undo);
    }

    /// <summary>
    /// Applies a validated plan as one unit: tiles, trees, objects, flags around the changes, money and
    /// the undo record (which replaces the previous one).
    /// </summary>
    internal void Commit(ConstructionPlan plan, Company company, long tick)
    {
        var map = Map;
        var width = map.Width;
        var depth = map.Depth;

        _touched.Clear();
        foreach (var change in plan.Changes)
        {
            var tile = change.Index;
            _touched.Add(tile, out _);
            var x = tile % width;
            var z = tile / width;
            if (x > 0)
            {
                _touched.Add(tile - 1, out _);
            }

            if (x < width - 1)
            {
                _touched.Add(tile + 1, out _);
            }

            if (z > 0)
            {
                _touched.Add(tile - width, out _);
            }

            if (z < depth - 1)
            {
                _touched.Add(tile + width, out _);
            }
        }

        Undo.Begin(company.Id, tick, plan.Cost);
        for (var slot = 0; slot < _touched.Count; slot++)
        {
            Undo.AddBefore(_touched[slot], UndoRecord.Capture(map, Objects, _touched[slot]));
        }

        foreach (var change in plan.Changes)
        {
            var tile = change.Index;
            if (change.NewHeight != change.OldHeight)
            {
                map.SetHeight(tile, change.NewHeight);
                if ((TerrainType)map.Terrain[tile] == TerrainType.Rock)
                {
                    map.SetTerrain(tile, TerrainType.Grass); // Digging exposes soil (research R5).
                }
            }

            map.SetScenery(tile, change.NewScenery);
            Objects.Set(tile, change.NewObject, change.NewOwner);
            _changedTiles.Add(tile);
        }

        for (var slot = 0; slot < _touched.Count; slot++)
        {
            var tile = _touched[slot];
            map.RecomputeTraits(tile % width, tile / width);
        }

        for (var slot = 0; slot < _touched.Count; slot++)
        {
            Undo.SetAfter(slot, UndoRecord.Capture(map, Objects, _touched[slot]));
        }

        company.Spend(plan.Cost);
        Version++;
    }

    /// <summary>Why the company's last action cannot be undone now, or null when it can.</summary>
    internal string? CheckUndo(int companyId, long tick)
    {
        var undo = Undo;
        if (!undo.HasValue || undo.CompanyId != companyId)
        {
            return ConstructionReasons.NothingToUndo;
        }

        if (tick - undo.AppliedAtTick > Rules.UndoWindowTicks)
        {
            return ConstructionReasons.UndoExpired;
        }

        var tiles = undo.Tiles;
        var after = undo.After;
        for (var i = 0; i < tiles.Length; i++)
        {
            if (UndoRecord.Capture(Map, Objects, tiles[i]) != after[i])
            {
                return ConstructionReasons.ChangedSince;
            }
        }

        return null;
    }

    /// <summary>Restores the tiles of the last action, refunds its cost and forgets it.</summary>
    internal void ApplyUndo()
    {
        var undo = Undo;
        var company = FindCompany(undo.CompanyId)!;
        var tiles = undo.Tiles;
        var before = undo.Before;
        var after = undo.After;
        for (var i = 0; i < tiles.Length; i++)
        {
            UndoRecord.Restore(Map, Objects, tiles[i], before[i]);
            if (before[i].Height != after[i].Height || before[i].Scenery != after[i].Scenery || before[i].Kind != after[i].Kind)
            {
                _changedTiles.Add(tiles[i]);
            }
        }

        company.Refund(undo.Cost);
        undo.Clear();
        Version++;
    }

    /// <summary>
    /// Marks every tile crossed by the prototype network as a public road owned by town 0 (data-model.md
    /// TileObjects). Integer walk in half-tile steps, the same tiles <see cref="NetworkTerrainPainter"/>
    /// paints for the network's straight grid roads.
    /// </summary>
    private void MarkPublicRoads(TransportNetwork network)
    {
        var owner = TileOwner.Town(0);
        for (var edge = 0; edge < network.EdgeCount; edge++)
        {
            var from = network.EdgeFrom(edge);
            var to = network.EdgeTo(edge);
            if (from > to)
            {
                continue;
            }

            var a = network.NodePosition(from);
            var b = network.NodePosition(to);
            long dx = b.X - a.X;
            long dz = b.Z - a.Z;
            var steps = (Math.Max(Math.Abs(dx), Math.Abs(dz)) / (FixedMath.UnitsPerTile / 2)) + 1;
            for (long i = 0; i <= steps; i++)
            {
                var x = (int)((a.X + (dx * i / steps)) >> FixedMath.UnitsPerTileBits);
                var z = (int)((a.Z + (dz * i / steps)) >> FixedMath.UnitsPerTileBits);
                if (Map.Contains(x, z))
                {
                    Objects.Set(Map.Index(x, z), TileObjectKind.PublicRoad, owner);
                }
            }
        }
    }
}

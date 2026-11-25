using Talt.Core.Determinism;
using Talt.Core.Industries;
using Talt.Core.Maps;
using Talt.Core.Network;
using Talt.Core.Roads;

namespace Talt.Core.Construction;

/// <summary>
/// Everything construction reads and changes (data-model.md ConstructionState, research R1): the
/// map's tile data, the object/owner layer, the road layer (feature 007), the companies and the undo
/// record. Owned by <see cref="Simulation.GameSimulation"/> when the scenario has a map; changed only by
/// construction commands, so it is deterministic and part of the fingerprint.
/// </summary>
public sealed class ConstructionState : IStateHashable
{
    private readonly List<Company> _companies = [];
    private readonly List<int> _changedTiles = [];
    private readonly List<int> _roadTiles = [];
    private readonly TileSet _touched = new(1024);

    // Feature 011 (research R5): road tiles owned by each company, by road type, kept up to date by every tile
    // mutation path so monthly maintenance never scans the map. Derived from tiles, so not hashed.
    private int[] _ownedRoads;

    public ConstructionState(ConstructionRules rules, MapData map, RoadRules? roadRules = null)
    {
        Rules = rules ?? throw new ArgumentNullException(nameof(rules));
        Map = map ?? throw new ArgumentNullException(nameof(map));
        RoadRules = roadRules ?? RoadRules.Default;
        Objects = new TileObjects(map.Width, map.Depth);
        Roads = new RoadLayer(map.Width, map.Depth);
        Network = new RoadNetwork(Roads);
        Span<int> speeds = stackalloc int[RoadRules.Types.Count];
        for (var i = 0; i < speeds.Length; i++)
        {
            speeds[i] = RoadRules.Types[i].SpeedKmh;
        }

        Network.SetTypeSpeeds(speeds);
        Player = new Company(Company.PlayerId, rules.StartingBalance);
        _companies.Add(Player);
        _ownedRoads = new int[RoadRules.Types.Count];
        Plan.EnsureCapacity(Math.Min(rules.MaxTilesPerAction, 4096));
    }

    public ConstructionRules Rules { get; }

    /// <summary>Road types, costs and limits (feature 007).</summary>
    public RoadRules RoadRules { get; }

    /// <summary>The map's tile data, changed in place by construction.</summary>
    public MapData Map { get; }

    public TileObjects Objects { get; }

    /// <summary>Per-tile road pieces (feature 007).</summary>
    public RoadLayer Roads { get; }

    /// <summary>The routing graph derived from <see cref="Roads"/>, updated after every change (not hashed: derived).</summary>
    public RoadNetwork Network { get; }

    /// <summary>The industries of the game, for footprint checks; null in tests that build a bare state.</summary>
    public IndustryFleet? Industries { get; private set; }

    /// <summary>The game year (research R12): set by the simulation from its date; not hashed (derived from the tick).</summary>
    public int Year { get; set; } = 1950;

    /// <summary>The player's company.</summary>
    public Company Player { get; }

    public IReadOnlyList<Company> Companies => _companies;

    public UndoRecord Undo { get; } = new();

    /// <summary>
    /// Price level of construction in parts per million (feature 011, inflation, research R7): set by the finance
    /// system each month; 1 000 000 without inflation. Not hashed here (the finance state hashes it).
    /// </summary>
    public long CostLevel { get; internal set; } = Finance.PaymentCalculator.LevelOne;

    /// <summary>A planned cost at today's price level (rounded down).</summary>
    public long Inflate(long cost) => CostLevel == Finance.PaymentCalculator.LevelOne ? cost : Finance.Money.MulDiv(cost, CostLevel, Finance.PaymentCalculator.LevelOne);

    /// <summary>Road tiles owned by the company with this road type (incremental, research R5).</summary>
    public int OwnedRoadTiles(int companyId, int roadType)
    {
        var index = (companyId * RoadRules.Types.Count) + roadType;
        return (uint)roadType < (uint)RoadRules.Types.Count && (uint)index < (uint)_ownedRoads.Length ? _ownedRoads[index] : 0;
    }

    /// <summary>Bumped by every applied action (lets previews know they must be planned again). Not hashed.</summary>
    public int Version { get; private set; }

    /// <summary>Planner and plan used by command validation (the client's preview keeps its own).</summary>
    internal ConstructionPlanner Planner { get; } = new();

    internal ConstructionPlan Plan { get; } = new();

    /// <summary>The towns of the game (feature 008), for rating rules; null in tests that build a bare state.</summary>
    public Towns.TownRegistry? Towns { get; private set; }

    /// <summary>Lets construction ask towns about ratings and report demolitions (feature 008).</summary>
    public void AttachTowns(Towns.TownRegistry towns) => Towns = towns ?? throw new ArgumentNullException(nameof(towns));

    /// <summary>The stations of the game (feature 010); null in tests that build a bare state.</summary>
    public Stations.StationRegistry? Stations { get; private set; }

    /// <summary>Lets road planning protect station roads and planners find stations (feature 010).</summary>
    public void AttachStations(Stations.StationRegistry stations) => Stations = stations ?? throw new ArgumentNullException(nameof(stations));

    /// <summary>Lets road planning avoid industry footprints (feature 009).</summary>
    public void AttachIndustries(IndustryFleet industries) => Industries = industries ?? throw new ArgumentNullException(nameof(industries));

    /// <summary>True when an industry stands on the tile.</summary>
    public bool IsIndustry(int x, int z) => Industries is not null && Industries.IsOccupied(x, z);

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
        hasher.Append(Roads);
    }

    /// <summary>Builds the routing graph from the road layer from scratch (game creation).</summary>
    internal void RebuildNetwork()
    {
        Network.BuildAll();
        Roads.DiscardChangedTiles();
    }

    /// <summary>
    /// Applies a validated plan as one unit: tiles, trees, objects, road pieces, flags around the changes,
    /// money and the undo record (which replaces the previous one); then updates the road network.
    /// </summary>
    internal void Commit(ConstructionPlan plan, Company company, long tick)
    {
        var map = Map;
        var width = map.Width;
        var depth = map.Depth;

        _touched.Clear();
        _roadTiles.Clear();
        foreach (var change in plan.Changes)
        {
            var tile = change.Index;
            _touched.Add(tile, out _);
            if (change.RoadChanged)
            {
                _roadTiles.Add(tile);
            }

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

        // Towns (feature 008) learn about demolished buildings, removed town roads and trees before the tiles
        // change: the rating penalties apply with the action (FR-017).
        var removedTownBuildings = Towns?.ApplyConstruction(plan, company.Id) ?? false;

        Undo.Begin(company.Id, tick, plan.Cost);
        for (var slot = 0; slot < _touched.Count; slot++)
        {
            Undo.AddBefore(_touched[slot], UndoRecord.Capture(map, Objects, Roads, _touched[slot]));
        }

        var roadsChange = _roadTiles.Count > 0;
        if (roadsChange)
        {
            Network.BeforeChange(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_roadTiles));
        }

        foreach (var change in plan.Changes)
        {
            var tile = change.Index;
            CountRoad(tile, -1);
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
            if (change.RoadChanged)
            {
                Roads.Set(tile, change.NewRoad);
            }

            CountRoad(tile, 1);
            _changedTiles.Add(tile);
        }

        if (roadsChange)
        {
            Network.AfterChange(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_roadTiles));
        }

        for (var slot = 0; slot < _touched.Count; slot++)
        {
            var tile = _touched[slot];
            map.RecomputeTraits(tile % width, tile / width);
        }

        for (var slot = 0; slot < _touched.Count; slot++)
        {
            Undo.SetAfter(slot, UndoRecord.Capture(map, Objects, Roads, _touched[slot]));
        }

        company.Spend(plan.Cost);
        if (removedTownBuildings)
        {
            // Undo restores tiles but not a town's buildings, so demolishing them cannot be undone (plan.md).
            Undo.Clear();
        }

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
            if (UndoRecord.Capture(Map, Objects, Roads, tiles[i]) != after[i])
            {
                return ConstructionReasons.ChangedSince;
            }
        }

        return null;
    }

    /// <summary>Restores the tiles of the last action, refunds its cost, updates the network and forgets it.</summary>
    internal void ApplyUndo()
    {
        var undo = Undo;
        var company = FindCompany(undo.CompanyId)!;
        var tiles = undo.Tiles;
        var before = undo.Before;
        var after = undo.After;
        _roadTiles.Clear();
        for (var i = 0; i < tiles.Length; i++)
        {
            if (before[i].Road != after[i].Road)
            {
                _roadTiles.Add(tiles[i]);
            }
        }

        var roadsChange = _roadTiles.Count > 0;
        if (roadsChange)
        {
            Network.BeforeChange(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_roadTiles));
        }

        for (var i = 0; i < tiles.Length; i++)
        {
            CountRoad(tiles[i], -1);
            UndoRecord.Restore(Map, Objects, Roads, tiles[i], before[i]);
            CountRoad(tiles[i], 1);
            if (before[i].Height != after[i].Height || before[i].Scenery != after[i].Scenery || before[i].Kind != after[i].Kind
                || before[i].Road != after[i].Road)
            {
                _changedTiles.Add(tiles[i]);
            }
        }

        if (roadsChange)
        {
            Network.AfterChange(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_roadTiles));
        }

        company.Refund(undo.Cost);
        undo.Clear();
        Version++;
    }

    /// <summary>
    /// Debug only (client <c>--road-grid</c>, benchmarks): lays a full road grid every <paramref name="spacing"/>
    /// tiles and rebuilds the network. Changes state outside commands, so recorded sessions do not replay it.
    /// </summary>
    public int SeedDebugGrid(int spacing)
    {
        var count = RoadSeeder.SeedGrid(this, spacing, Math.Max(0, RoadRules.BestOrdinaryType(Year)));
        RebuildNetwork();
        return count;
    }

    /// <summary>
    /// Writes a town building tile outside of commands (feature 008 growth and founding): the object and owner,
    /// and trees removed. With <paramref name="notify"/> the tile is reported to the client and previews replan.
    /// </summary>
    internal void PlaceTownObject(int tile, TileObjectKind kind, TileOwner owner, bool notify)
    {
        CountRoad(tile, -1);
        Objects.Set(tile, kind, owner);
        CountRoad(tile, 1);
        var hadTree = Scenery.Kind(Map.SceneryData[tile]) != 0;
        if (kind != TileObjectKind.None && hadTree)
        {
            Map.SetScenery(tile, Scenery.None);
        }

        if (notify)
        {
            if (hadTree)
            {
                _changedTiles.Add(tile);
            }

            Version++;
        }
    }

    /// <summary>
    /// Writes a road piece laid by a town (feature 008): new pieces get the town as owner and lose their trees;
    /// existing pieces (the town's own, or another road it connects to) only get the new links. The caller
    /// updates the network.
    /// </summary>
    internal void PlaceTownRoad(int tile, in RoadPiece piece, TileOwner owner, bool newPiece, bool notify)
    {
        CountRoad(tile, -1);
        Roads.Set(tile, piece);
        CountRoad(tile, 1);
        if (newPiece)
        {
            PlaceTownObject(tile, TileObjectKind.Road, owner, notify);
        }
        else if (notify)
        {
            Version++;
        }
    }

    /// <summary>
    /// Writes a road piece outside of commands, at game creation only (town streets, debug grids): no undo,
    /// no money, no network update (call <see cref="RebuildNetwork"/> afterwards).
    /// </summary>
    internal void PlaceInitialRoad(int tile, in RoadPiece piece, TileOwner owner)
    {
        CountRoad(tile, -1);
        Roads.Set(tile, piece);
        Objects.Set(tile, piece.HasRoad ? TileObjectKind.Road : TileObjectKind.None, owner);
        CountRoad(tile, 1);
        Map.SetScenery(tile, Scenery.None);
    }

    /// <summary>Adds (+1) or removes (−1) a tile's contribution to the owned-road counts (research R5).</summary>
    private void CountRoad(int tile, int sign)
    {
        var owner = Objects.OwnerAt(tile);
        if (owner.Kind != TileOwnerKind.Company || !Roads.HasRoad(tile))
        {
            return;
        }

        var types = RoadRules.Types.Count;
        var type = Roads.TypeAt(tile);
        if ((uint)type >= (uint)types)
        {
            return;
        }

        var index = (owner.Index * types) + type;
        if (index >= _ownedRoads.Length)
        {
            Array.Resize(ref _ownedRoads, ((owner.Index + 1) * types));
        }

        _ownedRoads[index] += sign;
    }
}

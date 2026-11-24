using Talt.Core.Determinism;
using Talt.Core.Maps;
using Talt.Core.Roads;

namespace Talt.Core.Construction;

/// <summary>Everything construction can change on one tile.</summary>
public readonly record struct TileSnapshot(byte Height, byte Terrain, byte Flags, byte Scenery, TileObjectKind Kind, TileOwner Owner, RoadPiece Road = default);

/// <summary>
/// The last applied construction action, kept so it can be undone (data-model.md UndoRecord,
/// research R10): the company, the tick, the cost and, for every tile the action touched (changed
/// tiles and neighbours whose flags were recomputed), the values before and after. Buffers are reused.
/// </summary>
public sealed class UndoRecord : IStateHashable
{
    private int[] _tiles = new int[256];
    private TileSnapshot[] _before = new TileSnapshot[256];
    private TileSnapshot[] _after = new TileSnapshot[256];

    public bool HasValue { get; private set; }

    public int CompanyId { get; private set; }

    public long AppliedAtTick { get; private set; }

    public long Cost { get; private set; }

    public int TileCount { get; private set; }

    public ReadOnlySpan<int> Tiles => _tiles.AsSpan(0, TileCount);

    public ReadOnlySpan<TileSnapshot> Before => _before.AsSpan(0, TileCount);

    public ReadOnlySpan<TileSnapshot> After => _after.AsSpan(0, TileCount);

    public static TileSnapshot Capture(MapData map, TileObjects objects, RoadLayer roads, int tile) => new(
        map.Heights[tile],
        map.Terrain[tile],
        map.Flags[tile],
        map.SceneryData[tile],
        objects.KindAt(tile),
        objects.OwnerAt(tile),
        roads.PieceAt(tile));

    /// <summary>Restores a tile; returns true when its road piece changed (the network must be updated).</summary>
    public static bool Restore(MapData map, TileObjects objects, RoadLayer roads, int tile, in TileSnapshot snapshot)
    {
        map.SetHeight(tile, snapshot.Height);
        map.SetTerrain(tile, (TerrainType)snapshot.Terrain);
        map.SetFlags(tile, (TileTraits)snapshot.Flags);
        map.SetScenery(tile, snapshot.Scenery);
        objects.Set(tile, snapshot.Kind, snapshot.Owner);
        if (roads.PieceAt(tile) == snapshot.Road)
        {
            return false;
        }

        roads.Set(tile, snapshot.Road);
        return true;
    }

    internal void Begin(int companyId, long tick, long cost)
    {
        HasValue = true;
        CompanyId = companyId;
        AppliedAtTick = tick;
        Cost = cost;
        TileCount = 0;
    }

    internal void AddBefore(int tile, in TileSnapshot before)
    {
        if (TileCount == _tiles.Length)
        {
            var size = _tiles.Length * 2;
            Array.Resize(ref _tiles, size);
            Array.Resize(ref _before, size);
            Array.Resize(ref _after, size);
        }

        _tiles[TileCount] = tile;
        _before[TileCount] = before;
        TileCount++;
    }

    internal void SetAfter(int entry, in TileSnapshot after) => _after[entry] = after;

    internal void Clear()
    {
        HasValue = false;
        TileCount = 0;
        Cost = 0;
        CompanyId = 0;
        AppliedAtTick = 0;
    }

    public void AppendTo(StateHasher hasher)
    {
        ArgumentNullException.ThrowIfNull(hasher);
        hasher.Append(HasValue ? 1 : 0);
        if (!HasValue)
        {
            return;
        }

        hasher.Append(CompanyId);
        hasher.Append(AppliedAtTick);
        hasher.Append(Cost);
        hasher.Append(TileCount);
        hasher.Append<int>(Tiles);
        foreach (var snapshot in Before)
        {
            Append(hasher, snapshot);
        }

        foreach (var snapshot in After)
        {
            Append(hasher, snapshot);
        }
    }

    private static void Append(StateHasher hasher, in TileSnapshot s)
    {
        hasher.Append((long)s.Height | ((long)s.Terrain << 8) | ((long)s.Flags << 16) | ((long)s.Scenery << 24) | ((long)s.Kind << 32) | ((long)s.Owner.Raw << 40));
        hasher.Append(s.Road.Pack());
    }
}

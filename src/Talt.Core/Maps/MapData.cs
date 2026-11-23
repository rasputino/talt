using Talt.Core.Determinism;

namespace Talt.Core.Maps;

/// <summary>Counts describing a generated map (for the console summary, tests and benchmarks).</summary>
/// <param name="SeaTiles">Tiles under the sea.</param>
/// <param name="LakeTiles">Tiles under lakes.</param>
/// <param name="RiverTiles">River tiles.</param>
/// <param name="Springs">River tiles with no river tile upstream.</param>
public readonly record struct MapStatistics(int SeaTiles, int LakeTiles, int RiverTiles, int Springs);

/// <summary>
/// The result of map generation: compact per-tile arrays and the reserved sites (data-model.md,
/// MapData). Index of tile (x, z) is <c>x + z * Width</c>. Gameplay reads this, never the voxels.
/// </summary>
public sealed class MapData
{
    private readonly byte[] _height;
    private readonly byte[] _terrain;
    private readonly byte[] _waterLevel;
    private readonly byte[] _flags;
    private readonly byte[] _scenery;

    internal MapData(
        MapSettings settings,
        byte[] height,
        byte[] terrain,
        byte[] waterLevel,
        byte[] flags,
        byte[] scenery,
        IReadOnlyList<Site> sites,
        MapStatistics statistics)
    {
        Settings = settings;
        _height = height;
        _terrain = terrain;
        _waterLevel = waterLevel;
        _flags = flags;
        _scenery = scenery;
        Sites = sites;
        Statistics = statistics;
    }

    public MapSettings Settings { get; }

    public int Width => Settings.Width;

    public int Depth => Settings.Depth;

    public MapStatistics Statistics { get; }

    /// <summary>All reserved sites, sorted by kind, then Z, then X.</summary>
    public IReadOnlyList<Site> Sites { get; }

    public IEnumerable<Site> TownSites => Sites.Where(s => s.Kind == SiteKind.Town);

    public IEnumerable<Site> IndustrySites => Sites.Where(s => s.Kind != SiteKind.Town);

    /// <summary>Ground height of each tile in voxels (river beds are carved ground).</summary>
    public ReadOnlySpan<byte> Heights => _height;

    public ReadOnlySpan<byte> Terrain => _terrain;

    /// <summary>Water surface height of each tile; 0 where there is no water.</summary>
    public ReadOnlySpan<byte> WaterLevels => _waterLevel;

    public ReadOnlySpan<byte> Flags => _flags;

    public ReadOnlySpan<byte> SceneryData => _scenery;

    public int Index(int x, int z) => x + z * Width;

    public bool Contains(int x, int z) => (uint)x < (uint)Width && (uint)z < (uint)Depth;

    public int HeightAt(int x, int z) => _height[Index(x, z)];

    public TerrainType TerrainAt(int x, int z) => (TerrainType)_terrain[Index(x, z)];

    public int WaterLevelAt(int x, int z) => _waterLevel[Index(x, z)];

    public TileTraits FlagsAt(int x, int z) => (TileTraits)_flags[Index(x, z)];

    public byte SceneryAt(int x, int z) => _scenery[Index(x, z)];

    public bool IsBuildable(int x, int z) => FlagsAt(x, z).HasFlag(TileTraits.Buildable);

    /// <summary>Height of the visible surface: the water surface where there is water, else the ground.</summary>
    public int SurfaceHeightAt(int x, int z)
    {
        var i = Index(x, z);
        return Math.Max(_height[i], _waterLevel[i]);
    }

    /// <summary>Water tile: sea, lake or river, or any water surface above the ground.</summary>
    public bool IsWater(int index) =>
        _waterLevel[index] > 0 || (_flags[index] & (byte)(TileTraits.Sea | TileTraits.Lake | TileTraits.River)) != 0;

    // Mutators for construction (feature 006). Only ConstructionState calls them, inside commands.
    internal void SetHeight(int index, int height) => _height[index] = (byte)height;

    internal void SetTerrain(int index, TerrainType terrain) => _terrain[index] = (byte)terrain;

    internal void SetFlags(int index, TileTraits flags) => _flags[index] = (byte)flags;

    internal void SetScenery(int index, byte scenery) => _scenery[index] = scenery;

    /// <summary>
    /// Recomputes the Cliff and Buildable flags of one land tile from its neighbours, with the rules of
    /// the generator's classifier: a cliff has a land neighbour more than one voxel higher or lower;
    /// buildable land is neither cliff nor bare rock. Water tiles are left as they are.
    /// </summary>
    internal void RecomputeTraits(int x, int z)
    {
        var i = Index(x, z);
        if (IsWater(i))
        {
            return;
        }

        var h = _height[i];
        var cliff = Steep(h, x > 0, i - 1) || Steep(h, x < Width - 1, i + 1)
            || Steep(h, z > 0, i - Width) || Steep(h, z < Depth - 1, i + Width);
        var flags = (TileTraits)_flags[i] & ~(TileTraits.Cliff | TileTraits.Buildable);
        if (cliff)
        {
            flags |= TileTraits.Cliff;
        }
        else if ((TerrainType)_terrain[i] != TerrainType.Rock)
        {
            flags |= TileTraits.Buildable;
        }

        _flags[i] = (byte)flags;

        bool Steep(int height, bool inside, int neighbour) =>
            inside && !IsWater(neighbour) && Math.Abs(height - _height[neighbour]) > 1;
    }

    /// <summary>Appends every mutable tile array to a simulation fingerprint (feature 006).</summary>
    internal void AppendStateTo(StateHasher hasher)
    {
        hasher.Append(Width);
        hasher.Append(Depth);
        hasher.Append<byte>(_height);
        hasher.Append<byte>(_terrain);
        hasher.Append<byte>(_waterLevel);
        hasher.Append<byte>(_flags);
        hasher.Append<byte>(_scenery);
    }

    /// <summary>64-bit fingerprint of the settings, all tile arrays and all sites (determinism checks and goldens).</summary>
    public ulong Fingerprint()
    {
        var hasher = new StateHasher();
        hasher.Append(Settings.Width);
        hasher.Append(Settings.Depth);
        hasher.Append(Settings.Seed);
        hasher.Append(Settings.Roughness);
        hasher.Append(Settings.MaxHeight);
        hasher.Append(Settings.WaterAmount);
        hasher.Append(Settings.SeaLevel);
        hasher.Append<byte>(_height);
        hasher.Append<byte>(_terrain);
        hasher.Append<byte>(_waterLevel);
        hasher.Append<byte>(_flags);
        hasher.Append<byte>(_scenery);
        hasher.Append(Sites.Count);
        foreach (var site in Sites)
        {
            hasher.Append(site.X);
            hasher.Append(site.Z);
            hasher.Append((int)site.Kind);
            hasher.Append(site.FreeRadius);
            hasher.Append(site.Score);
        }

        return hasher.GetFingerprint();
    }
}

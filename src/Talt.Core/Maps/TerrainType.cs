namespace Talt.Core.Maps;

/// <summary>What covers the ground of a tile (one byte per tile).</summary>
public enum TerrainType : byte
{
    Grass = 0,
    Water,
    Beach,
    Rock,
    Snow,
}

/// <summary>Per-tile facts stored next to the terrain type (one byte per tile).</summary>
[Flags]
public enum TileTraits : byte
{
    None = 0,
    Sea = 1,
    Lake = 2,
    River = 4,

    /// <summary>Land that can host roads and buildings: not water, not a cliff, not bare rock.</summary>
    Buildable = 8,

    /// <summary>Land with a neighbouring land tile more than one voxel higher or lower.</summary>
    Cliff = 16,

    /// <summary>Land within two tiles of the sea or a lake.</summary>
    Coast = 32,
}

/// <summary>
/// Trees and other scenery on a tile, packed in one byte: bits 0 to 2 the kind (0 = nothing),
/// bits 3 to 7 a variant or growth stage. Scenery is data, not terrain voxels (it can be removed).
/// </summary>
public static class Scenery
{
    public const byte None = 0;
    public const int KindBroadleaf = 1;
    public const int KindConifer = 2;
    public const int KindBirch = 3;

    public static byte Pack(int kind, int variant) => (byte)((kind & 7) | ((variant & 31) << 3));

    public static int Kind(byte scenery) => scenery & 7;

    public static int Variant(byte scenery) => scenery >> 3;
}

using Talt.Core.Determinism;

namespace Talt.Core.Construction;

/// <summary>
/// What stands on a tile besides terrain and trees (data-model.md TileObjects.Kind). Later features
/// add their kinds (road pieces, station tiles, depots) and a demolish cost in <c>construction.json</c>.
/// </summary>
public enum TileObjectKind : byte
{
    None = 0,

    /// <summary>
    /// A road piece (feature 007): its links, type and flags are in <see cref="Roads.RoadLayer"/>; the owner
    /// is a company or a town. Was the prototype's <c>PublicRoad</c> (same value).
    /// </summary>
    Road = 1,

    /// <summary>Reserved for tests of the demolish rules (nothing in the game creates it).</summary>
    Test = 255,
}

/// <summary>Who an owner value refers to.</summary>
public enum TileOwnerKind
{
    None = 0,
    Company = 1,
    Town = 2,
    Industry = 3,
}

/// <summary>
/// Owner of a tile object packed in 16 bits (data-model.md TileObjects.Owner): the kind in bits 14–15
/// and an index (company id, town id...) in bits 0–13.
/// </summary>
public readonly record struct TileOwner(ushort Raw)
{
    public const int MaxIndex = (1 << 14) - 1;

    public static TileOwner None => default;

    public TileOwnerKind Kind => (TileOwnerKind)(Raw >> 14);

    public int Index => Raw & MaxIndex;

    public static TileOwner Company(int id) => Create(TileOwnerKind.Company, id);

    public static TileOwner Town(int id) => Create(TileOwnerKind.Town, id);

    public static TileOwner Industry(int id) => Create(TileOwnerKind.Industry, id);

    public bool IsCompany(int id) => Kind == TileOwnerKind.Company && Index == id;

    private static TileOwner Create(TileOwnerKind kind, int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(index, MaxIndex);
        return new TileOwner((ushort)(((int)kind << 14) | index));
    }
}

/// <summary>
/// Dense per-tile layer of objects and their owners (research R7): one byte of kind and two bytes of
/// owner per tile, indexed like <see cref="Maps.MapData"/> (<c>x + z * width</c>). An empty tile has
/// kind <see cref="TileObjectKind.None"/> and owner <see cref="TileOwner.None"/>.
/// </summary>
public sealed class TileObjects : IStateHashable
{
    private readonly byte[] _kinds;
    private readonly ushort[] _owners;

    public TileObjects(int width, int depth)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(depth);
        Width = width;
        Depth = depth;
        _kinds = new byte[width * depth];
        _owners = new ushort[width * depth];
    }

    public int Width { get; }

    public int Depth { get; }

    public TileObjectKind KindAt(int index) => (TileObjectKind)_kinds[index];

    public TileOwner OwnerAt(int index) => new(_owners[index]);

    /// <summary>Places or removes an object; <see cref="TileObjectKind.None"/> always clears the owner.</summary>
    public void Set(int index, TileObjectKind kind, TileOwner owner)
    {
        _kinds[index] = (byte)kind;
        _owners[index] = kind == TileObjectKind.None ? (ushort)0 : owner.Raw;
    }

    public void AppendTo(StateHasher hasher)
    {
        ArgumentNullException.ThrowIfNull(hasher);
        hasher.Append<byte>(_kinds);
        hasher.Append<ushort>(_owners);
    }
}

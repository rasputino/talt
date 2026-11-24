namespace Talt.Core.Roads;

/// <summary>Per-tile road flags (data-model.md RoadLayer.Flags).</summary>
[Flags]
public enum RoadFeatures : byte
{
    None = 0,

    /// <summary>One end of a bridge: its long link (<see cref="RoadPiece.LongDir"/>, <see cref="RoadPiece.Span"/>) is the bridge.</summary>
    BridgeEnd = 1,

    /// <summary>One end of a tunnel: its long link is the tunnel.</summary>
    TunnelPortal = 2,

    /// <summary>A motorway carriageway tile with an exit or entry ramp on its outer side.</summary>
    Connector = 4,

    /// <summary>A bridge running along X passes above this tile.</summary>
    OverBridgeX = 8,

    /// <summary>A bridge running along Z passes above this tile.</summary>
    OverBridgeZ = 16,

    /// <summary>A tunnel running along X passes below this tile.</summary>
    UnderTunnelX = 32,

    /// <summary>A tunnel running along Z passes below this tile.</summary>
    UnderTunnelZ = 64,

    /// <summary>Flags that describe something above or below the tile, not the tile's own piece.</summary>
    Passing = OverBridgeX | OverBridgeZ | UnderTunnelX | UnderTunnelZ,

    /// <summary>The flags of a bridge or tunnel end.</summary>
    LongLinkEnd = BridgeEnd | TunnelPortal,
}

/// <summary>
/// Everything <see cref="RoadLayer"/> stores for one tile (data-model.md RoadPiece): the link mask (bit d =
/// connects towards <see cref="Dir8"/> d), the exit mask (bit d = travel towards d allowed, a subset of the
/// links), the road type (index + 1, 0 = none), flags, and for bridge and tunnel ends the long link: its
/// direction (<see cref="LongDir"/>, direction + 1, 0 = none) and its length in tiles (<see cref="Span"/>).
/// </summary>
public readonly record struct RoadPiece(byte Links, byte Exits, byte Type, RoadFeatures Flags, byte Span, byte LongDir)
{
    public static RoadPiece Empty => default;

    /// <summary>True when the tile holds a road piece of its own (not just a bridge passing above).</summary>
    public bool HasRoad => Type != 0;

    /// <summary>Road type index, or −1 for none.</summary>
    public int TypeIndex => Type - 1;

    public bool HasLink(int direction) => (Links & (1 << direction)) != 0;

    public bool HasExit(int direction) => (Exits & (1 << direction)) != 0;

    public int LinkCount => Dir8.Count(Links);

    /// <summary>The direction of the long link (bridge or tunnel end), or −1.</summary>
    public int LongLinkDirection => LongDir - 1;

    public bool IsBridgeEnd => (Flags & RoadFeatures.BridgeEnd) != 0;

    public bool IsTunnelPortal => (Flags & RoadFeatures.TunnelPortal) != 0;

    public bool IsConnector => (Flags & RoadFeatures.Connector) != 0;

    /// <summary>Only the flags of bridges or tunnels passing above or below (no own piece).</summary>
    public RoadPiece PassingOnly => new(0, 0, 0, Flags & RoadFeatures.Passing, 0, 0);

    /// <summary>The same piece with the passing flags of <paramref name="other"/> (keeps what passes above or below).</summary>
    public RoadPiece WithPassingOf(RoadPiece other) => this with { Flags = (Flags & ~RoadFeatures.Passing) | (other.Flags & RoadFeatures.Passing) };

    /// <summary>Packs the piece into 48 bits for hashing and undo comparisons.</summary>
    public long Pack() => Links | ((long)Exits << 8) | ((long)Type << 16) | ((long)Flags << 24) | ((long)Span << 32) | ((long)LongDir << 40);
}

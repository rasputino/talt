using System.Runtime.InteropServices;
using Talt.Core.Construction;

namespace Talt.Core.Vehicles;

/// <summary>Bit flags of <see cref="RoadVehicleState.Flags"/> (data-model.md RoadVehicle.Flags).</summary>
[Flags]
public enum RoadVehicleBits
{
    None = 0,

    /// <summary>Holds <see cref="RoadVehicleState.HeldJunction"/>.</summary>
    HoldsJunction = 1 << 0,

    /// <summary>Stopped at its stop place (unloading or loading).</summary>
    AtStop = 1 << 1,

    /// <summary>Broken down: crawls at a pulsing reduced speed (research R9).</summary>
    BrokenDown = 1 << 2,

    /// <summary>Stopped by the player.</summary>
    Stopped = 1 << 3,

    /// <summary>Inside a depot (not on any lane).</summary>
    InDepot = 1 << 4,

    /// <summary>Its route no longer matches the road and it waits for a new one.</summary>
    UrgentRoute = 1 << 5,

    /// <summary>Reported stuck (cleared when it moves).</summary>
    Stuck = 1 << 6,

    /// <summary>No route to its target exists; it waits and retries.</summary>
    NoRoute = 1 << 7,

    // Phase 1 outputs, cleared at the start of each tick's drive step.

    /// <summary>Reached its stop place this tick.</summary>
    ArrivedEvent = 1 << 8,

    /// <summary>Stands at a dead end's turning point and wants to turn around.</summary>
    DeadEndEvent = 1 << 9,

    /// <summary>Released its junction this tick.</summary>
    ReleasedEvent = 1 << 10,

    /// <summary>Moved to another lane this tick.</summary>
    LaneChanged = 1 << 11,

    /// <summary>Wants its junction (see <see cref="RoadVehicleState.Requested"/>).</summary>
    RequestEvent = 1 << 12,

    /// <summary>Its route did not match the road at a node this tick.</summary>
    RouteMismatchEvent = 1 << 13,

    /// <summary>The breakdown time ran out this tick.</summary>
    RecoveredEvent = 1 << 14,

    /// <summary>Events written by the drive step.</summary>
    Events = ArrivedEvent | DeadEndEvent | ReleasedEvent | LaneChanged | RequestEvent | RouteMismatchEvent | RecoveredEvent,
}

/// <summary>
/// Hot per-vehicle state read and written by the drive step (data-model.md RoadVehicle, research R7). Packed so one
/// vehicle's driving data sits in one cache line; all integers. The drive step reads the current array and writes a
/// second one, so every vehicle sees the previous tick of the others.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct RoadVehicleState
{
    /// <summary>Lane key <c>tile × 8 + direction</c>, or −1 inside a depot.</summary>
    public int Lane;

    /// <summary>Front position along the lane, units from the centre of the lane's tile (negative or past the link at dead ends).</summary>
    public int S;

    /// <summary>Units per tick.</summary>
    public int Speed;

    /// <summary>The model's top speed, units per tick.</summary>
    public int ModelSpeed;

    /// <summary>Units per tick².</summary>
    public int Accel;

    /// <summary>Units.</summary>
    public int Length;

    /// <summary><see cref="RoadVehicleBits"/>.</summary>
    public RoadVehicleBits Flags;

    /// <summary>Tile of the held junction, or −1.</summary>
    public int HeldJunction;

    /// <summary>Junction tile wanted this tick (with <see cref="RoadVehicleBits.RequestEvent"/>), or −1.</summary>
    public int Requested;

    /// <summary>Tick when the current junction wait started, or −1.</summary>
    public int WaitingSince;

    /// <summary>Ticks without moving while wanting to move.</summary>
    public int StillTicks;

    /// <summary>Next step of the route to use.</summary>
    public int RouteIndex;

    /// <summary>Tile of the stop piece or depot the vehicle drives to, or −1.</summary>
    public int TargetTile;

    /// <summary>Ticks of breakdown left (0 when running normally).</summary>
    public int BreakdownTicks;

    public readonly bool Has(RoadVehicleBits flag) => (Flags & flag) != 0;
}

/// <summary>
/// Look-up caches of a vehicle's lane and the lane after it (research R7): written only by the vehicle itself (in the
/// drive step or when it is placed on a lane), read only by it, so they live outside the double-buffered state. Pure
/// functions of the road layer, the lane and the route: derived data, never hashed.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct LaneCache
{
    /// <summary>Cached for the lane (set whenever <see cref="RoadVehicleState.Lane"/> changes): the tile it leads to.</summary>
    public int To;

    /// <summary>Cached for the lane: its link length, units.</summary>
    public int LaneLength;

    /// <summary>Cached for the lane: the road's speed limit with the ramp share, units per tick.</summary>
    public int RoadCap;

    /// <summary>Cached for the lane: link mask (low byte) and exit mask (second byte) of <see cref="To"/>.</summary>
    public int ToMasks;

    /// <summary>Derived look-up cache: the key of the lane after this one last time it was looked up, or −1.</summary>
    public int NextKey;

    /// <summary>Derived look-up cache: the lane index of <see cref="NextKey"/> (only cached once the lane exists).</summary>
    public int NextIndex;

    /// <summary>Road network version the lane caches were filled at (refilled when the roads change).</summary>
    public int CacheVersion;

    /// <summary>Derived look-up cache for <see cref="NextKey"/>: the tile it leads to.</summary>
    public int NextTo;

    /// <summary>Derived look-up cache for <see cref="NextKey"/>: its link length.</summary>
    public int NextLength;

    /// <summary>Derived look-up cache for <see cref="NextKey"/>: link and exit masks of <see cref="NextTo"/>.</summary>
    public int NextMasks;

}

/// <summary>
/// Tells construction where vehicles stand (research R12), so road links, stop pieces and depots are never taken from
/// under one.
/// </summary>
public interface IVehicleOccupancy
{
    /// <summary>True when the plan removes or changes something a vehicle stands on.</summary>
    bool Blocks(ConstructionPlan plan);
}

using System.Runtime.InteropServices;

namespace Talt.Core.Simulation;

/// <summary>
/// Hot per-vehicle simulation state, packed together so one cache line holds everything the tick
/// reads and writes for a vehicle (data-model.md VehicleFleet.*). All fields are integers.
/// </summary>
/// <remarks>
/// An earlier layout used one array per field; the SoA vs Arch benchmark (research R5) showed that
/// scattering seven arrays costs ~25 % compared with grouping the fields, so they are packed here.
/// Sequential layout with only <c>int</c> fields: 24 bytes, no padding, hashed as raw bytes.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
public struct VehicleState
{
    /// <summary>Line index (data-model.md VehicleFleet.Line).</summary>
    public int Line;

    /// <summary>Edge index within the line loop (data-model.md VehicleFleet.RouteIndex).</summary>
    public int RouteIndex;

    /// <summary>Units travelled along the current edge (data-model.md VehicleFleet.Distance).</summary>
    public int Distance;

    /// <summary>Units per tick (data-model.md VehicleFleet.Speed).</summary>
    public int Speed;

    /// <summary>Units per tick (data-model.md VehicleFleet.MaxSpeed).</summary>
    public int MaxSpeed;

    /// <summary>Ticks left waiting at a stop; 0 when moving (data-model.md VehicleFleet.DwellTicks).</summary>
    public int DwellTicks;
}

using System.Numerics;
using Talt.Core.Determinism;
using Talt.Core.Network;

namespace Talt.Core.Simulation;

/// <summary>
/// Rendering only: position and heading of a vehicle on the ground plane, in tiles.
/// </summary>
public readonly record struct VehiclePose(Vector2 Position, Vector2 Direction);

/// <summary>
/// Simulates vehicles running on transit lines over a <see cref="TransportNetwork"/>.
/// </summary>
/// <remarks>
/// <para>
/// A <b>line</b> is a closed loop of edges with two stops: the start of the route (index 0) and the
/// start of the way back (<c>returnIndex</c>). Vehicles accelerate, cruise, brake before each stop,
/// wait there for <see cref="DwellTicks"/> and continue.
/// </para>
/// <para>
/// State is stored in flat arrays of integer fixed-point values (distances in units, speeds in units
/// per tick, see <see cref="FixedMath"/>), so the result is bit-identical on every platform. The hot
/// fields of each vehicle are packed in one <see cref="VehicleState"/> for cache locality.
/// Vehicles occupy dense slots <c>0..VehicleCount-1</c>; each has a stable <c>VehicleId</c> that never
/// changes or gets reused. Removing a vehicle moves the last one into its slot.
/// </para>
/// <para>
/// Vehicles do not interact yet, so each one is updated independently and <see cref="Tick"/> runs in
/// parallel batches. It allocates nothing per vehicle, and parallel and sequential ticks give
/// identical results because all arithmetic is integer.
/// </para>
/// </remarks>
public sealed class VehicleFleet : IStateHashable
{
    /// <summary>Acceleration, units per tick² (1.5 tiles/s²).</summary>
    public static readonly int Acceleration = FixedMath.AccelerationFromTilesPerSecondSquared(1.5);

    /// <summary>Braking deceleration, units per tick² (2.5 tiles/s²).</summary>
    public static readonly int Deceleration = FixedMath.AccelerationFromTilesPerSecondSquared(2.5);

    /// <summary>Minimum speed while braking, so stops are always reached (0.5 tiles/s).</summary>
    public static readonly int CreepSpeed = FixedMath.SpeedFromTilesPerSecond(0.5);

    /// <summary>Highest maximum speed a vehicle may have (40 tiles/s).</summary>
    public static readonly int MaxAllowedSpeed = FixedMath.SpeedFromTilesPerSecond(40);

    /// <summary>Ticks a vehicle waits at each stop (3 seconds).</summary>
    public static readonly int DwellTicks = FixedMath.SecondsToTicks(3);

    private const int BatchSize = 4096;

    private readonly TransportNetwork _network;
    private readonly Action<int> _tickBatch;
    private readonly Dictionary<int, int> _slotById = [];

    // Lines: edges of all lines in one flat array.
    private int[] _lineEdges = new int[1024];
    private long[] _distanceToStop = new long[1024]; // From the end of each line edge to the next stop.
    private int _lineEdgeCount;
    private int[] _lineStart = new int[64];
    private int[] _lineLength = new int[64];
    private int[] _lineReturn = new int[64];

    // Vehicles: stable ids plus packed hot state, all integer (data-model.md VehicleFleet.*).
    private int[] _id = new int[1024];
    private VehicleState[] _state = new VehicleState[1024];

    private int _nextVehicleId = 1;
    private long _totalDistance;

    public VehicleFleet(TransportNetwork network)
    {
        ArgumentNullException.ThrowIfNull(network);
        _network = network;
        _tickBatch = TickBatch;
    }

    public TransportNetwork Network => _network;

    public int VehicleCount { get; private set; }

    public int LineCount { get; private set; }

    /// <summary>Total distance travelled by all vehicles, in units.</summary>
    public long TotalDistance => Interlocked.Read(ref _totalDistance);

    /// <summary>
    /// Adds a line made of an outbound route followed by the route back.
    /// </summary>
    /// <param name="outbound">Edges from the first stop to the second stop.</param>
    /// <param name="inbound">Edges from the second stop back to the first stop.</param>
    /// <returns>The new line's index.</returns>
    public int AddLine(ReadOnlySpan<int> outbound, ReadOnlySpan<int> inbound)
    {
        if (outbound.IsEmpty || inbound.IsEmpty)
        {
            throw new ArgumentException("Both directions of a line need at least one edge.");
        }

        var length = outbound.Length + inbound.Length;
        EnsureLineCapacity(_lineEdgeCount + length);
        var start = _lineEdgeCount;
        outbound.CopyTo(_lineEdges.AsSpan(start));
        inbound.CopyTo(_lineEdges.AsSpan(start + outbound.Length));
        ValidateLoop(_lineEdges.AsSpan(start, length));
        _lineEdgeCount += length;

        var returnIndex = outbound.Length;
        ComputeDistanceToStop(start, length, returnIndex);

        if (LineCount == _lineStart.Length)
        {
            Array.Resize(ref _lineStart, LineCount * 2);
            Array.Resize(ref _lineLength, LineCount * 2);
            Array.Resize(ref _lineReturn, LineCount * 2);
        }

        _lineStart[LineCount] = start;
        _lineLength[LineCount] = length;
        _lineReturn[LineCount] = returnIndex;
        return LineCount++;
    }

    /// <summary>Number of edges in a line (both directions).</summary>
    public int LineEdgeCount(int line) => _lineLength[line];

    /// <summary>Adds a vehicle at the start of an edge of a line, standing still.</summary>
    /// <param name="line">Line the vehicle runs on.</param>
    /// <param name="maxSpeed">Cruise speed, in units per tick (1 to <see cref="MaxAllowedSpeed"/>).</param>
    /// <param name="initialDwellTicks">Ticks to wait before departing.</param>
    /// <param name="startEdge">Index of the line edge where the vehicle starts (0 = first stop).</param>
    /// <returns>The new vehicle's stable id.</returns>
    public int AddVehicle(int line, int maxSpeed, int initialDwellTicks = 0, int startEdge = 0)
    {
        if ((uint)line >= (uint)LineCount)
        {
            throw new ArgumentOutOfRangeException(nameof(line));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxSpeed);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maxSpeed, MaxAllowedSpeed);
        ArgumentOutOfRangeException.ThrowIfNegative(initialDwellTicks);
        if ((uint)startEdge >= (uint)_lineLength[line])
        {
            throw new ArgumentOutOfRangeException(nameof(startEdge));
        }

        EnsureVehicleCapacity(VehicleCount + 1);

        var slot = VehicleCount++;
        var id = _nextVehicleId++;
        _id[slot] = id;
        _state[slot] = new VehicleState
        {
            Line = line,
            RouteIndex = startEdge,
            MaxSpeed = maxSpeed,
            DwellTicks = initialDwellTicks,
        };
        _slotById.Add(id, slot);
        return id;
    }

    /// <summary>Removes a vehicle. The last vehicle moves into its slot.</summary>
    /// <returns>False when no vehicle has this id.</returns>
    public bool RemoveVehicle(int vehicleId)
    {
        if (!_slotById.Remove(vehicleId, out var slot))
        {
            return false;
        }

        var last = VehicleCount - 1;
        if (slot != last)
        {
            _id[slot] = _id[last];
            _state[slot] = _state[last];
            _slotById[_id[slot]] = slot;
        }

        VehicleCount--;
        return true;
    }

    public bool ContainsVehicle(int vehicleId) => _slotById.ContainsKey(vehicleId);

    /// <summary>Id given to the most recently added vehicle (0 if none was ever added).</summary>
    public int LastCreatedVehicleId => _nextVehicleId - 1;

    /// <summary>Slot currently used by a vehicle, or -1.</summary>
    public int SlotOf(int vehicleId) => _slotById.GetValueOrDefault(vehicleId, -1);

    public int VehicleIdAt(int slot) => _id[slot];

    /// <summary>Advances every vehicle by one tick.</summary>
    /// <param name="parallel">Use all CPU cores. Results are identical either way.</param>
    public void Tick(bool parallel = true)
    {
        var batches = (VehicleCount + BatchSize - 1) / BatchSize;

        if (parallel && batches > 1)
        {
            Parallel.For(0, batches, _tickBatch);
        }
        else
        {
            for (var b = 0; b < batches; b++)
            {
                TickBatch(b);
            }
        }
    }

    /// <summary>Rendering only: pose of the vehicle in a slot, in tiles.</summary>
    public VehiclePose GetPose(int slot)
    {
        ref readonly var state = ref _state[slot];
        var edge = _lineEdges[_lineStart[state.Line] + state.RouteIndex];
        var from = _network.NodePositionTiles(_network.EdgeFrom(edge));
        var to = _network.NodePositionTiles(_network.EdgeTo(edge));
        var length = FixedMath.UnitsToTiles(_network.EdgeLength(edge));
        var direction = (to - from) / length;
        return new VehiclePose(from + direction * FixedMath.UnitsToTiles(state.Distance), direction);
    }

    /// <summary>Speed of the vehicle in a slot, in units per tick.</summary>
    public int GetSpeed(int slot) => _state[slot].Speed;

    public bool IsAtStop(int slot) => _state[slot].DwellTicks > 0;

    /// <summary>Rendering only: copies all vehicle poses into <paramref name="poses"/>.</summary>
    public void CopyPoses(Span<VehiclePose> poses)
    {
        if (poses.Length < VehicleCount)
        {
            throw new ArgumentException("The destination is smaller than the number of vehicles.", nameof(poses));
        }

        for (var v = 0; v < VehicleCount; v++)
        {
            poses[v] = GetPose(v);
        }
    }

    /// <summary>
    /// Rendering only: copies all vehicle poses into <paramref name="poses"/>, splitting the work over
    /// all cores for large fleets. Poses are read-only views, so order of work does not matter.
    /// </summary>
    public void CopyPoses(VehiclePose[] poses, bool parallel)
    {
        ArgumentNullException.ThrowIfNull(poses);
        if (!parallel || VehicleCount <= BatchSize)
        {
            CopyPoses(poses.AsSpan());
            return;
        }

        if (poses.Length < VehicleCount)
        {
            throw new ArgumentException("The destination is smaller than the number of vehicles.", nameof(poses));
        }

        var batches = (VehicleCount + BatchSize - 1) / BatchSize;
        Parallel.For(0, batches, batch =>
        {
            var end = Math.Min((batch + 1) * BatchSize, VehicleCount);
            for (var v = batch * BatchSize; v < end; v++)
            {
                poses[v] = GetPose(v);
            }
        });
    }

    /// <summary>Copies the vehicle id of every slot into <paramref name="ids"/>.</summary>
    public void CopyIds(Span<int> ids) => _id.AsSpan(0, VehicleCount).CopyTo(ids);

    public void AppendTo(StateHasher hasher)
    {
        ArgumentNullException.ThrowIfNull(hasher);
        hasher.Append<int>(_lineEdges.AsSpan(0, _lineEdgeCount));
        hasher.Append<long>(_distanceToStop.AsSpan(0, _lineEdgeCount));
        hasher.Append<int>(_lineStart.AsSpan(0, LineCount));
        hasher.Append<int>(_lineLength.AsSpan(0, LineCount));
        hasher.Append<int>(_lineReturn.AsSpan(0, LineCount));
        hasher.Append<int>(_id.AsSpan(0, VehicleCount));
        hasher.Append<VehicleState>(_state.AsSpan(0, VehicleCount));
        hasher.Append(_nextVehicleId);
        hasher.Append(TotalDistance);
    }

    private void TickBatch(int batch)
    {
        var end = Math.Min((batch + 1) * BatchSize, VehicleCount);
        var travelled = 0L;

        for (var v = batch * BatchSize; v < end; v++)
        {
            travelled += TickVehicle(v);
        }

        // Integer addition is associative, so the batch order does not change the total.
        Interlocked.Add(ref _totalDistance, travelled);
    }

    /// <returns>Distance travelled during this tick, in units.</returns>
    private long TickVehicle(int v)
    {
        ref var state = ref _state[v];
        if (state.DwellTicks > 0)
        {
            state.DwellTicks--;
            return 0;
        }

        var line = state.Line;
        var start = _lineStart[line];
        var index = state.RouteIndex;
        var edgeLength = _network.EdgeLength(_lineEdges[start + index]);

        // Brake when the stopping distance reaches the next stop, otherwise accelerate.
        var remaining = (long)edgeLength - state.Distance + _distanceToStop[start + index];
        var speed = FixedMath.BrakingDistance(state.Speed, Deceleration) >= remaining
            ? Math.Max(state.Speed - Deceleration, CreepSpeed)
            : Math.Min(state.Speed + Acceleration, state.MaxSpeed);

        if (speed >= remaining)
        {
            // Arrived: snap to the stop and wait.
            state.RouteIndex = index < _lineReturn[line] ? _lineReturn[line] : 0;
            state.Distance = 0;
            state.Speed = 0;
            state.DwellTicks = DwellTicks;
            return remaining;
        }

        var lineLength = _lineLength[line];
        var distance = state.Distance + speed;
        while (distance >= edgeLength)
        {
            distance -= edgeLength;
            index = index + 1 == lineLength ? 0 : index + 1;
            edgeLength = _network.EdgeLength(_lineEdges[start + index]);
        }

        state.RouteIndex = index;
        state.Distance = distance;
        state.Speed = speed;
        return speed;
    }

    private void ComputeDistanceToStop(int start, int length, int returnIndex)
    {
        // Walk backwards: the distance after edge i is 0 if edge i+1 starts at a stop,
        // otherwise the length of edge i+1 plus the distance after it.
        for (var i = length - 1; i >= 0; i--)
        {
            var next = i + 1 == length ? 0 : i + 1;
            _distanceToStop[start + i] = next == 0 || next == returnIndex
                ? 0
                : _network.EdgeLength(_lineEdges[start + next]) + _distanceToStop[start + next];
        }
    }

    private void ValidateLoop(ReadOnlySpan<int> edges)
    {
        for (var i = 0; i < edges.Length; i++)
        {
            var next = edges[(i + 1) % edges.Length];
            if (_network.EdgeTo(edges[i]) != _network.EdgeFrom(next))
            {
                throw new ArgumentException($"Line edges are not connected at position {i}.");
            }
        }
    }

    private void EnsureLineCapacity(int required)
    {
        if (required > _lineEdges.Length)
        {
            var size = Math.Max(required, _lineEdges.Length * 2);
            Array.Resize(ref _lineEdges, size);
            Array.Resize(ref _distanceToStop, size);
        }
    }

    private void EnsureVehicleCapacity(int required)
    {
        if (required > _id.Length)
        {
            var size = Math.Max(required, _id.Length * 2);
            Array.Resize(ref _id, size);
            Array.Resize(ref _state, size);
        }
    }
}

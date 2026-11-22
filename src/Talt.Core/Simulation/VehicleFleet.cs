using System.Numerics;
using Talt.Core.Network;

namespace Talt.Core.Simulation;

/// <summary>
/// Position and heading of a vehicle on the ground plane, in tiles.
/// </summary>
public readonly record struct VehiclePose(Vector2 Position, Vector2 Direction);

/// <summary>
/// Simulates vehicles running on transit lines over a <see cref="TransportNetwork"/>.
/// </summary>
/// <remarks>
/// <para>
/// A <b>line</b> is a closed loop of edges with two stops: the start of the route (index 0) and the
/// start of the way back (<c>returnIndex</c>). Vehicles accelerate, cruise, brake before each stop,
/// wait there for <see cref="DwellSeconds"/> and continue.
/// </para>
/// <para>
/// State is stored as a structure of arrays (one array per field), which keeps the per-tick loop
/// cache friendly. Vehicles do not interact yet, so each one is updated independently and the tick
/// runs in parallel batches. <see cref="Tick"/> allocates nothing per vehicle.
/// </para>
/// </remarks>
public sealed class VehicleFleet
{
    public const float Acceleration = 1.5f;  // tiles/s²
    public const float Deceleration = 2.5f;  // tiles/s²
    public const float DwellSeconds = 3f;
    private const float CreepSpeed = 0.5f;   // Minimum speed while braking, so stops are always reached.
    private const int BatchSize = 4096;

    private readonly TransportNetwork _network;
    private readonly Action<int> _tickBatch;

    // Lines: edges of all lines in one flat array.
    private int[] _lineEdges = new int[1024];
    private float[] _distanceToStop = new float[1024]; // From the end of each line edge to the next stop.
    private int _lineEdgeCount;
    private int[] _lineStart = new int[64];
    private int[] _lineLength = new int[64];
    private int[] _lineReturn = new int[64];

    // Vehicles: structure of arrays.
    private int[] _line = new int[1024];
    private int[] _routeIndex = new int[1024];
    private float[] _distance = new float[1024];
    private float[] _speed = new float[1024];
    private float[] _maxSpeed = new float[1024];
    private float[] _dwell = new float[1024];

    private float _tickDelta;
    private double _totalDistance;

    public VehicleFleet(TransportNetwork network)
    {
        ArgumentNullException.ThrowIfNull(network);
        _network = network;
        _tickBatch = TickBatch;
    }

    public int VehicleCount { get; private set; }

    public int LineCount { get; private set; }

    /// <summary>Total distance travelled by all vehicles, in tiles. Handy to check the simulation runs.</summary>
    public double TotalDistance => Volatile.Read(ref _totalDistance);

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
    /// <param name="maxSpeed">Cruise speed, in tiles per second.</param>
    /// <param name="initialDwell">Seconds to wait before departing.</param>
    /// <param name="startEdge">Index of the line edge where the vehicle starts (0 = first stop).</param>
    /// <returns>The new vehicle's index.</returns>
    public int AddVehicle(int line, float maxSpeed, float initialDwell = 0f, int startEdge = 0)
    {
        if ((uint)line >= (uint)LineCount)
        {
            throw new ArgumentOutOfRangeException(nameof(line));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxSpeed);
        if ((uint)startEdge >= (uint)_lineLength[line])
        {
            throw new ArgumentOutOfRangeException(nameof(startEdge));
        }

        EnsureVehicleCapacity(VehicleCount + 1);

        var v = VehicleCount++;
        _line[v] = line;
        _routeIndex[v] = startEdge;
        _distance[v] = 0f;
        _speed[v] = 0f;
        _maxSpeed[v] = maxSpeed;
        _dwell[v] = initialDwell;
        return v;
    }

    /// <summary>Advances the simulation by <paramref name="deltaSeconds"/>.</summary>
    /// <param name="deltaSeconds">Simulated time step.</param>
    /// <param name="parallel">Use all CPU cores. Single-threaded mode exists for benchmarks and tests.</param>
    public void Tick(float deltaSeconds, bool parallel = true)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(deltaSeconds);
        _tickDelta = deltaSeconds;
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

    public VehiclePose GetPose(int vehicle)
    {
        var edge = _lineEdges[_lineStart[_line[vehicle]] + _routeIndex[vehicle]];
        var from = _network.NodePosition(_network.EdgeFrom(edge));
        var to = _network.NodePosition(_network.EdgeTo(edge));
        var length = _network.EdgeLength(edge);
        var direction = (to - from) / length;
        return new VehiclePose(from + direction * _distance[vehicle], direction);
    }

    public float GetSpeed(int vehicle) => _speed[vehicle];

    public bool IsAtStop(int vehicle) => _dwell[vehicle] > 0f;

    /// <summary>Copies all vehicle poses into <paramref name="poses"/> (length ≥ VehicleCount).</summary>
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

    private void TickBatch(int batch)
    {
        var dt = _tickDelta;
        var end = Math.Min((batch + 1) * BatchSize, VehicleCount);
        var travelled = 0.0;

        for (var v = batch * BatchSize; v < end; v++)
        {
            travelled += TickVehicle(v, dt);
        }

        // Rarely contended: one interlocked add per batch, not per vehicle.
        AddDistance(travelled);
    }

    /// <returns>Distance travelled during this step.</returns>
    private float TickVehicle(int v, float dt)
    {
        if (_dwell[v] > 0f)
        {
            _dwell[v] -= dt;
            return 0f;
        }

        var line = _line[v];
        var start = _lineStart[line];
        var lineLength = _lineLength[line];
        var index = _routeIndex[v];
        var edgeLength = _network.EdgeLength(_lineEdges[start + index]);
        var distance = _distance[v];
        var speed = _speed[v];

        // Brake when the stopping distance reaches the next stop, otherwise accelerate.
        var remaining = edgeLength - distance + _distanceToStop[start + index];
        var brakingDistance = speed * speed / (2f * Deceleration);
        speed = brakingDistance >= remaining
            ? MathF.Max(speed - Deceleration * dt, CreepSpeed)
            : MathF.Min(speed + Acceleration * dt, _maxSpeed[v]);

        var move = speed * dt;
        if (move >= remaining)
        {
            // Arrived: snap to the stop and wait.
            _routeIndex[v] = index < _lineReturn[line] ? _lineReturn[line] : 0;
            _distance[v] = 0f;
            _speed[v] = 0f;
            _dwell[v] = DwellSeconds;
            return remaining;
        }

        distance += move;
        while (distance >= edgeLength)
        {
            distance -= edgeLength;
            index = index + 1 == lineLength ? 0 : index + 1;
            edgeLength = _network.EdgeLength(_lineEdges[start + index]);
        }

        _routeIndex[v] = index;
        _distance[v] = distance;
        _speed[v] = speed;
        return move;
    }

    private void AddDistance(double value)
    {
        double current;
        do
        {
            current = _totalDistance;
        }
        while (Interlocked.CompareExchange(ref _totalDistance, current + value, current) != current);
    }

    private void ComputeDistanceToStop(int start, int length, int returnIndex)
    {
        // Walk backwards: the distance after edge i is 0 if edge i+1 starts at a stop,
        // otherwise the length of edge i+1 plus the distance after it.
        for (var i = length - 1; i >= 0; i--)
        {
            var next = i + 1 == length ? 0 : i + 1;
            _distanceToStop[start + i] = next == 0 || next == returnIndex
                ? 0f
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
        if (required > _line.Length)
        {
            var size = Math.Max(required, _line.Length * 2);
            Array.Resize(ref _line, size);
            Array.Resize(ref _routeIndex, size);
            Array.Resize(ref _distance, size);
            Array.Resize(ref _speed, size);
            Array.Resize(ref _maxSpeed, size);
            Array.Resize(ref _dwell, size);
        }
    }
}

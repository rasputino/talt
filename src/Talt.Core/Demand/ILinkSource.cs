namespace Talt.Core.Demand;

/// <summary>
/// A transport mode's contribution to the cargo network (feature 013, research R3, contracts/demand-api.md): its lines
/// as ordered stops at stations, with the capacity and speed of the vehicles that run them. Road vehicles implement it;
/// rail (feature 015) and later modes register their own implementation with <see cref="CargoRouter.AddSource"/>, so
/// destination cargo travels on every mode through the same stations without changes to the router.
/// </summary>
public interface ILinkSource
{
    /// <summary>Changes whenever lines, stops, stop rules or the vehicles on lines change.</summary>
    int LinkVersion { get; }

    /// <summary>Reports every line that has vehicles, once per cargo they carry.</summary>
    void CollectLinks(LinkCollector collector);
}

/// <summary>
/// Receives lines from <see cref="ILinkSource"/>s: <see cref="BeginLine"/>, one <see cref="AddStop"/> per stop in
/// order, then <see cref="EndLine"/>. Storage is reused between collections (no allocation in steady state).
/// </summary>
public sealed class LinkCollector
{
    private int[] _lineCompany = new int[64];
    private int[] _lineCargo = new int[64];
    private int[] _lineTicksPerTileMilli = new int[64];
    private int[] _lineUnitsPerRound = new int[64];
    private int[] _lineStopStart = new int[64];
    private int[] _lineStopCount = new int[64];
    private int[] _stopStation = new int[256];
    private byte[] _stopFlags = new byte[256];
    private bool _open;

    /// <summary>Lines collected so far.</summary>
    public int LineCount { get; private set; }

    /// <summary>Stops collected so far.</summary>
    public int StopCount { get; private set; }

    /// <summary>Forgets every line.</summary>
    public void Clear()
    {
        LineCount = 0;
        StopCount = 0;
        _open = false;
    }

    /// <summary>
    /// Starts a line of a company carrying one cargo. <paramref name="ticksPerTileMilli"/> is how many thousandths of
    /// a tick its slowest vehicle needs per tile; <paramref name="unitsPerRound"/> is the capacity of all its vehicles
    /// for that cargo (units carried per round of the line).
    /// </summary>
    public void BeginLine(int company, int cargo, int ticksPerTileMilli, int unitsPerRound)
    {
        if (_open)
        {
            throw new InvalidOperationException("EndLine was not called.");
        }

        if (LineCount == _lineCompany.Length)
        {
            var size = LineCount * 2;
            Array.Resize(ref _lineCompany, size);
            Array.Resize(ref _lineCargo, size);
            Array.Resize(ref _lineTicksPerTileMilli, size);
            Array.Resize(ref _lineUnitsPerRound, size);
            Array.Resize(ref _lineStopStart, size);
            Array.Resize(ref _lineStopCount, size);
        }

        _lineCompany[LineCount] = company;
        _lineCargo[LineCount] = cargo;
        _lineTicksPerTileMilli[LineCount] = Math.Max(1, ticksPerTileMilli);
        _lineUnitsPerRound[LineCount] = Math.Max(0, unitsPerRound);
        _lineStopStart[LineCount] = StopCount;
        _lineStopCount[LineCount] = 0;
        _open = true;
    }

    /// <summary>Adds the next stop: a station, and whether cargo may board (<paramref name="loads"/>) or leave (<paramref name="unloads"/>) there.</summary>
    public void AddStop(int stationId, bool loads, bool unloads)
    {
        if (!_open)
        {
            throw new InvalidOperationException("BeginLine was not called.");
        }

        if (StopCount == _stopStation.Length)
        {
            Array.Resize(ref _stopStation, StopCount * 2);
            Array.Resize(ref _stopFlags, StopCount * 2);
        }

        _stopStation[StopCount] = stationId;
        _stopFlags[StopCount] = (byte)((loads ? 1 : 0) | (unloads ? 2 : 0));
        StopCount++;
        _lineStopCount[LineCount]++;
    }

    public void EndLine()
    {
        if (!_open)
        {
            throw new InvalidOperationException("BeginLine was not called.");
        }

        _open = false;
        LineCount++;
    }

    public int CompanyOf(int line) => _lineCompany[line];

    public int CargoOf(int line) => _lineCargo[line];

    public int TicksPerTileMilliOf(int line) => _lineTicksPerTileMilli[line];

    public int UnitsPerRoundOf(int line) => _lineUnitsPerRound[line];

    public int FirstStopOf(int line) => _lineStopStart[line];

    public int StopCountOf(int line) => _lineStopCount[line];

    public int StationOf(int stop) => _stopStation[stop];

    public bool Loads(int stop) => (_stopFlags[stop] & 1) != 0;

    public bool Unloads(int stop) => (_stopFlags[stop] & 2) != 0;
}

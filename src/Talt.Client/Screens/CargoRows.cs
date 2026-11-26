using System.Globalization;
using System.Text;
using Talt.Client.Localization;
using Talt.Client.Ui;
using Talt.Client.Ui.Widgets;
using Talt.Core.Stations;
using Talt.Core.Towns;

namespace Talt.Client.Screens;

/// <summary>
/// Collapsible waiting-cargo rows (feature 013 hand test): a summary line per cargo ("15 passengers waiting") with a
/// + / − button that shows where they want to go, one line per destination town. Used by the station window (the
/// station's group) and the town window (the player's stations named after the town). Figures come from a
/// <see cref="WaitingView"/> refreshed when the window's version changes (once a day), not every frame.
/// </summary>
internal static class CargoRows
{
    /// <summary>Most destination lines shown when expanded.</summary>
    public const int MaxDestinationLines = 12;

    /// <summary>Adds one row per cargo of a station's group: its figures and, for destination cargo, the breakdown.</summary>
    public static void AddStation(StackPanel content, StationRegistry stations, TownRegistry? towns, int stationId, Func<long> version)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(stations);
        var view = new WaitingView(stations, version, [stationId]);
        for (var c = 0; c < stations.Content.CargoCount; c++)
        {
            var cargo = c;
            AddRow(content, view, cargo, version,
                () => view.Refresh().HasLine(cargo),
                s => StationLine(s, stations, view, cargo),
                s => Breakdown(s, stations, towns, view, cargo));
        }
    }

    /// <summary>Adds the town's waiting passengers and mail at the player's stations named after it.</summary>
    public static void AddTown(StackPanel content, StationRegistry stations, TownRegistry towns, int town, int companyId, Func<long> version)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(stations);
        ArgumentNullException.ThrowIfNull(towns);
        var view = new WaitingView(stations, version, () => StationsOfTown(stations, town, companyId));
        if (stations.Demand is not { } demand)
        {
            return;
        }

        foreach (var rule in demand.Cargo)
        {
            if (rule.Source != Talt.Core.Demand.DemandSource.Buildings)
            {
                continue;
            }

            var cargo = rule.CargoIndex;
            AddRow(content, view, cargo, version,
                () => view.Refresh().Waiting(cargo) > 0,
                s => s.Format("info.town.waiting", ("amount", view.Waiting(cargo).ToString("N0", s.Culture)), ("cargo", s.Get(stations.Content.Cargo(cargo).NameKey))),
                s => Breakdown(s, stations, towns, view, cargo));
        }
    }

    private static void AddRow(StackPanel content, WaitingView view, int cargo, Func<long> version, Func<bool> visible, Func<Strings, string> summary, Func<Strings, string> details)
    {
        var expanded = false;
        var block = content.Add(new StackPanel(Orientation.Vertical) { Spacing = 0, VisibleWhen = visible });
        var row = block.Add(new StackPanel(Orientation.Horizontal) { Spacing = 4 });
        row.Add(new Button(TextRef.Dynamic(() => expanded ? 1 : 0, _ => expanded ? "−" : "+"), () => expanded = !expanded)
        {
            Style = ButtonStyle.Flat,
            MinWidth = 24,
            Tooltip = TextRef.Of("info.cargo.expand.tooltip"),
            VisibleWhen = () => view.Refresh().HasBreakdown(cargo),
        });
        row.Add(new Spacer { MinWidth = 24, VisibleWhen = () => !view.Refresh().HasBreakdown(cargo) });
        row.Add(new Label(TextRef.Dynamic(version, summary)) { Wrap = true, Width = 350, Margin = new Thickness(0, 4, 0, 0) });
        block.Add(new Label(TextRef.Dynamic(() => version() ^ (expanded ? 1L << 62 : 0), details))
        {
            Wrap = true,
            Width = 340,
            Margin = new Thickness(28, 0, 0, 4),
            Color = TextColor.Muted,
            VisibleWhen = () => expanded && view.Refresh().HasBreakdown(cargo),
        });
    }

    /// <summary>The station window's line for one cargo: waiting, rating and last month's figures.</summary>
    public static string StationLine(Strings s, StationRegistry stations, WaitingView view, int cargo)
    {
        ArgumentNullException.ThrowIfNull(s);
        ArgumentNullException.ThrowIfNull(stations);
        ArgumentNullException.ThrowIfNull(view);
        if (view.Refresh().LineOf(cargo) is not { } line)
        {
            return string.Empty;
        }

        var name = s.Get(stations.Content.Cargo(cargo).NameKey);
        var flags = (line.Received ? s.Get("info.station.cargo.receives") : string.Empty)
            + (line.Received && line.Accepted ? " · " : string.Empty)
            + (line.Accepted ? s.Get("info.station.cargo.accepts") : string.Empty);
        return s.Format("info.station.cargo.line",
                ("cargo", name),
                ("flags", flags),
                ("waiting", line.Waiting.ToString("N0", s.Culture)),
                ("rating", line.Served ? s.Format("info.station.cargo.rating", ("percent", (line.Rating / 10).ToString(CultureInfo.InvariantCulture))) : s.Get("info.station.cargo.notServed")))
            + "\n"
            + s.Format("info.station.cargo.month",
                ("received", line.ReceivedLastMonth.ToString("N0", s.Culture)),
                ("pickedUp", line.PickedUpLastMonth.ToString("N0", s.Culture)),
                ("delivered", line.DeliveredLastMonth.ToString("N0", s.Culture)),
                ("days", line.DaysSincePickup < 0 ? "—" : line.DaysSincePickup.ToString(CultureInfo.InvariantCulture)));
    }

    /// <summary>One line per destination town (largest first), then the units with no fixed destination.</summary>
    public static string Breakdown(Strings s, StationRegistry stations, TownRegistry? towns, WaitingView view, int cargo)
    {
        ArgumentNullException.ThrowIfNull(s);
        ArgumentNullException.ThrowIfNull(view);
        var list = view.Refresh().Destinations(cargo);
        var builder = new StringBuilder();
        var shown = 0;
        foreach (var entry in list)
        {
            if (shown == MaxDestinationLines)
            {
                builder.Append('\n');
                builder.Append(s.Format("info.cargo.destination.more", ("count", (list.Count - shown).ToString(CultureInfo.InvariantCulture))));
                break;
            }

            if (builder.Length > 0)
            {
                builder.Append('\n');
            }

            var town = towns is not null && (uint)entry.Town < (uint)towns.Count ? towns.NameOf(entry.Town) : "?";
            var amount = entry.Amount.ToString("N0", s.Culture);
            builder.Append(entry.NextGroupKey < 0
                ? s.Format("info.cargo.destination.noRoute", ("amount", amount), ("town", town))
                : s.Format("info.cargo.destination", ("amount", amount), ("town", town), ("next", StationWindow.Name(s, stations, towns, entry.NextGroupKey))));
            shown++;
        }

        var any = view.AnyDestination(cargo);
        if (any > 0)
        {
            if (builder.Length > 0)
            {
                builder.Append('\n');
            }

            builder.Append(s.Format("info.cargo.destination.any", ("amount", any.ToString("N0", s.Culture))));
        }

        return builder.ToString();
    }

    /// <summary>The player's stations named after a town (one per group is enough: waiting cargo is per group).</summary>
    private static IEnumerable<int> StationsOfTown(StationRegistry stations, int town, int companyId)
    {
        var groups = new HashSet<int>();
        for (var slot = 0; slot < stations.Count; slot++)
        {
            if (stations.OwnerAt(slot) == companyId && stations.NameAt(slot).Town == town && groups.Add(stations.GroupOf(stations.IdAt(slot))))
            {
                yield return stations.IdAt(slot);
            }
        }
    }
}

/// <summary>Waiting cargo of one or more stations' groups by cargo and destination, recomputed when the version changes.</summary>
internal sealed class WaitingView
{
    private readonly StationRegistry _stations;
    private readonly Func<long> _version;
    private readonly Func<IEnumerable<int>> _members;
    private readonly Dictionary<int, List<DestinationAmount>> _destinations = [];
    private readonly Dictionary<int, StationCargoInfo> _lines = [];
    private readonly Dictionary<int, int> _waiting = [];
    private readonly List<DestinationAmount> _scratch = [];
    private long _seen = long.MinValue;

    public WaitingView(StationRegistry stations, Func<long> version, int[] stationIds)
        : this(stations, version, () => stationIds)
    {
    }

    public WaitingView(StationRegistry stations, Func<long> version, Func<IEnumerable<int>> members)
    {
        _stations = stations;
        _version = version;
        _members = members;
    }

    /// <summary>Recomputes when the version changed; returns itself.</summary>
    public WaitingView Refresh()
    {
        var version = _version();
        if (version == _seen)
        {
            return this;
        }

        _seen = version;
        _destinations.Clear();
        _lines.Clear();
        _waiting.Clear();
        foreach (var station in _members())
        {
            if (!_stations.TryGetStation(station, out var info))
            {
                continue;
            }

            foreach (var line in info.Cargo)
            {
                _lines[line.Cargo] = line;
                _waiting[line.Cargo] = _waiting.GetValueOrDefault(line.Cargo) + line.Waiting;
                if (_stations.Demand?.IsDirected(line.Cargo) != true)
                {
                    continue;
                }

                _stations.DestinationWaiting(station, line.Cargo, _scratch);
                if (!_destinations.TryGetValue(line.Cargo, out var list))
                {
                    list = [];
                    _destinations[line.Cargo] = list;
                }

                foreach (var entry in _scratch)
                {
                    var i = list.FindIndex(e => e.Town == entry.Town && e.NextGroupKey == entry.NextGroupKey);
                    if (i >= 0)
                    {
                        list[i] = list[i] with { Amount = list[i].Amount + entry.Amount };
                    }
                    else
                    {
                        list.Add(entry);
                    }
                }
            }
        }

        foreach (var list in _destinations.Values)
        {
            list.Sort((a, b) => b.Amount != a.Amount ? b.Amount.CompareTo(a.Amount) : a.Town.CompareTo(b.Town));
        }

        return this;
    }

    public bool HasLine(int cargo) => _lines.ContainsKey(cargo);

    public StationCargoInfo? LineOf(int cargo) => _lines.TryGetValue(cargo, out var line) ? line : null;

    /// <summary>Units waiting (all destinations).</summary>
    public int Waiting(int cargo) => _waiting.GetValueOrDefault(cargo);

    public IReadOnlyList<DestinationAmount> Destinations(int cargo) =>
        _destinations.TryGetValue(cargo, out var list) ? list : [];

    /// <summary>Units of a destination cargo waiting without a fixed destination (groups on no line, feature 010).</summary>
    public int AnyDestination(int cargo)
    {
        var directed = 0;
        foreach (var entry in Destinations(cargo))
        {
            directed += entry.Amount;
        }

        return Math.Max(0, Waiting(cargo) - directed);
    }

    /// <summary>True when there is something to list under the + button.</summary>
    public bool HasBreakdown(int cargo) => Destinations(cargo).Count > 0;
}

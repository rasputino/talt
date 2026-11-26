using System.Globalization;
using System.Text;
using Talt.Client.Localization;
using Talt.Client.Ui;
using Talt.Client.Ui.Widgets;
using Talt.Client.Ui.Windows;
using Talt.Core.Construction;
using Talt.Core.Content;
using Talt.Core.Stations;
using Talt.Core.Towns;

namespace Talt.Client.Screens;

/// <summary>
/// The station window (feature 010, US7, FR-025): name, kind and warehouse group, waiting cargo with ratings and
/// last month's activity per cargo, a catchment highlight toggle and renaming. Figures update every day. Town
/// names are proper nouns and never translated; a custom name is shown as the player typed it.
/// </summary>
internal static class StationWindow
{
    /// <summary>The window title: the station's name.</summary>
    public static TextRef Title(StationRegistry stations, TownRegistry? towns, int stationId) =>
        TextRef.Dynamic(() => stations.Version, s => Name(s, stations, towns, stationId));

    /// <summary>A station's display name (research R11), or the generic title when it is gone.</summary>
    public static string Name(Strings s, StationRegistry stations, TownRegistry? towns, int stationId)
    {
        ArgumentNullException.ThrowIfNull(s);
        ArgumentNullException.ThrowIfNull(stations);
        var slot = stations.SlotOf(stationId);
        if (slot < 0)
        {
            return s.Get("info.station.title");
        }

        if (stations.CustomNameAt(slot) is { } custom)
        {
            return custom;
        }

        var (town, kind, place, number) = stations.NameAt(slot);
        var townName = towns is not null && (uint)town < (uint)towns.Count ? towns.NameOf(town) : s.Get("station.name.noTown");
        var key = kind switch
        {
            StationNameKind.BusStop => "station.name.busStop",
            StationNameKind.TruckStop => "station.name.truckStop",
            _ => $"station.name.warehouse.{place.ToString().ToLowerInvariant()}",
        };
        var name = s.Format(key, ("town", townName));
        return number > 1 ? s.Format("station.name.numbered", ("name", name), ("number", number.ToString(CultureInfo.InvariantCulture))) : name;
    }

    /// <summary>Localized, comma-separated cargo names of a cargo bit set ("nothing" when empty).</summary>
    public static string CargoList(Strings s, GameContent content, ulong mask)
    {
        ArgumentNullException.ThrowIfNull(s);
        ArgumentNullException.ThrowIfNull(content);
        if (mask == 0)
        {
            return s.Get("station.cargo.none");
        }

        var builder = new StringBuilder();
        for (var c = 0; c < content.CargoCount; c++)
        {
            if ((mask & (1UL << c)) == 0)
            {
                continue;
            }

            if (builder.Length > 0)
            {
                builder.Append(", ");
            }

            builder.Append(s.Get(content.Cargo(c).NameKey));
        }

        return builder.ToString();
    }

    /// <summary>Adds the station's lines and controls to a window's content.</summary>
    public static void Add(StackPanel content, UiWindow window, StationRegistry stations, int stationId, GameSession session)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(stations);
        ArgumentNullException.ThrowIfNull(session);
        long Version() => (session.Simulation.CurrentTick / Talt.Core.Simulation.GameClock.TicksPerDay) ^ ((long)stations.Version << 32);
        if (stations.SlotOf(stationId) < 0)
        {
            content.Add(new Label(TextRef.Of("info.station.gone")) { Color = TextColor.Muted });
            return;
        }

        content.Add(new Label(TextRef.Dynamic(Version, s => Kind(s, stations, stationId))));
        content.Add(new Label(TextRef.Dynamic(Version, s => Group(s, stations, stationId))) { Color = TextColor.Muted });
        // One row per cargo; destination cargo opens with + to show where it wants to go (feature 013).
        content.Add(new Label(TextRef.Of("info.station.cargo.none")) { Color = TextColor.Muted, VisibleWhen = () => !HasCargo(stations, stationId) });
        CargoRows.AddStation(content, stations, session.Simulation.Towns, stationId, Version);

        var buttons = content.Add(new StackPanel(Orientation.Horizontal) { Spacing = 6 });
        buttons.Add(new Button(TextRef.Of("info.station.catchment"), () => session.ToggleCatchment(stationId))
        {
            IsSelected = () => session.CatchmentStationId == stationId,
            Tooltip = TextRef.Of("info.station.catchment.tooltip"),
        });

        var slot = stations.SlotOf(stationId);
        if (stations.OwnerAt(slot) == session.Construction.Player.Id)
        {
            var rename = content.Add(new StackPanel(Orientation.Horizontal) { Spacing = 6 });
            var input = rename.Add(new TextInput(stations.CustomNameAt(slot) ?? string.Empty, StationRegistry.MaxNameLength) { MinWidth = 220 });
            rename.Add(new Button(TextRef.Of("info.station.rename"), () =>
                session.Submit(new RenameStationCommand(session.Construction.Player.Id, stationId, input.Text)))
            {
                Tooltip = TextRef.Of("info.station.rename.tooltip"),
            });
        }

        window.Closed += _ => session.ClearCatchment(stationId);
    }

    public static string Kind(Strings s, StationRegistry stations, int stationId)
    {
        var slot = stations.SlotOf(stationId);
        if (slot < 0)
        {
            return s.Get("info.station.gone");
        }

        if (stations.KindAt(slot) == StationKind.Warehouse)
        {
            var type = stations.Rules.Warehouses[stations.WarehouseTypeAt(slot)];
            return s.Format("info.station.warehouse", ("size", s.Get(type.NameKey)), ("radius", type.InfluenceRadius.ToString(CultureInfo.InvariantCulture)));
        }

        var bus = (stations.CategoriesAt(slot) & (1 << (int)CargoCategory.Passenger)) != 0;
        var truck = (stations.CategoriesAt(slot) & ~(1 << (int)CargoCategory.Passenger)) != 0;
        var key = bus && truck ? "info.station.kind.both" : bus ? "info.station.kind.bus" : "info.station.kind.truck";
        return s.Format(key, ("pieces", stations.TilesAt(slot).Count.ToString(CultureInfo.InvariantCulture)));
    }

    public static string Group(Strings s, StationRegistry stations, int stationId)
    {
        var group = stations.GroupOf(stationId);
        if (group < 0)
        {
            return string.Empty;
        }

        var count = stations.StationsInGroup(group).Length;
        return count > 1
            ? s.Format("info.station.group", ("count", count.ToString(CultureInfo.InvariantCulture)))
            : s.Get("info.station.group.none");
    }

    /// <summary>True when the station's window has at least one cargo line (cheap: no snapshot).</summary>
    private static bool HasCargo(StationRegistry stations, int stationId) =>
        stations.SuppliesMask(stationId) != 0 || stations.AcceptsMask(stationId) != 0 || stations.GroupOf(stationId) >= 0 && HasServed(stations, stationId);

    private static bool HasServed(StationRegistry stations, int stationId)
    {
        for (var c = 0; c < stations.Content.CargoCount; c++)
        {
            if (stations.IsServed(stationId, c) || stations.WaitingAt(stationId, c) > 0)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>One line per cargo: waiting (any destination until feature 013), rating and last month's figures.</summary>
    public static string CargoTable(Strings s, StationRegistry stations, int stationId)
    {
        if (!stations.TryGetStation(stationId, out var info))
        {
            return s.Get("info.station.gone");
        }

        if (info.Cargo.Count == 0)
        {
            return s.Get("info.station.cargo.none");
        }

        var builder = new StringBuilder();
        foreach (var line in info.Cargo)
        {
            if (builder.Length > 0)
            {
                builder.Append('\n');
            }

            var name = s.Get(stations.Content.Cargo(line.Cargo).NameKey);
            var flags = (line.Received ? s.Get("info.station.cargo.receives") : string.Empty)
                + (line.Received && line.Accepted ? " · " : string.Empty)
                + (line.Accepted ? s.Get("info.station.cargo.accepts") : string.Empty);
            builder.Append(s.Format("info.station.cargo.line",
                ("cargo", name),
                ("flags", flags),
                ("waiting", line.Waiting.ToString("N0", s.Culture)),
                ("rating", line.Served ? s.Format("info.station.cargo.rating", ("percent", (line.Rating / 10).ToString(CultureInfo.InvariantCulture))) : s.Get("info.station.cargo.notServed"))));
            builder.Append('\n');
            builder.Append(s.Format("info.station.cargo.month",
                ("received", line.ReceivedLastMonth.ToString("N0", s.Culture)),
                ("pickedUp", line.PickedUpLastMonth.ToString("N0", s.Culture)),
                ("delivered", line.DeliveredLastMonth.ToString("N0", s.Culture)),
                ("days", line.DaysSincePickup < 0 ? "—" : line.DaysSincePickup.ToString(CultureInfo.InvariantCulture))));
        }

        _ = Company.PlayerId;
        return builder.ToString();
    }
}

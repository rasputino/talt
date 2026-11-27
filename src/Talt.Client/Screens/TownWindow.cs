using System.Globalization;
using System.Text;
using Talt.Client.Localization;
using Talt.Client.Ui;
using Talt.Client.Ui.Widgets;
using Talt.Core.Construction;
using Talt.Core.Content;
using Talt.Core.Demand;
using Talt.Core.Towns;

namespace Talt.Client.Screens;

/// <summary>
/// The town window (feature 008, US3, FR-023): name, population, buildings, last month's passengers and mail
/// generated and transported, accepted cargo, the rating of the player's company and the growth state. Texts
/// update when the month ends (the simulation tick changes); the name is a proper noun and is never translated.
/// </summary>
internal static class TownWindow
{
    /// <summary>The window title: the town's name.</summary>
    public static TextRef Title(TownRegistry towns, int town) =>
        (uint)town < (uint)towns.Count ? TextRef.Dynamic(() => 0, _ => towns.NameOf(town)) : TextRef.Of("info.town.title");

    /// <summary>Adds the town's figures, gauges and demand rows to a window's content.</summary>
    public static void Add(StackPanel content, TownRegistry towns, int town, Func<long> version, DestinationTables? demand = null, int mailCargo = -1, Talt.Core.Stations.StationRegistry? stations = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(towns);
        if ((uint)town >= (uint)towns.Count)
        {
            content.Add(new Label(TextRef.Of("info.town.title")) { Color = TextColor.Muted });
            return;
        }

        content.Add(new Label(TextRef.Dynamic(version, s => Population(s, towns, town)), TextSize.Title));
        content.Add(new Label(TextRef.Dynamic(version, s => Buildings(s, towns, town))) { Color = TextColor.Muted });
        content.Add(new Label(TextRef.Dynamic(version, s => Accepts(s, towns, town))) { Color = TextColor.Muted });

        // The town snapshot is read again only when the version changes (it is not free: steady frames must not allocate).
        var infoVersion = long.MinValue;
        TownInfo info = default;
        TownInfo Current()
        {
            if (version() is var v && v != infoVersion)
            {
                infoVersion = v;
                towns.TryGetTown(town, out info);
            }

            return info;
        }

        // Gauges: last month's transported share, the company's rating (mark: needed to build stations) and growth.
        content.Add(InfoRows.Section("info.town.section.service"));
        InfoRows.AddMeter(content, TextRef.Of("info.town.row.passengers"),
            new Meter(() => Current() is var i ? InfoRows.Share(i.PassengersTransportedLastMonth, i.PassengersGeneratedLastMonth) : 0f),
            TextRef.Dynamic(version, s => Current() is var i ? InfoRows.Ratio(s, i.PassengersTransportedLastMonth, i.PassengersGeneratedLastMonth) : string.Empty),
            TextRef.Dynamic(version, s => Passengers(s, towns, town)));
        InfoRows.AddMeter(content, TextRef.Of("info.town.row.mail"),
            new Meter(() => Current() is var i ? InfoRows.Share(i.MailTransportedLastMonth, i.MailGeneratedLastMonth) : 0f),
            TextRef.Dynamic(version, s => Current() is var i ? InfoRows.Ratio(s, i.MailTransportedLastMonth, i.MailGeneratedLastMonth) : string.Empty),
            TextRef.Dynamic(version, s => Mail(s, towns, town)));
        InfoRows.AddMeter(content, TextRef.Of("info.town.row.rating"),
            new Meter(() => towns.RatingOf(town, Company.PlayerId) / (float)TownRatingRules.Max)
            {
                Marker = () => towns.Rules.Ratings.StationMinimum / (float)TownRatingRules.Max,
            },
            TextRef.Dynamic(version, s => s.Get($"town.rating.{towns.BandOf(towns.RatingOf(town, Company.PlayerId)).ToString().ToLowerInvariant()}")),
            TextRef.Dynamic(version, s => Rating(s, towns, town, Company.PlayerId) + "\n" + s.Get("info.town.rating.marker")));
        InfoRows.AddMeter(content, TextRef.Of("info.town.row.growth"),
            new Meter(() => towns.ServiceOf(town) / 1000f),
            TextRef.Dynamic(version, s => InfoRows.Percent(s, towns.ServiceOf(town))),
            TextRef.Dynamic(version, s => Growth(s, towns, town)));

        if (stations is not null)
        {
            // Feature 013: passengers and mail waiting at the player's stations here, + for their destinations.
            CargoRows.AddTown(content, stations, towns, town, Company.PlayerId, version);
        }

        if (demand is not null)
        {
            AddDemand(content, towns, demand, town, mailCargo, version, stations?.Content);
        }
    }

    /// <summary>Feature 013 (FR-022) as rows: per cargo, the largest destinations with a gauge of the share carried.</summary>
    private static void AddDemand(StackPanel content, TownRegistry towns, DestinationTables demand, int town, int mailCargo, Func<long> version, GameContent? cargoContent)
    {
        var view = new TownDemandView(towns, demand, town, mailCargo, version);
        var rules = demand.Rules;
        for (var r = 0; r < rules.Cargo.Count; r++)
        {
            var rule = r;
            if (rules.Cargo[r].Source != DemandSource.Buildings || demand.RowOf(r, town) < 0)
            {
                continue;
            }

            var isMail = rules.Cargo[r].CargoIndex == mailCargo;
            var header = content.Add(new StackPanel(Orientation.Horizontal) { Spacing = 6, Margin = new Thickness(0, 4, 0, 0), VisibleWhen = () => view.Refresh().Count(rule) > 0 });
            if (cargoContent is not null)
            {
                header.Add(InfoRows.Badge(cargoContent, rules.Cargo[r].CargoIndex));
            }

            header.Add(new Label(TextRef.Of(isMail ? "info.town.demand.mail" : "info.town.demand.passengers"), TextSize.Title));
            for (var k = 0; k < MaxDestinationLines; k++)
            {
                var index = k;
                var row = InfoRows.AddMeter(content,
                    TextRef.Dynamic(version, s => view.Refresh().Count(rule) <= index ? string.Empty
                        : view.Town(rule, index) == town ? s.Get("info.town.demand.ownTown") : towns.NameOf(view.Town(rule, index))),
                    new Meter(() => view.Refresh().Count(rule) > index ? view.Served(rule, index) / 100f : 0f) { FixedColor = cargoContent is null ? UiColor.White : InfoRows.CargoColor(cargoContent, rules.Cargo[rule].CargoIndex) },
                    TextRef.Dynamic(version, s => view.Refresh().Count(rule) <= index ? string.Empty
                        : s.Format("info.town.demand.row", ("amount", view.Wanted(rule, index).ToString("N0", s.Culture)), ("served", view.Served(rule, index).ToString(CultureInfo.InvariantCulture)))),
                    TextRef.Of("info.town.demand.tooltip"));
                row.VisibleWhen = () => view.Refresh().Count(rule) > index;
            }
        }

        content.Add(new Label(TextRef.Dynamic(version, s => Goods(s, demand, town))) { Wrap = true, Width = 380, Color = TextColor.Muted, VisibleWhen = () => view.Refresh().GoodsSources > 0 });
    }

    private static TownInfo Info(TownRegistry towns, int town)
    {
        towns.TryGetTown(town, out var info);
        return info;
    }

    public static string Population(Strings s, TownRegistry towns, int town) =>
        s.Format("info.town.population", ("population", towns.PopulationOf(town).ToString("N0", s.Culture)));

    public static string Buildings(Strings s, TownRegistry towns, int town) =>
        s.Format("info.town.buildings", ("count", towns.BuildingCountOf(town).ToString("N0", s.Culture)), ("pattern", s.Get($"info.town.pattern.{towns.PatternOf(town).ToString().ToLowerInvariant()}")));

    public static string Passengers(Strings s, TownRegistry towns, int town)
    {
        towns.TryGetTown(town, out var info);
        return s.Format("info.town.passengers",
            ("transported", info.PassengersTransportedLastMonth.ToString("N0", s.Culture)),
            ("generated", info.PassengersGeneratedLastMonth.ToString("N0", s.Culture)));
    }

    public static string Mail(Strings s, TownRegistry towns, int town)
    {
        towns.TryGetTown(town, out var info);
        return s.Format("info.town.mail",
            ("transported", info.MailTransportedLastMonth.ToString("N0", s.Culture)),
            ("generated", info.MailGeneratedLastMonth.ToString("N0", s.Culture)));
    }

    public static string Accepts(Strings s, TownRegistry towns, int town)
    {
        var goods = towns.Accepts(town, food: false);
        var food = towns.Accepts(town, food: true);
        if (!goods && !food)
        {
            return s.Get("info.town.accepts.none");
        }

        var names = goods && food ? $"{s.Get("cargo.goods")}, {s.Get("cargo.food")}" : s.Get(goods ? "cargo.goods" : "cargo.food");
        return s.Format("info.town.accepts", ("cargos", names));
    }

    public static string Rating(Strings s, TownRegistry towns, int town, int companyId)
    {
        var rating = towns.RatingOf(town, companyId);
        var band = towns.BandOf(rating).ToString().ToLowerInvariant();
        return s.Format("info.town.rating", ("band", s.Get($"town.rating.{band}")), ("value", rating.ToString(CultureInfo.InvariantCulture)));
    }

    public static string Growth(Strings s, TownRegistry towns, int town)
    {
        var service = towns.ServiceOf(town);
        return service == 0
            ? s.Get("info.town.growth.none")
            : s.Format("info.town.growth", ("percent", (service / 10).ToString(CultureInfo.InvariantCulture)));
    }

    /// <summary>Destinations listed per cargo in the town window.</summary>
    public const int MaxDestinationLines = 5;

    /// <summary>
    /// Feature 013 (FR-022): where the town's passengers and mail want to go — the largest destinations with their
    /// monthly demand and the share the player's network moved last month — and how many goods sources want the town.
    /// </summary>
    public static string Demand(Strings s, TownRegistry towns, DestinationTables demand, int town, int mailCargo)
    {
        ArgumentNullException.ThrowIfNull(s);
        ArgumentNullException.ThrowIfNull(towns);
        ArgumentNullException.ThrowIfNull(demand);
        towns.TryGetTown(town, out var info);
        var builder = new StringBuilder();
        var rules = demand.Rules;
        for (var r = 0; r < rules.Cargo.Count; r++)
        {
            var rule = rules.Cargo[r];
            if (rule.Source != DemandSource.Buildings)
            {
                continue;
            }

            var row = demand.RowOf(r, town);
            if (row < 0)
            {
                continue;
            }

            var isMail = rule.CargoIndex == mailCargo;
            var generated = isMail ? info.MailGeneratedLastMonth : info.PassengersGeneratedLastMonth;
            if (builder.Length > 0)
            {
                builder.Append('\n');
            }

            builder.Append(s.Get(isMail ? "info.town.demand.mail" : "info.town.demand.passengers"));
            var destinations = demand.Destinations(r, row);
            var shares = demand.Shares(r, row).ToArray();
            var sorted = new int[destinations.Length];
            for (var i = 0; i < sorted.Length; i++)
            {
                sorted[i] = i;
            }

            Array.Sort(sorted, (a, b) => shares[b] != shares[a] ? shares[b].CompareTo(shares[a]) : a.CompareTo(b));
            for (var k = 0; k < sorted.Length && k < MaxDestinationLines; k++)
            {
                var i = sorted[k];
                var wanted = demand.MonthlyDemand(r, row, i, generated);
                var moved = demand.Statistics(r, row, i).MovedLast;
                var served = wanted <= 0 ? 0 : Math.Min(100, moved * 100 / wanted);
                var target = destinations[i] == town ? s.Get("info.town.demand.ownTown") : towns.NameOf(destinations[i]);
                builder.Append('\n');
                builder.Append(s.Format("info.town.demand.line", ("town", target), ("amount", wanted.ToString("N0", s.Culture)),
                    ("served", served.ToString(CultureInfo.InvariantCulture))));
            }
        }

        var goods = Goods(s, demand, town);
        if (goods.Length > 0)
        {
            builder.Append('\n');
            builder.Append(goods);
        }

        return builder.ToString();
    }

    /// <summary>Goods: per goods cargo, how many factories want to send here and how much they sent last month.</summary>
    public static string Goods(Strings s, DestinationTables demand, int town)
    {
        ArgumentNullException.ThrowIfNull(s);
        ArgumentNullException.ThrowIfNull(demand);
        var builder = new StringBuilder();
        var rules = demand.Rules;
        for (var r = 0; r < rules.Cargo.Count; r++)
        {
            if (rules.Cargo[r].Source != DemandSource.Industries)
            {
                continue;
            }

            var (sources, moved) = GoodsSources(demand, r, town);
            if (sources > 0)
            {
                if (builder.Length > 0)
                {
                    builder.Append('\n');
                }

                builder.Append(s.Format("info.town.demand.goods", ("sources", sources.ToString(CultureInfo.InvariantCulture)),
                    ("moved", moved.ToString("N0", s.Culture))));
            }
        }

        return builder.ToString();
    }

    /// <summary>Sources of an industry-fed demand rule whose table lists the town, and what they moved there last month.</summary>
    public static (int Sources, long Moved) GoodsSources(DestinationTables demand, int rule, int town)
    {
        ArgumentNullException.ThrowIfNull(demand);
        var sources = 0;
        long moved = 0;
        for (var row = 0; row < demand.RowCount(rule); row++)
        {
            var index = demand.Destinations(rule, row).IndexOf(town);
            if (index >= 0)
            {
                sources++;
                moved += demand.Statistics(rule, row, index).MovedLast;
            }
        }

        return (sources, moved);
    }
}

/// <summary>
/// The town window's demand rows (feature 013) recomputed when the window's version changes (once a day): per
/// building-fed rule, the largest destinations (as in <see cref="TownWindow.Demand"/>) with their monthly demand and
/// the percentage carried last month. Steady frames only read the arrays.
/// </summary>
internal sealed class TownDemandView
{
    private readonly TownRegistry _towns;
    private readonly DestinationTables _demand;
    private readonly int _town;
    private readonly int _mailCargo;
    private readonly Func<long> _version;
    private readonly int[] _count;
    private readonly int[,] _target;
    private readonly int[,] _wanted;
    private readonly int[,] _served;
    private bool[] _taken = [];
    private long _seen = long.MinValue;

    public TownDemandView(TownRegistry towns, DestinationTables demand, int town, int mailCargo, Func<long> version)
    {
        _towns = towns ?? throw new ArgumentNullException(nameof(towns));
        _demand = demand ?? throw new ArgumentNullException(nameof(demand));
        _town = town;
        _mailCargo = mailCargo;
        _version = version ?? throw new ArgumentNullException(nameof(version));
        var rules = demand.Rules.Cargo.Count;
        _count = new int[rules];
        _target = new int[rules, TownWindow.MaxDestinationLines];
        _wanted = new int[rules, TownWindow.MaxDestinationLines];
        _served = new int[rules, TownWindow.MaxDestinationLines];
    }

    /// <summary>Factories (of every goods rule) that want to send goods to the town.</summary>
    public int GoodsSources { get; private set; }

    /// <summary>Recomputes when the version changed; returns itself.</summary>
    public TownDemandView Refresh()
    {
        var version = _version();
        if (version == _seen)
        {
            return this;
        }

        _seen = version;
        _towns.TryGetTown(_town, out var info);
        var rules = _demand.Rules;
        GoodsSources = 0;
        for (var r = 0; r < rules.Cargo.Count; r++)
        {
            _count[r] = 0;
            var rule = rules.Cargo[r];
            if (rule.Source == DemandSource.Industries)
            {
                GoodsSources += TownWindow.GoodsSources(_demand, r, _town).Sources;
                continue;
            }

            var row = rule.Source == DemandSource.Buildings ? _demand.RowOf(r, _town) : -1;
            if (row < 0)
            {
                continue;
            }

            var generated = rule.CargoIndex == _mailCargo ? info.MailGeneratedLastMonth : info.PassengersGeneratedLastMonth;
            var destinations = _demand.Destinations(r, row);
            var shares = _demand.Shares(r, row);

            // The largest shares first (ties: table order), by repeated selection: at most five picks.
            if (_taken.Length < destinations.Length)
            {
                _taken = new bool[destinations.Length];
            }

            var taken = _taken.AsSpan(0, destinations.Length);
            taken.Clear();
            for (var k = 0; k < TownWindow.MaxDestinationLines && k < destinations.Length; k++)
            {
                var best = -1;
                for (var i = 0; i < destinations.Length; i++)
                {
                    if (!taken[i] && (best < 0 || shares[i] > shares[best]))
                    {
                        best = i;
                    }
                }

                taken[best] = true;
                var wanted = _demand.MonthlyDemand(r, row, best, generated);
                var moved = _demand.Statistics(r, row, best).MovedLast;
                _target[r, k] = destinations[best];
                _wanted[r, k] = wanted;
                _served[r, k] = wanted <= 0 ? 0 : Math.Min(100, moved * 100 / wanted);
                _count[r] = k + 1;
            }
        }

        return this;
    }

    /// <summary>Destinations listed for a rule.</summary>
    public int Count(int rule) => _count[rule];

    public int Town(int rule, int index) => _target[rule, index];

    /// <summary>Units a month that want to go there.</summary>
    public int Wanted(int rule, int index) => _wanted[rule, index];

    /// <summary>Percentage carried last month (0–100).</summary>
    public int Served(int rule, int index) => _served[rule, index];
}

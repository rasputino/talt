using System.Globalization;
using Talt.Client.Localization;
using Talt.Client.Ui;
using Talt.Client.Ui.Widgets;
using System.Text;
using Talt.Core.Construction;
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

    /// <summary>Adds the town's lines to a window's content.</summary>
    public static void Add(StackPanel content, TownRegistry towns, int town, Func<long> version, DestinationTables? demand = null, int mailCargo = -1, Talt.Core.Stations.StationRegistry? stations = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(towns);
        if ((uint)town >= (uint)towns.Count)
        {
            content.Add(new Label(TextRef.Of("info.town.title")) { Color = TextColor.Muted });
            return;
        }

        content.Add(Line(version, s => Population(s, towns, town)));
        content.Add(Line(version, s => Buildings(s, towns, town)));
        content.Add(Line(version, s => Passengers(s, towns, town)));
        content.Add(Line(version, s => Mail(s, towns, town)));
        content.Add(Line(version, s => Accepts(s, towns, town)));
        content.Add(Line(version, s => Rating(s, towns, town, Company.PlayerId)));
        content.Add(new Label(TextRef.Dynamic(version, s => Growth(s, towns, town))) { Color = TextColor.Muted, Wrap = true, Width = 300 });
        if (stations is not null)
        {
            // Feature 013: passengers and mail waiting at the player's stations here, + for their destinations.
            CargoRows.AddTown(content, stations, towns, town, Company.PlayerId, version);
        }

        if (demand is not null)
        {
            content.Add(new Label(TextRef.Dynamic(version, s => Demand(s, towns, demand, town, mailCargo))) { Wrap = true, Width = 340 });
        }
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

        // Goods: sources whose table lists this town.
        for (var r = 0; r < rules.Cargo.Count; r++)
        {
            if (rules.Cargo[r].Source != DemandSource.Industries)
            {
                continue;
            }

            var sources = 0;
            long moved = 0;
            for (var row = 0; row < demand.RowCount(r); row++)
            {
                var destinations = demand.Destinations(r, row);
                var index = destinations.IndexOf(town);
                if (index >= 0)
                {
                    sources++;
                    moved += demand.Statistics(r, row, index).MovedLast;
                }
            }

            if (sources > 0)
            {
                builder.Append('\n');
                builder.Append(s.Format("info.town.demand.goods", ("sources", sources.ToString(CultureInfo.InvariantCulture)),
                    ("moved", moved.ToString("N0", s.Culture))));
            }
        }

        return builder.ToString();
    }

    private static Label Line(Func<long> version, Func<Strings, string> compose) => new(TextRef.Dynamic(version, compose));
}

using System.Globalization;
using Talt.Client.Localization;
using Talt.Client.Ui;
using Talt.Client.Ui.Widgets;
using Talt.Core.Construction;
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
    public static void Add(StackPanel content, TownRegistry towns, int town, Func<long> version)
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

    private static Label Line(Func<long> version, Func<Strings, string> compose) => new(TextRef.Dynamic(version, compose));
}

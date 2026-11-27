using System.Globalization;
using System.Text;
using Talt.Client.Localization;
using Talt.Client.Ui;
using Talt.Client.Ui.Widgets;
using Talt.Core.Content;
using Talt.Core.Stations;
using Talt.Core.Towns;

namespace Talt.Client.Screens;

/// <summary>
/// Gauges shared by the road vehicle and train windows: the load bar of one cargo split by destination town, speed,
/// reliability, age and the money figures coloured by sign.
/// </summary>
internal static class VehicleGauges
{
    /// <summary>
    /// Adds the load row of one cargo (badge, bar split by destination, "amount / capacity") and, under it, the
    /// destinations in small text in the bar's order.
    /// </summary>
    public static void AddLoad(StackPanel parent, GameContent content, TownRegistry? towns, int cargo, HoldSegments hold, Func<int> capacity, Func<long> version)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(hold);
        ArgumentNullException.ThrowIfNull(capacity);
        var row = parent.Add(new StackPanel(Orientation.Horizontal) { Spacing = 8 });
        // The badge sits in a cell as wide as the label column, so the bar lines up with the gauges below.
        var cell = row.Add(new StackPanel(Orientation.Horizontal) { Width = InfoRows.LabelWidth });
        cell.Add(InfoRows.Badge(content, cargo)).Tooltip = TextRef.Of(content.Cargo(cargo).NameKey);
        row.Add(new SegmentBar(capacity, () => hold.Refresh().SegmentCount, i => hold.Segment(i)) { Width = 140 });
        row.Add(new Label(TextRef.Dynamic(version, s => InfoRows.Ratio(s, hold.Refresh().Total, capacity()))) { MinWidth = InfoRows.ValueWidth });
        parent.Add(new Label(TextRef.Dynamic(version, s => Destinations(s, towns, hold.Refresh())), TextSize.Small)
        {
            Wrap = true,
            Width = 360,
            Color = TextColor.Muted,
            Margin = new Thickness(InfoRows.LabelWidth + 8, 0, 0, 2),
            VisibleWhen = () => hold.Refresh().DestinationCount > 0,
        });
    }

    /// <summary>"Going to: Town 12, Other 4, …" in the order of the bar's segments.</summary>
    public static string Destinations(Strings s, TownRegistry? towns, HoldSegments hold)
    {
        ArgumentNullException.ThrowIfNull(s);
        ArgumentNullException.ThrowIfNull(hold);
        if (hold.DestinationCount == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        for (var i = 0; i < hold.DestinationCount; i++)
        {
            if (builder.Length > 0)
            {
                builder.Append(", ");
            }

            var town = hold.TownAt(i);
            builder.Append(s.Format("vehicle.destination", ("town", towns is not null && (uint)town < (uint)towns.Count ? towns.NameOf(town) : "?"),
                ("amount", hold.AmountAt(i).ToString("N0", s.Culture))));
        }

        if (hold.OtherAmount > 0)
        {
            builder.Append(", ");
            builder.Append(s.Format("vehicle.destination.other", ("amount", hold.OtherAmount.ToString("N0", s.Culture))));
        }

        return s.Format("vehicle.destinations", ("list", builder.ToString()));
    }

    /// <summary>Speed against top speed (accent gauge).</summary>
    public static void AddSpeed(StackPanel parent, Func<int> speed, Func<int> max, Func<long> version) =>
        InfoRows.AddMeter(parent, TextRef.Of("vehicle.row.speed"), new Meter(() => InfoRows.Share(speed(), max()), MeterColoring.Accent),
            TextRef.Dynamic(version, s => s.Format("vehicle.row.speed.value", ("speed", speed().ToString(CultureInfo.InvariantCulture)), ("max", max().ToString(CultureInfo.InvariantCulture)))));

    /// <summary>Reliability, 0–1 000 (red → green gauge).</summary>
    public static void AddReliability(StackPanel parent, Func<int> perMille, Func<long> version) =>
        InfoRows.AddMeter(parent, TextRef.Of("vehicle.row.reliability"), new Meter(() => perMille() / 1000f),
            TextRef.Dynamic(version, s => InfoRows.Percent(s, perMille())), TextRef.Of("vehicle.row.reliability.tooltip"));

    /// <summary>Age against the model's life (green → red gauge, full past the end of its life).</summary>
    public static void AddAge(StackPanel parent, Func<int> ageDays, Func<int> lifeYears, Func<long> version) =>
        InfoRows.AddMeter(parent, TextRef.Of("vehicle.row.age"), new Meter(() => InfoRows.Share(ageDays(), lifeYears() * 365L), MeterColoring.InverseRamp),
            TextRef.Dynamic(version, s => s.Format(ageDays() / 365 > lifeYears() ? "vehicle.row.age.old" : "vehicle.row.age.value",
                ("years", (ageDays() / 365).ToString(CultureInfo.InvariantCulture)), ("life", lifeYears().ToString(CultureInfo.InvariantCulture)))));

    /// <summary>Value, and this and last year's profit in green or red.</summary>
    public static void AddMoney(StackPanel parent, IVehicleUi ui, Func<long> value, Func<long> thisYear, Func<long> lastYear, Func<long> version)
    {
        ArgumentNullException.ThrowIfNull(ui);
        AddFigure(parent, "vehicle.row.value", TextRef.Dynamic(version, s => ui.Money(value(), s)), () => TextColor.Normal);
        AddFigure(parent, "vehicle.row.profitThisYear", TextRef.Dynamic(version, s => ui.Money(thisYear(), s)), () => thisYear() < 0 ? TextColor.Negative : TextColor.Positive);
        AddFigure(parent, "vehicle.row.profitLastYear", TextRef.Dynamic(version, s => ui.Money(lastYear(), s)), () => lastYear() < 0 ? TextColor.Negative : TextColor.Positive);
    }

    private static void AddFigure(StackPanel parent, string key, TextRef value, Func<TextColor> color)
    {
        var row = parent.Add(new StackPanel(Orientation.Horizontal) { Spacing = 8 });
        row.Add(new Label(TextRef.Of(key)) { Width = InfoRows.LabelWidth, Color = TextColor.Muted });
        row.Add(new Label(value) { MinWidth = InfoRows.ValueWidth, ColorOf = color });
    }
}

/// <summary>
/// One cargo on board a vehicle or train summed by destination town, recomputed when the version changes (every tick
/// while the window is open): the largest destinations first, then the rest. Each destination is a shade of the
/// cargo's colour in the load bar; units without a destination (undirected cargo) use the plain colour.
/// </summary>
internal sealed class HoldSegments
{
    /// <summary>Destinations given their own segment; the others are summed.</summary>
    public const int MaxDestinations = 6;

    private readonly Func<long> _version;
    private readonly Func<int> _holdCount;
    private readonly Func<int, CargoHold?> _holdAt;
    private readonly UiColor _color;
    private readonly Dictionary<int, int> _byTown = [];
    private readonly int[] _towns = new int[MaxDestinations];
    private readonly int[] _amounts = new int[MaxDestinations];
    private long _seen = long.MinValue;

    public HoldSegments(Func<long> version, Func<int> holdCount, Func<int, CargoHold?> holdAt, UiColor color)
    {
        _version = version ?? throw new ArgumentNullException(nameof(version));
        _holdCount = holdCount ?? throw new ArgumentNullException(nameof(holdCount));
        _holdAt = holdAt ?? throw new ArgumentNullException(nameof(holdAt));
        _color = color;
    }

    /// <summary>Units on board.</summary>
    public int Total { get; private set; }

    /// <summary>Destinations with their own segment.</summary>
    public int DestinationCount { get; private set; }

    /// <summary>Units going to the destinations past <see cref="MaxDestinations"/>.</summary>
    public int OtherAmount { get; private set; }

    /// <summary>Units with no destination.</summary>
    public int UndirectedAmount { get; private set; }

    /// <summary>Segments of the bar: the destinations, the others, the undirected units.</summary>
    public int SegmentCount => DestinationCount + 2;

    public int TownAt(int index) => _towns[index];

    public int AmountAt(int index) => _amounts[index];

    /// <summary>A bar segment: a destination's shade, the others (muted) or the undirected units (plain colour).</summary>
    public (int Amount, UiColor Color) Segment(int index) =>
        index < DestinationCount ? (_amounts[index], MeterColors.Shade(_color, index + 1))
        : index == DestinationCount ? (OtherAmount, MeterColors.Lerp(_color, UiColor.FromBytes(128, 128, 128, 255), 0.6f))
        : (UndirectedAmount, _color);

    /// <summary>Recomputes when the version changed; returns itself.</summary>
    public HoldSegments Refresh()
    {
        var version = _version();
        if (version == _seen)
        {
            return this;
        }

        _seen = version;
        _byTown.Clear();
        Total = 0;
        UndirectedAmount = 0;
        var holds = _holdCount();
        for (var h = 0; h < holds; h++)
        {
            if (_holdAt(h) is not { } hold)
            {
                continue;
            }

            for (var i = 0; i < hold.Count; i++)
            {
                var amount = hold[i].Amount;
                Total += amount;
                if (hold[i].Destination < 0)
                {
                    UndirectedAmount += amount;
                }
                else
                {
                    _byTown[hold[i].Destination] = _byTown.GetValueOrDefault(hold[i].Destination) + amount;
                }
            }
        }

        // The largest first (ties: lower town index), by repeated selection.
        DestinationCount = 0;
        var directed = Total - UndirectedAmount;
        var listed = 0;
        while (DestinationCount < MaxDestinations)
        {
            int bestTown = -1, bestAmount = 0;
            foreach (var (town, amount) in _byTown)
            {
                if (amount > bestAmount || (amount == bestAmount && amount > 0 && town < bestTown))
                {
                    bestTown = town;
                    bestAmount = amount;
                }
            }

            if (bestTown < 0)
            {
                break;
            }

            _towns[DestinationCount] = bestTown;
            _amounts[DestinationCount] = bestAmount;
            _byTown[bestTown] = 0;
            listed += bestAmount;
            DestinationCount++;
        }

        OtherAmount = directed - listed;
        return this;
    }
}

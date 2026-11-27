using System.Globalization;
using Talt.Client.Localization;
using Talt.Client.Ui;
using Talt.Client.Ui.Widgets;
using Talt.Core.Content;

namespace Talt.Client.Screens;

/// <summary>
/// Building blocks of the graphical info windows (station, town, vehicle, train, line): a label column, a gauge and
/// a value, all in fixed widths so the rows of a window line up. Values are read every frame through delegates;
/// texts are cached <see cref="TextRef"/>s, so steady frames allocate nothing.
/// </summary>
internal static class InfoRows
{
    /// <summary>Width of the label column, in logical pixels.</summary>
    public const float LabelWidth = 112;

    /// <summary>Width of the value column, in logical pixels.</summary>
    public const float ValueWidth = 110;

    /// <summary>Adds "label ▕████░░░▏ value" to <paramref name="parent"/> and returns the row.</summary>
    public static StackPanel AddMeter(StackPanel parent, TextRef label, Meter meter, TextRef value, TextRef? tooltip = null)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(meter);
        var row = parent.Add(new StackPanel(Orientation.Horizontal) { Spacing = 8 });
        meter.Tooltip ??= tooltip;
        row.Add(new Label(label) { Width = LabelWidth, Color = TextColor.Muted });
        row.Add(meter);
        row.Add(new Label(value) { MinWidth = ValueWidth });
        return row;
    }

    /// <summary>A localized key as a row label.</summary>
    public static TextRef Key(string key) => TextRef.Of(key);

    /// <summary>"{percent} %" of a 0–1 000 value.</summary>
    public static string Percent(Strings s, int perMille) =>
        s.Format("info.meter.percent", ("percent", (Math.Clamp(perMille, 0, 1000) / 10).ToString(CultureInfo.InvariantCulture)));

    /// <summary>"{amount} / {total}" with thousands separators.</summary>
    public static string Ratio(Strings s, long amount, long total) =>
        s.Format("info.meter.ratio", ("amount", amount.ToString("N0", s.Culture)), ("total", total.ToString("N0", s.Culture)));

    /// <summary>Share of <paramref name="amount"/> in <paramref name="total"/>, 0 when the total is not positive.</summary>
    public static float Share(long amount, long total) => total <= 0 ? 0f : Math.Clamp(amount / (float)total, 0f, 1f);

    /// <summary>A cargo's badge (icon and colour from the data files).</summary>
    public static CargoBadge Badge(GameContent content, int cargo)
    {
        ArgumentNullException.ThrowIfNull(content);
        var definition = content.Cargo(cargo);
        return new CargoBadge(definition.Id, MeterColors.FromRgb(definition.Color));
    }

    /// <summary>A cargo's colour from <c>cargo.json</c>.</summary>
    public static UiColor CargoColor(GameContent content, int cargo) =>
        (uint)cargo < (uint)content.CargoCount ? MeterColors.FromRgb(content.Cargo(cargo).Color) : UiColor.White;

    /// <summary>A small title over a group of rows.</summary>
    public static Label Section(string key) => new(TextRef.Of(key), TextSize.Title) { Margin = new Thickness(0, 4, 0, 0) };
}

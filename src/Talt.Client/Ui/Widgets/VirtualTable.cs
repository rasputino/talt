using System.Numerics;
using Silk.NET.Input;
using Talt.Client.Localization;
using Talt.Client.Ui.Font;

namespace Talt.Client.Ui.Widgets;

/// <summary>Either localized text or a numeric value rendered into caller-owned scratch space.</summary>
internal readonly record struct TableCell(string? Text, decimal Number, string? Format = null, IFormatProvider? Provider = null, string? NumberTemplate = null, float? Fill = null, UiColor? FillColor = null)
{
    internal static TableCell Gauge(float fill, UiColor? color) => new(null, (decimal)fill, "P0", Fill: fill, FillColor: color);

    public static implicit operator TableCell(string text) => new(text, 0);
    public static implicit operator TableCell(long number) => new(null, number);
}

/// <summary>A fixed-height table that binds only the visible rows to stable entity keys.</summary>
internal sealed class VirtualTable : Widget
{
    private const int RowLimit = 10;
    private const float Gutter = 14;
    private readonly Func<int> _refresh;
    private readonly Func<int, long> _keyAt;
    private readonly Row[] _rows = new Row[RowLimit];
    private readonly float _width;
    private int _count;
    private int _visibleRows;
    private float _dragOffset;

    public VirtualTable(float[] widths, Func<int> refresh, Func<int, long> keyAt, Func<long> revision,
        Func<Strings, long, int, TableCell> cell, Action<long> activate)
    {
        _refresh = refresh;
        _keyAt = keyAt;
        _width = widths.Sum() + Gutter;
        for (var i = 0; i < _rows.Length; i++)
        {
            _rows[i] = Add(new Row(widths, revision, cell, activate));
        }
    }

    internal int FirstRow { get; private set; }

    public override bool IsHitTestVisible => true;

    protected override Vector2 MeasureCore(UiContext ui, Vector2 available)
    {
        _count = _refresh();
        return new Vector2(_width * ui.Scale, Math.Min(RowLimit, Math.Max(1, _count)) * ui.Theme.ButtonHeight * ui.Scale);
    }

    protected override void ArrangeCore(UiContext ui, RectF area)
    {
        var height = ui.Theme.ButtonHeight * ui.Scale;
        _visibleRows = Math.Clamp((int)(area.Height / height), 1, RowLimit);
        FirstRow = Math.Clamp(FirstRow, 0, LastPage);
        for (var i = 0; i < RowLimit; i++)
        {
            var index = FirstRow + i;
            var row = _rows[index % RowLimit];
            row.Visible = i < _visibleRows && index < _count;
            if (row.Visible)
            {
                row.Bind(_keyAt(index));
                var slot = new RectF(area.X, area.Y + (i * height), MathF.Max(0, area.Width - Gutter * ui.Scale), height);
                row.Measure(ui, new Vector2(slot.Width, slot.Height));
                row.Arrange(ui, slot);
            }
        }
    }

    private int LastPage => Math.Max(0, _count - _visibleRows);

    public override bool OnScroll(UiContext ui, float delta)
    {
        if (LastPage == 0 || delta == 0)
        {
            return false;
        }

        FirstRow = (int)Math.Clamp((long)FirstRow - (long)Math.CopySign(Math.Max(1, Math.Abs(delta) * 3), delta), 0, LastPage);
        return true;
    }

    private float ThumbHeight(UiContext ui) =>
        Math.Min(Bounds.Height, Math.Max(16 * ui.Scale, Bounds.Height * _visibleRows / Math.Max(1, _count)));

    private float ThumbTop(UiContext ui) => Bounds.Y + ((Bounds.Height - ThumbHeight(ui)) * FirstRow / Math.Max(1, LastPage));

    public override bool OnMouseDown(UiContext ui, MouseButton button, Vector2 position)
    {
        if (button != MouseButton.Left || LastPage == 0 || position.X < Bounds.Right - Gutter * ui.Scale)
        {
            return false;
        }

        var offset = position.Y - ThumbTop(ui);
        _dragOffset = offset >= 0 && offset < ThumbHeight(ui) ? offset : ThumbHeight(ui) / 2;
        OnDrag(ui, position, Vector2.Zero);
        return true;
    }

    public override void OnDrag(UiContext ui, Vector2 position, Vector2 delta)
    {
        var extent = Bounds.Height - ThumbHeight(ui);
        if (extent > 0)
        {
            FirstRow = (int)MathF.Round(LastPage * Math.Clamp((position.Y - Bounds.Y - _dragOffset) / extent, 0f, 1f));
        }
    }

    protected override void DrawSelf(UiContext ui, UiDrawList list)
    {
        if (LastPage > 0)
        {
            list.AddRect(new RectF(Bounds.Right - 8 * ui.Scale, ThumbTop(ui), 6 * ui.Scale, ThumbHeight(ui)), ui.Theme.TextMuted, 2 * ui.Scale);
        }
    }

    private sealed class CellWidget(Func<long> version, Func<Strings, TableCell> resolve) : Widget
    {
        private long _version = -1;
        private int _language = -1;
        private TableCell _cell;

        protected override Vector2 MeasureCore(UiContext ui, Vector2 available) =>
            new(available.X, ui.Theme.ButtonHeight * ui.Scale);

        protected override void DrawSelf(UiContext ui, UiDrawList list)
        {
            var current = version();
            if (_version != current || _language != ui.LanguageVersion)
            {
                _version = current;
                _language = ui.LanguageVersion;
                _cell = resolve(ui.Strings);
            }

            if (_cell.Fill is { } fraction)
            {
                var height = 3 * ui.Scale;
                var track = new RectF(Bounds.X, Bounds.Bottom - height, Bounds.Width - 4 * ui.Scale, height);
                list.AddRect(track, ui.Theme.Button);
                list.AddRect(new RectF(track.X, track.Y, track.Width * fraction, height), _cell.FillColor ?? ui.Theme.Accent);
            }

            var size = ui.FontSize(TextSize.Small);
            var top = Bounds.Y + MathF.Round((Bounds.Height - TextLayout.LineHeight(ui.Font, size)) / 2);
            var origin = new Vector2(Bounds.X, top);
            if (_cell.Text is { } text)
            {
                list.AddTextClipped(ui.Font, text, origin, size, ui.Theme.Text, Bounds.Width);
            }
            else
            {
                Span<char> digits = stackalloc char[96];
                _cell.Number.TryFormat(digits, out var length, _cell.Format, _cell.Provider ?? ui.Strings.Culture);
                if (_cell.NumberTemplate is { } template && template.IndexOf("{number}", StringComparison.Ordinal) is var index && index >= 0)
                {
                    Span<char> formatted = stackalloc char[160];
                    var total = template.Length - "{number}".Length + length;
                    if (total <= formatted.Length)
                    {
                        template.AsSpan(0, index).CopyTo(formatted);
                        digits[..length].CopyTo(formatted[index..]);
                        template.AsSpan(index + "{number}".Length).CopyTo(formatted[(index + length)..]);
                        list.AddTextClipped(ui.Font, formatted[..total], origin, size, ui.Theme.Text, Bounds.Width);
                        return;
                    }
                }
                list.AddTextClipped(ui.Font, digits[..length], origin, size, ui.Theme.Text, Bounds.Width);
            }
        }
    }

    private sealed class Row : StackPanel
    {
        private readonly Action<long> _activate;
        private long _key;
        private long _pressed;
        private long _binding;

        public Row(float[] widths, Func<long> revision, Func<Strings, long, int, TableCell> cell, Action<long> activate)
            : base(Orientation.Horizontal)
        {
            Spacing = 0;
            _activate = activate;
            for (var i = 0; i < widths.Length; i++)
            {
                var column = i;
                Add(new CellWidget(() => _binding + revision(), s => cell(s, _key, column)) { Width = widths[i] });
            }
        }

        public override bool IsHitTestVisible => true;

        public void Bind(long key)
        {
            if (_key != key)
            {
                _key = key;
                _binding++;
            }
        }

        public override bool OnMouseDown(UiContext ui, MouseButton button, Vector2 position)
        {
            _pressed = _key;
            return button == MouseButton.Left;
        }

        public override void OnMouseUp(UiContext ui, MouseButton button, Vector2 position, bool inside)
        {
            if (button == MouseButton.Left && inside && _pressed == _key)
            {
                _activate(_key);
            }
        }

        protected override void DrawSelf(UiContext ui, UiDrawList list)
        {
            if (ui.Hovered == this)
            {
                list.AddRect(Bounds, ui.Theme.ButtonHover);
            }
        }
    }
}

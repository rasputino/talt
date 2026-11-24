using System.Globalization;
using System.Numerics;
using Talt.Client.Ui;
using Talt.Client.Ui.Font;
using Talt.Client.Ui.Widgets;
using Talt.Core.Terrain;
using Talt.Core.Towns;

namespace Talt.Client.Screens;

/// <summary>
/// Name and population plates above the towns (feature 008, FR-022). Every frame the screen asks
/// <see cref="Update"/> with the camera; the layer then draws at most <see cref="MaxLabels"/> plates, larger
/// towns first, skipping plates that would overlap one already placed. Clicks are not taken by the widget (the
/// world below stays usable); the screen asks <see cref="TownUnder"/> instead. Allocation-free per frame: label
/// texts are cached and rebuilt only when a population or the language changes.
/// </summary>
internal sealed class TownLabels : Widget
{
    public const int MaxLabels = 64;

    /// <summary>Height of the plate's anchor above the town centre's ground (world units).</summary>
    private const float AnchorHeight = 9f;

    private readonly TownRegistry _towns;
    private readonly Heightmap _ground;
    private readonly int[] _order;
    private readonly Vector2[] _screen;
    private readonly bool[] _visible;
    private readonly string?[] _text;
    private readonly int[] _textPopulation;
    private readonly RectF[] _placed = new RectF[MaxLabels];
    private readonly int[] _placedTown = new int[MaxLabels];
    private CultureInfo? _textCulture;
    private int _orderVersion = -1;
    private int _placedCount;

    public TownLabels(TownRegistry towns, Heightmap ground)
    {
        _towns = towns ?? throw new ArgumentNullException(nameof(towns));
        _ground = ground ?? throw new ArgumentNullException(nameof(ground));
        _order = new int[towns.Count];
        _screen = new Vector2[towns.Count];
        _visible = new bool[towns.Count];
        _text = new string?[towns.Count];
        _textPopulation = new int[towns.Count];
    }

    /// <summary>Number of plates drawn in the last frame.</summary>
    public int PlacedCount => _placedCount;

    /// <summary>
    /// Projects the town centres for this frame (framebuffer pixels). Towns beyond the fog or behind the camera
    /// are hidden.
    /// </summary>
    public void Update(Matrix4x4 viewProjection, Vector2 viewport, Vector3 cameraPosition, float fogDistance)
    {
        for (var town = 0; town < _towns.Count; town++)
        {
            var (cx, cz) = _towns.CentreOf(town);
            var world = new Vector3(cx + 0.5f, _ground.Get(cx, cz) + AnchorHeight, cz + 0.5f);
            _visible[town] = Vector3.Distance(world, cameraPosition) <= fogDistance * 1.2f
                && TryProject(viewProjection, viewport, world, out _screen[town]);
        }
    }

    /// <summary>World point to framebuffer pixels; false when behind the camera or off screen.</summary>
    public static bool TryProject(Matrix4x4 viewProjection, Vector2 viewport, Vector3 world, out Vector2 screen)
    {
        var clip = Vector4.Transform(new Vector4(world, 1f), viewProjection);
        screen = default;
        if (clip.W <= 0.01f)
        {
            return false;
        }

        var x = clip.X / clip.W;
        var y = clip.Y / clip.W;
        if (x < -1.1f || x > 1.1f || y < -1.1f || y > 1.1f)
        {
            return false;
        }

        screen = new Vector2((x + 1f) * 0.5f * viewport.X, (1f - y) * 0.5f * viewport.Y);
        return true;
    }

    /// <summary>The town whose plate is under a cursor position (framebuffer pixels), or −1.</summary>
    public int TownUnder(Vector2 cursor)
    {
        for (var i = _placedCount - 1; i >= 0; i--)
        {
            var r = _placed[i];
            if (cursor.X >= r.X && cursor.X <= r.Right && cursor.Y >= r.Y && cursor.Y <= r.Bottom)
            {
                return _placedTown[i];
            }
        }

        return -1;
    }

    /// <summary>True when two rectangles overlap (touching edges do not).</summary>
    public static bool Overlaps(in RectF a, in RectF b) => a.X < b.Right && b.X < a.Right && a.Y < b.Bottom && b.Y < a.Bottom;

    protected override Vector2 MeasureCore(UiContext ui, Vector2 available) => Vector2.Zero;

    protected override void DrawSelf(UiContext ui, UiDrawList list)
    {
        RefreshOrder();
        _placedCount = 0;
        var size = ui.FontSize(TextSize.Body);
        var lineHeight = MathF.Ceiling(TextLayout.LineHeight(ui.Font, size));
        var padX = 6f * ui.Scale;
        var padY = 2f * ui.Scale;
        var theme = ui.Theme;
        var culture = ui.Strings.Culture;
        if (!ReferenceEquals(culture, _textCulture))
        {
            _textCulture = culture;
            Array.Clear(_text);
        }

        foreach (var town in _order)
        {
            if (_placedCount == MaxLabels)
            {
                break;
            }

            if (!_visible[town])
            {
                continue;
            }

            var text = TextOf(town, culture);
            var width = MathF.Ceiling(TextLayout.Measure(ui.Font, text, size)) + (2 * padX);
            var height = lineHeight + (2 * padY);
            var anchor = _screen[town];
            var rect = new RectF(MathF.Round(anchor.X - (width / 2f)), MathF.Round(anchor.Y - height), width, height);
            var free = true;
            for (var i = 0; i < _placedCount; i++)
            {
                if (Overlaps(rect, _placed[i]))
                {
                    free = false;
                    break;
                }
            }

            if (!free)
            {
                continue;
            }

            _placed[_placedCount] = rect;
            _placedTown[_placedCount] = town;
            _placedCount++;
            list.AddPanel(rect, theme.Panel, theme.PanelBorder, MathF.Max(1f, ui.Scale), 3f * ui.Scale);
            list.AddText(ui.Font, text, new Vector2(rect.X + padX, rect.Y + padY), size, theme.Text);
        }
    }

    private string TextOf(int town, CultureInfo culture)
    {
        var population = _towns.PopulationOf(town);
        if (_text[town] is null || _textPopulation[town] != population)
        {
            _text[town] = $"{_towns.NameOf(town)}  {population.ToString("N0", culture)}";
            _textPopulation[town] = population;
        }

        return _text[town]!;
    }

    /// <summary>Towns by population, largest first (rebuilt when towns change).</summary>
    private void RefreshOrder()
    {
        if (_orderVersion == _towns.Version)
        {
            return;
        }

        _orderVersion = _towns.Version;
        for (var town = 0; town < _order.Length; town++)
        {
            _order[town] = town;
        }

        // Insertion sort: the order barely changes between months and this runs on change only.
        for (var i = 1; i < _order.Length; i++)
        {
            var town = _order[i];
            var population = _towns.PopulationOf(town);
            var j = i - 1;
            while (j >= 0 && _towns.PopulationOf(_order[j]) < population)
            {
                _order[j + 1] = _order[j];
                j--;
            }

            _order[j + 1] = town;
        }
    }
}

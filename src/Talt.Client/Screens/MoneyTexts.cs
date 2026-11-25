using System.Numerics;
using Talt.Client.Ui;
using Talt.Client.Ui.Font;
using Talt.Client.Ui.Widgets;
using Talt.Core.Terrain;

namespace Talt.Client.Screens;

/// <summary>
/// Floating money texts on the map (feature 011, FR-009, research R13): each paid delivery shows its amount above
/// the place it happened (the unloading station; the vehicle from feature 012 on), rising and fading for
/// <see cref="LifeSeconds"/>. Texts are formatted once when added; drawing allocates nothing.
/// </summary>
internal sealed class MoneyTexts : Widget
{
    /// <summary>Texts shown at once; the oldest is replaced when full.</summary>
    public const int Capacity = 64;

    public const float LifeSeconds = 2.5f;

    /// <summary>World height of the text's start above the ground.</summary>
    private const float AnchorHeight = 4f;

    /// <summary>Logical pixels a text rises during its life.</summary>
    private const float RisePixels = 36f;

    private readonly Heightmap _ground;
    private readonly Vector3[] _world = new Vector3[Capacity];
    private readonly string[] _text = new string[Capacity];
    private readonly bool[] _positive = new bool[Capacity];
    private readonly float[] _born = new float[Capacity];
    private readonly Vector2[] _screen = new Vector2[Capacity];
    private readonly bool[] _visible = new bool[Capacity];
    private int _next;
    private int _count;
    private float _now;

    public MoneyTexts(Heightmap ground) => _ground = ground ?? throw new ArgumentNullException(nameof(ground));

    /// <summary>Texts alive.</summary>
    public int Count => _count;

    /// <summary>The text of entry <paramref name="index"/> (for tests).</summary>
    internal string TextAt(int index) => _text[index];

    /// <summary>Adds a text above tile (x, z) at time <paramref name="now"/> (seconds of interface time).</summary>
    public void Add(int x, int z, string text, bool positive, float now)
    {
        var slot = _next;
        _next = (_next + 1) % Capacity;
        _count = Math.Min(Capacity, _count + 1);
        _world[slot] = new Vector3(x + 0.5f, _ground.Get(x, z) + AnchorHeight, z + 0.5f);
        _text[slot] = text;
        _positive[slot] = positive;
        _born[slot] = now;
    }

    /// <summary>Projects the live texts for this frame (framebuffer pixels); hides those beyond the fog or behind the camera.</summary>
    public void Update(Matrix4x4 viewProjection, Vector2 viewport, Vector3 cameraPosition, float fogDistance, float now)
    {
        _now = now;
        for (var i = 0; i < _count; i++)
        {
            _visible[i] = now - _born[i] < LifeSeconds
                && Vector3.Distance(_world[i], cameraPosition) <= fogDistance
                && TownLabels.TryProject(viewProjection, viewport, _world[i], out _screen[i]);
        }
    }

    protected override Vector2 MeasureCore(UiContext ui, Vector2 available) => Vector2.Zero;

    protected override void DrawSelf(UiContext ui, UiDrawList list)
    {
        var size = ui.FontSize(TextSize.Title);
        for (var i = 0; i < _count; i++)
        {
            if (!_visible[i])
            {
                continue;
            }

            var age = Math.Clamp((_now - _born[i]) / LifeSeconds, 0f, 1f);
            var color = (_positive[i] ? ui.Theme.Positive : ui.Theme.Negative).WithAlpha(1f - (age * age));
            var width = TextLayout.Measure(ui.Font, _text[i], size);
            var position = new Vector2(MathF.Round(_screen[i].X - (width / 2f)), MathF.Round(_screen[i].Y - (age * RisePixels * ui.Scale)));
            list.AddText(ui.Font, _text[i], position, size, color);
        }
    }
}

using System.Numerics;
using System.Runtime.CompilerServices;

namespace Talt.Client.Ui.Widgets;

/// <summary>
/// Shows a save thumbnail (feature 014): one <see cref="SlotSize"/>² area of the interface atlas, reserved on first use and
/// rewritten when a different picture is shown, so the load screen needs no texture of its own. Only one picture is shown
/// at a time (the selected save). The 256² RGB overview is reduced 2 × 2.
/// </summary>
internal sealed class ThumbnailView : Widget
{
    /// <summary>Side of the atlas slot, in texels.</summary>
    public const int SlotSize = 128;

    private static readonly ConditionalWeakTable<UiAtlas, Slot> Slots = new();

    private byte[]? _pending;
    private int _pendingWidth;
    private int _pendingHeight;
    private bool _hasImage;

    public ThumbnailView(float size)
    {
        Size = size;
        HorizontalAlignment = Alignment.Start;
    }

    /// <summary>Displayed side, logical pixels.</summary>
    public float Size { get; }

    /// <summary>Shows <paramref name="rgb"/> (row-major RGB24), or nothing when null.</summary>
    public void Show(byte[]? rgb, int width, int height)
    {
        _pending = rgb;
        _pendingWidth = width;
        _pendingHeight = height;
        _hasImage = rgb is not null;
        if (rgb is not null)
        {
            _pendingVersion = ++_nextPicture; // A new picture id: the shared slot is rewritten when it shows another one.
        }
    }

    private static int _nextPicture;
    private int _pendingVersion;

    protected override Vector2 MeasureCore(UiContext ui, Vector2 available) => new(Size * ui.Scale, Size * ui.Scale);

    protected override void DrawSelf(UiContext ui, UiDrawList list)
    {
        list.AddRect(Bounds, UiColor.FromBytes(24, 28, 34, 255));
        if (!_hasImage)
        {
            return;
        }

        var slot = Slots.GetValue(ui.Atlas, atlas =>
        {
            var (x, y) = atlas.Allocate(SlotSize, SlotSize);
            return new Slot(x, y);
        });
        if (_pending is not null && slot.Version != _pendingVersion)
        {
            Upload(ui.Atlas, slot, _pending, _pendingWidth, _pendingHeight);
            slot.Version = _pendingVersion;
        }

        var atlasSize = new Vector2(ui.Atlas.Width, ui.Atlas.Height);
        var icon = new IconImage(slot.X / atlasSize.X, slot.Y / atlasSize.Y, (slot.X + SlotSize) / atlasSize.X, (slot.Y + SlotSize) / atlasSize.Y);
        list.AddIcon(icon, Bounds, UiColor.White);
    }

    private static void Upload(UiAtlas atlas, Slot slot, byte[] rgb, int width, int height)
    {
        for (var y = 0; y < SlotSize; y++)
        {
            for (var x = 0; x < SlotSize; x++)
            {
                // Box filter over the source pixels that map onto this texel.
                int x0 = x * width / SlotSize, x1 = Math.Max(x0 + 1, (x + 1) * width / SlotSize);
                int y0 = y * height / SlotSize, y1 = Math.Max(y0 + 1, (y + 1) * height / SlotSize);
                int r = 0, g = 0, b = 0, n = 0;
                for (var sy = y0; sy < y1; sy++)
                {
                    for (var sx = x0; sx < x1; sx++)
                    {
                        var at = ((sy * width) + sx) * 3;
                        r += rgb[at];
                        g += rgb[at + 1];
                        b += rgb[at + 2];
                        n++;
                    }
                }

                atlas.WritePixel(slot.X + x, slot.Y + y, UiColor.FromBytes((byte)(r / n), (byte)(g / n), (byte)(b / n), 255));
            }
        }

        atlas.MarkChanged();
    }

    private sealed class Slot(int x, int y)
    {
        public int X { get; } = x;

        public int Y { get; } = y;

        public int Version { get; set; } = int.MinValue;
    }
}

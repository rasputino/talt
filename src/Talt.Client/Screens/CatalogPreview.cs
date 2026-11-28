using System.Numerics;
using Talt.Client.Ui;
using Talt.Client.Ui.Widgets;
using Talt.Core.Meshing.Models;

namespace Talt.Client.Screens;

/// <summary>Side elevation of the shipped voxel body. Project once when selecting a model.</summary>
internal sealed class CatalogPreview : Widget
{
    private readonly UiColor[] _pixels;
    private readonly int _width;
    private readonly int _height;

    public CatalogPreview(VoxModel model, int paint)
    {
        var alongX = model.SizeX >= model.SizeZ;
        _width = Math.Max(model.SizeX, model.SizeZ);
        _height = model.SizeY;
        _pixels = new UiColor[_width * _height];
        for (var y = 0; y < _height; y++)
        {
            for (var x = 0; x < _width; x++)
            {
                for (var z = 0; z < Math.Min(model.SizeX, model.SizeZ); z++)
                {
                    var index = alongX ? model.Get(x, y, z) : model.Get(z, y, x);
                    if (index != 0)
                    {
                        _pixels[((_height - 1 - y) * _width) + x] = index == 1
                            ? MeterColors.FromRgb((uint)paint) : new UiColor(model.Palette[index] | 0xff000000u);
                        break;
                    }
                }
            }
        }
    }

    protected override Vector2 MeasureCore(UiContext ui, Vector2 available) => new(144 * ui.Scale, 44 * ui.Scale);

    protected override void DrawSelf(UiContext ui, UiDrawList list)
    {
        var scale = MathF.Min(Bounds.Width / _width, Bounds.Height / _height);
        for (var i = 0; i < _pixels.Length; i++)
        {
            if (_pixels[i].A != 0)
            {
                list.AddRect(new RectF(Bounds.X + i % _width * scale, Bounds.Y + i / _width * scale, scale, scale), _pixels[i]);
            }
        }
    }
}

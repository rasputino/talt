using System.Numerics;
using Talt.Client.Rendering;
using Talt.Client.Ui;
using Talt.Core.Construction;
using Talt.Core.Maps;

namespace Talt.Client.Construction;

/// <summary>
/// Turns a construction plan into the ghost preview (US1, research R12): a thin translucent slab on each
/// affected tile at its height after the action, green when the action is allowed, red where it is
/// blocked (or everywhere when the whole action is refused, for example for money), orange where the
/// action leaves a tile out, and a neutral colour on the rest of the dragged area so its whole extent is
/// visible (hand test 2026-11-23). Reuses its buffers.
/// </summary>
internal sealed class PreviewBoxes
{
    private const float Thickness = 0.12f;
    private const float Inset = 0.04f;

    /// <summary>Above this many tiles the unchanged part of the area is not drawn tile by tile.</summary>
    public const int MaxAreaTiles = 16_384;

    private readonly TileSet _marked = new(1024);
    private BoxInstance[] _boxes = new BoxInstance[1024];
    private int _count;

    public ReadOnlySpan<BoxInstance> Boxes => _boxes.AsSpan(0, _count);

    public void Clear() => _count = 0;

    /// <param name="removes">
    /// A removal tool (feature 007 hand test, 2026-11-24): removed pieces are drawn in the removal colour and the
    /// neighbours that only lose a link are not marked, so the preview shows exactly what disappears.
    /// </param>
    public void Build(ConstructionPlan plan, bool allowed, MapData map, Theme theme, bool removes = false)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(theme);
        _count = 0;
        var valid = Linear(removes ? theme.PreviewRemove : theme.PreviewValid);
        var invalid = Linear(theme.PreviewInvalid);
        var skipped = Linear(theme.PreviewSkipped);
        var neutral = Linear(theme.PreviewArea);
        var width = map.Width;
        _marked.Clear();
        foreach (var change in plan.Changes)
        {
            var i = change.Index;
            if (removes && allowed && (change.NewRoad.HasRoad || !change.OldRoad.HasRoad))
            {
                continue; // Keeps its piece (only a link or a passing bridge goes away).
            }

            _marked.Add(i, out _);
            Add(i % width, i / width, Math.Max(change.NewHeight, map.WaterLevels[i]), allowed ? valid : invalid);
        }

        foreach (var problem in plan.Problems)
        {
            var i = problem.Index;
            _marked.Add(i, out _);
            Add(i % width, i / width, map.SurfaceHeightAt(i % width, i / width), problem.Blocking ? invalid : skipped);
        }

        // The rest of the area: neutral, or red when the whole action is refused with nothing
        // tile-specific to show (for example nothing to do).
        var rest = _count == 0 && !allowed ? invalid : neutral;
        if (plan.HasArea && plan.Area.TileCount <= MaxAreaTiles)
        {
            var area = plan.Area;
            for (var z = area.Z0; z <= area.Z1; z++)
            {
                for (var x = area.X0; x <= area.X1; x++)
                {
                    if (_marked.Find(map.Index(x, z)) < 0)
                    {
                        Add(x, z, map.SurfaceHeightAt(x, z), rest);
                    }
                }
            }
        }

        if (_count == 0 && plan.HasArea && !allowed)
        {
            // Larger than drawn tile by tile (only possible when refused as too large): one box for the area.
            var area = plan.Area;
            var height = map.SurfaceHeightAt(area.X0, area.Z0);
            Grow(1);
            _boxes[_count++] = BoxInstance.Create(
                new Vector3(area.X0 + Inset, height, area.Z0 + Inset),
                new Vector3(area.X1 + 1 - Inset, height + Thickness, area.Z1 + 1 - Inset),
                invalid);
        }
    }

    private void Add(int x, int z, int height, Vector4 color)
    {
        Grow(_count + 1);
        _boxes[_count++] = BoxInstance.Create(
            new Vector3(x + Inset, height - 0.02f, z + Inset),
            new Vector3(x + 1 - Inset, height + Thickness, z + 1 - Inset),
            color);
    }

    private void Grow(int needed)
    {
        if (needed > _boxes.Length)
        {
            Array.Resize(ref _boxes, Math.Max(needed, _boxes.Length * 2));
        }
    }

    private static Vector4 Linear(UiColor color)
    {
        static float Channel(byte value)
        {
            var c = value / 255f;
            return c <= 0.04045f ? c / 12.92f : MathF.Pow((c + 0.055f) / 1.055f, 2.4f);
        }

        return new Vector4(Channel(color.R), Channel(color.G), Channel(color.B), color.A / 255f);
    }
}

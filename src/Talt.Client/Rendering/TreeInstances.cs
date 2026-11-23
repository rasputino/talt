using System.Numerics;
using Talt.Core.Maps;

namespace Talt.Client.Rendering;

/// <summary>
/// Turns the trees of the map's scenery data near the camera into boxes (trunk and crown; feature 006,
/// research R12). Trees are gathered ring by ring from the centre, so the nearest ones are kept when
/// the cap is reached. Rebuilt only when the centre moves by <see cref="RebuildStep"/> tiles or the
/// scenery changes; the buffer is reused.
/// </summary>
internal sealed class TreeInstances
{
    public const int DefaultRadius = 160;
    public const int DefaultMaxTrees = 48_000;
    public const int RebuildStep = 16;

    private BoxInstance[] _boxes = [];
    private int _count;
    private int _builtX = int.MinValue;
    private int _builtZ = int.MinValue;
    private int _builtVersion = -1;

    public TreeInstances(int radius = DefaultRadius, int maxTrees = DefaultMaxTrees)
    {
        Radius = radius;
        MaxTrees = maxTrees;
    }

    public int Radius { get; }

    public int MaxTrees { get; }

    public int TreeCount => _count / 2;

    public ReadOnlySpan<BoxInstance> Boxes => _boxes.AsSpan(0, _count);

    /// <summary>Rebuilds when needed; returns true when <see cref="Boxes"/> changed.</summary>
    public bool Update(MapData map, Vector3 center, int sceneryVersion)
    {
        ArgumentNullException.ThrowIfNull(map);
        var cx = (int)MathF.Floor(center.X);
        var cz = (int)MathF.Floor(center.Z);
        if (sceneryVersion == _builtVersion && Math.Abs(cx - _builtX) < RebuildStep && Math.Abs(cz - _builtZ) < RebuildStep)
        {
            return false;
        }

        _builtX = cx;
        _builtZ = cz;
        _builtVersion = sceneryVersion;
        Build(map, cx, cz);
        return true;
    }

    private void Build(MapData map, int cx, int cz)
    {
        if (_boxes.Length < MaxTrees * 2)
        {
            _boxes = new BoxInstance[MaxTrees * 2];
        }

        _count = 0;
        var scenery = map.SceneryData;
        for (var ring = 0; ring <= Radius && _count < MaxTrees * 2; ring++)
        {
            for (var dz = -ring; dz <= ring && _count < MaxTrees * 2; dz++)
            {
                // Whole rows at the ring's top and bottom, only the two ends in between.
                var step = dz == -ring || dz == ring ? 1 : Math.Max(1, 2 * ring);
                for (var dx = -ring; dx <= ring && _count < MaxTrees * 2; dx += step)
                {
                    var x = cx + dx;
                    var z = cz + dz;
                    if (!map.Contains(x, z))
                    {
                        continue;
                    }

                    var value = scenery[map.Index(x, z)];
                    var kind = Scenery.Kind(value);
                    if (kind != 0)
                    {
                        AddTree(x, z, map.HeightAt(x, z), kind, Scenery.Variant(value));
                    }
                }
            }
        }
    }

    private void AddTree(int x, int z, int ground, int kind, int variant)
    {
        // Variant (0–31) gives a little size and position variety; shapes are original.
        var scale = 0.85f + ((variant & 7) * 0.05f);
        var jitterX = (((variant >> 3) & 3) - 1.5f) * 0.08f;
        var jitterZ = ((variant & 3) - 1.5f) * 0.08f;
        var center = new Vector2(x + 0.5f + jitterX, z + 0.5f + jitterZ);
        var (trunkHeight, trunkWidth, crownHeight, crownWidth, trunkColor, crownColor) = kind switch
        {
            Scenery.KindConifer => (1.2f, 0.22f, 3.4f, 0.75f, new Vector4(0.26f, 0.16f, 0.08f, 1f), new Vector4(0.05f, 0.20f, 0.08f, 1f)),
            Scenery.KindBirch => (1.8f, 0.18f, 1.8f, 0.80f, new Vector4(0.80f, 0.78f, 0.72f, 1f), new Vector4(0.30f, 0.52f, 0.14f, 1f)),
            _ => (1.4f, 0.26f, 2.0f, 0.95f, new Vector4(0.30f, 0.19f, 0.09f, 1f), new Vector4(0.12f, 0.36f, 0.10f, 1f)),
        };

        trunkHeight *= scale;
        crownHeight *= scale;
        crownWidth *= scale;
        var y = (float)ground;
        var trunkHalf = trunkWidth / 2f;
        var crownHalf = crownWidth / 2f;
        _boxes[_count++] = BoxInstance.Create(
            new Vector3(center.X - trunkHalf, y, center.Y - trunkHalf),
            new Vector3(center.X + trunkHalf, y + trunkHeight, center.Y + trunkHalf),
            trunkColor);
        _boxes[_count++] = BoxInstance.Create(
            new Vector3(center.X - crownHalf, y + (trunkHeight * 0.8f), center.Y - crownHalf),
            new Vector3(center.X + crownHalf, y + (trunkHeight * 0.8f) + crownHeight, center.Y + crownHalf),
            crownColor);
    }
}

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
    private ModelPlacement[] _placements = [];
    private int _placementCount;
    private readonly ModelEntry?[] _kindModels = new ModelEntry?[8];
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

    public int TreeCount => (_count / 2) + _placementCount;

    /// <summary>Trees drawn as voxel models (feature 018), added to each frame's model batch.</summary>
    public ReadOnlySpan<ModelPlacement> Placements => _placements.AsSpan(0, _placementCount);

    /// <summary>Uses voxel models for the tree kinds that have one (keys <c>tree:broadleaf|conifer|birch</c>).</summary>
    public void UseModels(ModelLibrary? library)
    {
        _kindModels[Scenery.KindBroadleaf] = Find(library, "tree:broadleaf");
        _kindModels[Scenery.KindConifer] = Find(library, "tree:conifer");
        _kindModels[Scenery.KindBirch] = Find(library, "tree:birch");
        _builtVersion = -1; // Rebuild with the models.
    }

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

        if (_placements.Length < MaxTrees)
        {
            _placements = new ModelPlacement[MaxTrees];
        }

        _count = 0;
        _placementCount = 0;
        var scenery = map.SceneryData;
        for (var ring = 0; ring <= Radius && (_count / 2) + _placementCount < MaxTrees; ring++)
        {
            for (var dz = -ring; dz <= ring && (_count / 2) + _placementCount < MaxTrees; dz++)
            {
                // Whole rows at the ring's top and bottom, only the two ends in between.
                var step = dz == -ring || dz == ring ? 1 : Math.Max(1, 2 * ring);
                for (var dx = -ring; dx <= ring && (_count / 2) + _placementCount < MaxTrees; dx += step)
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

    private static ModelEntry? Find(ModelLibrary? library, string key) =>
        library is not null && library.TryGet(key, out var entry) ? entry : null;

    private void AddTree(int x, int z, int ground, int kind, int variant)
    {
        if ((uint)kind < (uint)_kindModels.Length && _kindModels[kind] is { } model)
        {
            // Variant (0–31): model variant, a small offset in the tile and a turn.
            var offset = new Vector2((((variant >> 3) & 3) - 1.5f) * 0.1f, ((variant & 3) - 1.5f) * 0.1f);
            _placements[_placementCount++] = new ModelPlacement(
                model.Variant(variant + (x * 7) + (z * 13)),
                new Vector3(x + 0.5f + offset.X, ground, z + 0.5f + offset.Y),
                (variant & 3) * MathF.PI / 2f,
                variant / 31f);
            return;
        }

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

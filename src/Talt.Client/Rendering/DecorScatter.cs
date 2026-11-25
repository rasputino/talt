using System.Numerics;
using Talt.Core.Construction;
using Talt.Core.Maps;

namespace Talt.Client.Rendering;

/// <summary>Decoration on one tile (data-model.md Decoration): none, a bush or a rock.</summary>
internal enum DecorKind : byte
{
    None,
    Bush,
    Rock,
}

/// <summary>
/// Bushes and rocks on free land (feature 018, research R9). Placement is a pure function of the tile and
/// the map seed, so the same map always shows them in the same places; they are decoration only (not
/// simulation state, never saved). Gathered ring by ring around the camera target like the trees and rebuilt
/// only when the target moves <see cref="RebuildStep"/> tiles, the density changes or the map's scenery or
/// objects change.
/// </summary>
internal sealed class DecorScatter
{
    public const int RebuildStep = 16;
    public const int DefaultRadius = 120;
    public const int DefaultMax = 40_000;

    private readonly int _radius;
    private readonly int _max;
    private ModelPlacement[] _placements = [];
    private int _count;
    private int _builtX = int.MinValue;
    private int _builtZ = int.MinValue;
    private int _builtVersion = -1;
    private float _builtDensity = -1f;
    private ModelEntry? _bushes;
    private ModelEntry? _rocks;

    public DecorScatter(int radius = DefaultRadius, int max = DefaultMax)
    {
        _radius = radius;
        _max = max;
    }

    public ReadOnlySpan<ModelPlacement> Placements => _placements.AsSpan(0, _count);

    public int Count => _count;

    /// <summary>Uses the <c>bush</c> and <c>rock</c> models; without them nothing is placed.</summary>
    public void UseModels(ModelLibrary? library)
    {
        _bushes = library is not null && library.TryGet("bush", out var bushes) ? bushes : null;
        _rocks = library is not null && library.TryGet("rock", out var rocks) ? rocks : null;
        _builtVersion = -1;
    }

    /// <summary>
    /// What a tile shows at <paramref name="density"/> (0–1), from the tile's terrain, flags, scenery and object.
    /// Bushes grow on buildable grass, rocks on grass, rock and snow; never on water, beaches, roads,
    /// buildings, industries or under trees.
    /// </summary>
    public static DecorKind At(MapData map, TileObjects? objects, int x, int z, float density, out float jitter)
    {
        ArgumentNullException.ThrowIfNull(map);
        jitter = 0f;
        if (density <= 0f || !map.Contains(x, z))
        {
            return DecorKind.None;
        }

        var index = map.Index(x, z);
        if (map.IsWater(index) || Scenery.Kind(map.SceneryData[index]) != 0 || (objects is not null && objects.KindAt(index) != TileObjectKind.None))
        {
            return DecorKind.None;
        }

        var hash = Hash(x, z, map.Settings.Seed);
        var roll = (hash & 0xffff) / 65536f;
        jitter = ((hash >> 16) & 0xffff) / 65536f;
        var terrain = map.TerrainAt(x, z);
        var buildable = map.FlagsAt(x, z).HasFlag(TileTraits.Buildable);
        return terrain switch
        {
            TerrainType.Grass when buildable && roll < 0.10f * density => DecorKind.Bush,
            TerrainType.Grass when roll >= 0.10f * density && roll < 0.13f * density => DecorKind.Rock,
            TerrainType.Rock or TerrainType.Snow when roll < 0.12f * density => DecorKind.Rock,
            _ => DecorKind.None,
        };
    }

    /// <summary>Rebuilds when needed; returns true when <see cref="Placements"/> changed.</summary>
    public bool Update(MapData map, TileObjects? objects, Vector3 center, int sceneryVersion, float density)
    {
        ArgumentNullException.ThrowIfNull(map);
        var cx = (int)MathF.Floor(center.X);
        var cz = (int)MathF.Floor(center.Z);
        if (sceneryVersion == _builtVersion && density == _builtDensity
            && Math.Abs(cx - _builtX) < RebuildStep && Math.Abs(cz - _builtZ) < RebuildStep)
        {
            return false;
        }

        (_builtX, _builtZ, _builtVersion, _builtDensity) = (cx, cz, sceneryVersion, density);
        if (_placements.Length < _max)
        {
            _placements = new ModelPlacement[_max];
        }

        _count = 0;
        if (_bushes is null && _rocks is null)
        {
            return true;
        }

        for (var ring = 0; ring <= _radius && _count < _max; ring++)
        {
            for (var dz = -ring; dz <= ring && _count < _max; dz++)
            {
                var step = dz == -ring || dz == ring ? 1 : Math.Max(1, 2 * ring);
                for (var dx = -ring; dx <= ring && _count < _max; dx += step)
                {
                    var x = cx + dx;
                    var z = cz + dz;
                    var kind = At(map, objects, x, z, density, out var jitter);
                    var entry = kind switch { DecorKind.Bush => _bushes, DecorKind.Rock => _rocks, _ => null };
                    if (entry is null)
                    {
                        continue;
                    }

                    var hash = Hash(x, z, map.Settings.Seed ^ 0x5bd1);
                    var offset = new Vector2(((hash & 0xff) / 255f) - 0.5f, (((hash >> 8) & 0xff) / 255f) - 0.5f) * 0.5f;
                    _placements[_count++] = new ModelPlacement(
                        entry.Variant((int)(hash >> 16)),
                        new Vector3(x + 0.5f + offset.X, map.HeightAt(x, z), z + 0.5f + offset.Y),
                        jitter * MathF.Tau,
                        jitter);
                }
            }
        }

        return true;
    }

    private static uint Hash(int x, int z, long seed)
    {
        var h = unchecked((uint)seed ^ (uint)(seed >> 32) ^ ((uint)x * 0x27d4eb2du) ^ ((uint)z * 0x165667b1u));
        h ^= h >> 15;
        h *= 0x85ebca6bu;
        h ^= h >> 13;
        h *= 0xc2b2ae35u;
        h ^= h >> 16;
        return h;
    }
}

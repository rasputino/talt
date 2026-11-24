using System.Numerics;
using Talt.Client.Rendering;
using Talt.Core.Industries;
using Talt.Core.Terrain;
using Talt.Core.Voxels;

namespace Talt.Client.Picking;

/// <summary>A ray in world space; <see cref="Direction"/> is normalized.</summary>
internal readonly record struct Ray(Vector3 Origin, Vector3 Direction)
{
    public Vector3 At(float distance) => Origin + Direction * distance;

    /// <summary>
    /// The ray under a cursor position (framebuffer pixels), from the inverse view-projection matrix
    /// (WebGPU depth range 0..1).
    /// </summary>
    public static Ray FromScreen(Matrix4x4 viewProjection, Vector2 cursor, Vector2 viewport)
    {
        Matrix4x4.Invert(viewProjection, out var inverse);
        var x = cursor.X / viewport.X * 2f - 1f;
        var y = 1f - cursor.Y / viewport.Y * 2f;
        var near = Unproject(new Vector4(x, y, 0f, 1f), inverse);
        var far = Unproject(new Vector4(x, y, 1f, 1f), inverse);
        return new Ray(near, Vector3.Normalize(far - near));
    }

    private static Vector3 Unproject(Vector4 clip, Matrix4x4 inverse)
    {
        var world = Vector4.Transform(clip, inverse);
        return new Vector3(world.X, world.Y, world.Z) / world.W;
    }
}

/// <summary>Kinds of objects the cursor can point at (later features add stations, buildings...).</summary>
internal enum PickKind
{
    None,
    Tile,
    Road,
    Vehicle,
    Industry,

    /// <summary>A town building or a town label (feature 008); <see cref="PickResult.TownId"/> is the town.</summary>
    Town,
}

/// <summary>What is under the cursor (data-model.md Pick result).</summary>
internal readonly record struct PickResult(
    PickKind Kind,
    int TileX,
    int TileZ,
    int Height,
    VoxelMaterial Material,
    int VehicleId,
    int IndustryId,
    Vector3 BoundsMin,
    Vector3 BoundsMax,
    float Distance,
    int TownId = -1)
{
    public static readonly PickResult None = new(PickKind.None, 0, 0, 0, VoxelMaterial.Air, -1, -1, default, default, float.PositiveInfinity);

    /// <summary>Identity for "one window per object": same object, same key.</summary>
    public string Key => Kind switch
    {
        PickKind.Vehicle => $"vehicle:{VehicleId}",
        PickKind.Industry => $"industry:{IndustryId}",
        PickKind.Town => $"town:{TownId}",
        PickKind.Road => $"road:{TileX},{TileZ}",
        PickKind.Tile => $"tile:{TileX},{TileZ}",
        _ => "none",
    };

    public bool SameObject(in PickResult other) => Kind switch
    {
        PickKind.Vehicle => other.Kind == Kind && VehicleId == other.VehicleId,
        PickKind.Industry => other.Kind == Kind && IndustryId == other.IndustryId,
        PickKind.Town => other.Kind == Kind && TownId == other.TownId && BoundsMin == other.BoundsMin,
        PickKind.None => other.Kind == Kind,
        _ => other.Kind == Kind && TileX == other.TileX && TileZ == other.TileZ,
    };
}

/// <summary>Everything layers need to test a ray against this frame's world.</summary>
internal sealed class PickContext
{
    public required Heightmap Heightmap { get; init; }

    public required VoxelWorld World { get; init; }

    /// <summary>Vehicles as drawn this frame and their ids.</summary>
    public VehicleInstance[] Vehicles { get; set; } = [];

    public int[] VehicleIds { get; set; } = [];

    public int VehicleCount { get; set; }

    /// <summary>Road pieces of the game (feature 007); null in tests that only paint road voxels.</summary>
    public Talt.Core.Roads.RoadLayer? Roads { get; set; }

    /// <summary>Towns of the game (feature 008); null for map-less games and tests.</summary>
    public Talt.Core.Towns.TownRegistry? Towns { get; set; }

    /// <summary>Industries of the game (feature 009).</summary>
    public Talt.Core.Industries.IndustryFleet? Industries { get; set; }

    /// <summary>The terrain hit of this ray (computed first by <see cref="MapPicker"/>).</summary>
    public PickResult Terrain { get; set; } = PickResult.None;
}

/// <summary>
/// One kind of pickable object (FR-011). The picker asks every layer and keeps the highest priority
/// visible hit; ties go to the nearest.
/// </summary>
internal interface IPickLayer
{
    int Priority { get; }

    bool TryPick(in Ray ray, PickContext context, out PickResult result);
}

/// <summary>Terrain tiles: exact walk over the voxel columns the ray crosses (research R5).</summary>
internal sealed class TerrainPickLayer : IPickLayer
{
    public int Priority => 0;

    public bool TryPick(in Ray ray, PickContext context, out PickResult result)
    {
        ArgumentNullException.ThrowIfNull(context);
        result = context.Terrain;
        return result.Kind != PickKind.None;
    }

    /// <summary>
    /// First tile column hit by the ray (2D DDA over tiles, testing the column's side and top), or none.
    /// Visits at most every column the ray crosses inside the map, so it is exact at any zoom.
    /// </summary>
    public static PickResult Cast(in Ray ray, Heightmap map, VoxelWorld world, float maxHeight = Chunk.Size)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(world);
        if (!ClipToBox(ray, Vector3.Zero, new Vector3(map.Width, maxHeight, map.Depth), out var tStart, out var tEnd))
        {
            return PickResult.None;
        }

        var start = ray.At(tStart + 1e-4f);
        var x = Math.Clamp((int)MathF.Floor(start.X), 0, map.Width - 1);
        var z = Math.Clamp((int)MathF.Floor(start.Z), 0, map.Depth - 1);
        var d = ray.Direction;
        var stepX = d.X > 0 ? 1 : -1;
        var stepZ = d.Z > 0 ? 1 : -1;
        var tDeltaX = d.X != 0 ? MathF.Abs(1f / d.X) : float.PositiveInfinity;
        var tDeltaZ = d.Z != 0 ? MathF.Abs(1f / d.Z) : float.PositiveInfinity;
        var tMaxX = d.X != 0 ? ((d.X > 0 ? x + 1 : x) - ray.Origin.X) / d.X : float.PositiveInfinity;
        var tMaxZ = d.Z != 0 ? ((d.Z > 0 ? z + 1 : z) - ray.Origin.Z) / d.Z : float.PositiveInfinity;
        var t0 = tStart;
        var limit = map.Width + map.Depth + 2;
        for (var i = 0; i < limit; i++)
        {
            var t1 = MathF.Min(MathF.Min(tMaxX, tMaxZ), tEnd);
            var height = map.Get(x, z);
            var y0 = ray.Origin.Y + d.Y * t0;
            var y1 = ray.Origin.Y + d.Y * t1;
            if (MathF.Min(y0, y1) <= height)
            {
                // Enters the column from the side (already below its top) or through the top face.
                var hit = y0 <= height ? t0 : (height - ray.Origin.Y) / d.Y;
                var material = height > 0 ? world.GetVoxel(x, height - 1, z) : VoxelMaterial.Air;
                // Outline: a flat square on the tile's top face (a full voxel box looked like it floated).
                return new PickResult(PickKind.Tile, x, z, height, material, -1, -1,
                    new Vector3(x, height - 0.02f, z), new Vector3(x + 1, height + 0.02f, z + 1), hit);
            }

            if (t1 >= tEnd)
            {
                break;
            }

            if (tMaxX < tMaxZ)
            {
                x += stepX;
                t0 = tMaxX;
                tMaxX += tDeltaX;
            }
            else
            {
                z += stepZ;
                t0 = tMaxZ;
                tMaxZ += tDeltaZ;
            }

            if ((uint)x >= map.Width || (uint)z >= map.Depth)
            {
                break;
            }
        }

        return PickResult.None;
    }

    /// <summary>Slab test: the part of the ray inside an axis-aligned box.</summary>
    public static bool ClipToBox(in Ray ray, Vector3 min, Vector3 max, out float tEnter, out float tExit)
    {
        tEnter = 0f;
        tExit = float.PositiveInfinity;
        for (var axis = 0; axis < 3; axis++)
        {
            var origin = ray.Origin[axis];
            var direction = ray.Direction[axis];
            if (MathF.Abs(direction) < 1e-9f)
            {
                if (origin < min[axis] || origin > max[axis])
                {
                    return false;
                }

                continue;
            }

            var a = (min[axis] - origin) / direction;
            var b = (max[axis] - origin) / direction;
            if (a > b)
            {
                (a, b) = (b, a);
            }

            tEnter = MathF.Max(tEnter, a);
            tExit = MathF.Min(tExit, b);
            if (tEnter > tExit)
            {
                return false;
            }
        }

        return true;
    }
}

/// <summary>Roads: the hit tile holds a road piece (feature 007), or, without road data, its top voxel is road.</summary>
internal sealed class RoadPickLayer : IPickLayer
{
    public int Priority => 50;

    public bool TryPick(in Ray ray, PickContext context, out PickResult result)
    {
        ArgumentNullException.ThrowIfNull(context);
        var terrain = context.Terrain;
        var hasRoad = context.Roads is { } roads
            ? terrain.Kind == PickKind.Tile && roads.Contains(terrain.TileX, terrain.TileZ) && roads.HasRoad(roads.Index(terrain.TileX, terrain.TileZ))
            : terrain.Kind == PickKind.Tile && terrain.Material == VoxelMaterial.Road;
        if (hasRoad)
        {
            result = terrain with { Kind = PickKind.Road };
            return true;
        }

        result = PickResult.None;
        return false;
    }
}

/// <summary>Vehicles: ray against each drawn vehicle's box (the same size as the vehicle shader draws).</summary>
internal sealed class VehiclePickLayer : IPickLayer
{
    /// <summary>Vehicle size along travel, height and width; must match VEHICLE_SIZE in vehicles.wgsl.</summary>
    public static readonly Vector3 Size = new(0.9f, 0.6f, 0.42f);

    /// <summary>Boxes are tested slightly larger so small vehicles are easier to point at.</summary>
    private const float Inflate = 1.2f;

    public int Priority => 100;

    public bool TryPick(in Ray ray, PickContext context, out PickResult result)
    {
        ArgumentNullException.ThrowIfNull(context);
        result = PickResult.None;
        var nearest = float.PositiveInfinity;
        var best = -1;
        var vehicles = context.Vehicles;

        // Only the part of the ray in front of the terrain can show a vehicle. Its footprint on the
        // ground is a segment; vehicles farther than a vehicle length from it are skipped with a cheap
        // 2D test before the exact box test (50 000 vehicles per frame, research R5).
        var length = context.Terrain.Kind != PickKind.None ? context.Terrain.Distance + 2f : 100_000f;
        var ax = ray.Origin.X;
        var az = ray.Origin.Z;
        var abx = ray.Direction.X * length;
        var abz = ray.Direction.Z * length;
        var abLengthSquared = abx * abx + abz * abz;
        var inverseLengthSquared = abLengthSquared > 1e-6f ? 1f / abLengthSquared : 0f;
        const float reach = 1.2f; // Half the inflated vehicle diagonal, rounded up.

        // Bounding rectangle of the footprint: two comparisons reject almost every vehicle.
        var minX = MathF.Min(ax, ax + abx) - reach;
        var maxX = MathF.Max(ax, ax + abx) + reach;
        var minZ = MathF.Min(az, az + abz) - reach;
        var maxZ = MathF.Max(az, az + abz) + reach;
        for (var v = 0; v < context.VehicleCount; v++)
        {
            var p = vehicles[v].Position;
            // Non-short-circuit &: one well-predicted branch instead of four random ones.
            if (!((p.X >= minX) & (p.X <= maxX) & (p.Z >= minZ) & (p.Z <= maxZ)))
            {
                continue;
            }

            var px = p.X - ax;
            var pz = p.Z - az;
            var t = Math.Clamp((px * abx + pz * abz) * inverseLengthSquared, 0f, 1f);
            var dx = px - t * abx;
            var dz = pz - t * abz;
            if (dx * dx + dz * dz > reach * reach)
            {
                continue;
            }

            var (min, max) = Box(vehicles[v]);
            if (TerrainPickLayer.ClipToBox(ray, min, max, out var hit, out _) && hit < nearest)
            {
                nearest = hit;
                best = v;
            }
        }

        if (best < 0)
        {
            return false;
        }

        var (boxMin, boxMax) = Box(vehicles[best], 1f);
        result = new PickResult(PickKind.Vehicle, (int)MathF.Floor(vehicles[best].Position.X), (int)MathF.Floor(vehicles[best].Position.Z),
            0, VoxelMaterial.Air, context.VehicleIds[best], -1, boxMin, boxMax, nearest);
        return true;
    }

    public static (Vector3 Min, Vector3 Max) Box(in VehicleInstance vehicle, float inflate = Inflate)
    {
        var size = vehicle.Position.W > 0.5f ? new Vector3(Size.Z, Size.Y, Size.X) : Size;
        size *= inflate;
        var basePoint = new Vector3(vehicle.Position.X, vehicle.Position.Y, vehicle.Position.Z);
        return (basePoint - new Vector3(size.X / 2, 0, size.Z / 2), basePoint + new Vector3(size.X / 2, size.Y, size.Z / 2));
    }
}

/// <summary>
/// Industries: ray against the footprint box of each open industry (feature 009). Priority sits
/// between roads and vehicles, so a vehicle in front still wins.
/// </summary>
internal sealed class IndustryPickLayer : IPickLayer
{
    public int Priority => 75;

    public bool TryPick(in Ray ray, PickContext context, out PickResult result)
    {
        ArgumentNullException.ThrowIfNull(context);
        result = PickResult.None;
        var industries = context.Industries;
        if (industries is null || industries.Count == 0)
        {
            return false;
        }

        var nearest = float.PositiveInfinity;
        var best = -1;
        var bestMin = default(Vector3);
        var bestMax = default(Vector3);
        for (var slot = 0; slot < industries.Count; slot++)
        {
            if (!industries.FootprintBounds(slot, out var minX, out var minZ, out var maxX, out var maxZ))
            {
                continue;
            }

            var kind = industries.Content.IndustryKind(industries.KindIndexAt(slot));
            var ground = context.Heightmap.Get(industries.XAt(slot), industries.ZAt(slot));
            var min = new Vector3(minX, ground, minZ);
            var max = new Vector3(maxX, ground + kind.ModelHeight, maxZ);
            if (TerrainPickLayer.ClipToBox(ray, min, max, out var hit, out _) && hit < nearest)
            {
                nearest = hit;
                best = slot;
                bestMin = min;
                bestMax = max;
            }
        }

        if (best < 0)
        {
            return false;
        }

        var height = (int)bestMin.Y;
        result = new PickResult(
            PickKind.Industry,
            industries.XAt(best),
            industries.ZAt(best),
            height,
            VoxelMaterial.Air,
            -1,
            industries.IdAt(best),
            bestMin,
            bestMax,
            nearest);
        return true;
    }
}

/// <summary>
/// Town buildings (feature 008, research R13): walks the tiles under the ray's ground track (2D DDA) where it is
/// low enough to meet a building, and tests the box of each building found; the first hit is the nearest. Same
/// priority as industries.
/// </summary>
internal sealed class TownPickLayer : IPickLayer
{
    /// <summary>Most tiles walked per pick (keeps a pick cheap when looking along the ground).</summary>
    private const int MaxSteps = 4096;

    private const float MaxBuildingHeight = (32 * TownInstances.StoreyHeight) + 1f;

    public int Priority => 75;

    public bool TryPick(in Ray ray, PickContext context, out PickResult result)
    {
        ArgumentNullException.ThrowIfNull(context);
        result = PickResult.None;
        var towns = context.Towns;
        if (towns is null || towns.Buildings.Count == 0)
        {
            return false;
        }

        // The part of the ray below the highest possible roof and above the ground (or up to the terrain hit).
        var top = context.World.TopChunkY * Chunk.Size + Chunk.Size + MaxBuildingHeight;
        if (!TerrainPickLayer.ClipToBox(ray, Vector3.Zero, new Vector3(context.Heightmap.Width, top, context.Heightmap.Depth), out var t0, out var t1))
        {
            return false;
        }

        if (context.Terrain.Kind != PickKind.None)
        {
            t1 = Math.Min(t1, context.Terrain.Distance + 1f);
        }

        var start = ray.At(t0);
        var x = (int)MathF.Floor(start.X);
        var z = (int)MathF.Floor(start.Z);
        var stepX = ray.Direction.X >= 0 ? 1 : -1;
        var stepZ = ray.Direction.Z >= 0 ? 1 : -1;
        var deltaX = ray.Direction.X == 0 ? float.PositiveInfinity : MathF.Abs(1f / ray.Direction.X);
        var deltaZ = ray.Direction.Z == 0 ? float.PositiveInfinity : MathF.Abs(1f / ray.Direction.Z);
        var nextX = ray.Direction.X == 0 ? float.PositiveInfinity : t0 + ((stepX > 0 ? (x + 1 - start.X) : (start.X - x)) * deltaX);
        var nextZ = ray.Direction.Z == 0 ? float.PositiveInfinity : t0 + ((stepZ > 0 ? (z + 1 - start.Z) : (start.Z - z)) * deltaZ);
        var t = t0;
        var buildings = towns.Buildings;
        for (var step = 0; step < MaxSteps && t <= t1; step++)
        {
            if (towns.TryGetBuildingAt(x, z, out var id))
            {
                var slot = buildings.SlotOf(id);
                var type = buildings.TypeDefinitionAt(slot);
                var min = new Vector3(buildings.XAt(slot), buildings.BaseHeightAt(slot), buildings.ZAt(slot));
                var max = new Vector3(min.X + type.Width, min.Y + (type.Height * TownInstances.StoreyHeight) + 0.6f, min.Z + type.Depth);
                if (TerrainPickLayer.ClipToBox(ray, min, max, out var hit, out _))
                {
                    result = new PickResult(PickKind.Town, buildings.XAt(slot), buildings.ZAt(slot), (int)min.Y, VoxelMaterial.Air, -1, -1, min, max, hit, buildings.TownAt(slot));
                    return true;
                }
            }

            if (nextX < nextZ)
            {
                t = nextX;
                nextX += deltaX;
                x += stepX;
            }
            else
            {
                t = nextZ;
                nextZ += deltaZ;
                z += stepZ;
            }
        }

        return false;
    }
}

/// <summary>
/// Finds the object under the cursor every frame (FR-008, research R5): the terrain is cast first,
/// then every registered layer is asked; hits behind the terrain are hidden and ignored.
/// </summary>
internal sealed class MapPicker
{
    /// <summary>A hit may be this far behind the terrain hit and still count (it stands on it).</summary>
    private const float OcclusionTolerance = 0.75f;

    private readonly List<IPickLayer> _layers = [new TerrainPickLayer(), new RoadPickLayer(), new VehiclePickLayer(), new IndustryPickLayer(), new TownPickLayer()];

    /// <summary>Adds a kind of pickable object (stations, buildings... in later features).</summary>
    public void Register(IPickLayer layer) => _layers.Add(layer ?? throw new ArgumentNullException(nameof(layer)));

    public PickResult Pick(in Ray ray, PickContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        // Up to the top of the highest chunk: hills above the first 32 voxels must be pickable too.
        context.Terrain = TerrainPickLayer.Cast(ray, context.Heightmap, context.World, (context.World.TopChunkY + 1) * Chunk.Size);
        var visibleUpTo = context.Terrain.Kind == PickKind.None ? float.PositiveInfinity : context.Terrain.Distance + OcclusionTolerance;
        var best = PickResult.None;
        var bestPriority = int.MinValue;
        foreach (var layer in _layers)
        {
            if (!layer.TryPick(ray, context, out var hit) || hit.Distance > visibleUpTo)
            {
                continue;
            }

            if (layer.Priority > bestPriority || (layer.Priority == bestPriority && hit.Distance < best.Distance))
            {
                best = hit;
                bestPriority = layer.Priority;
            }
        }

        return best;
    }
}

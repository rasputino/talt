using System.Numerics;
using System.Runtime.InteropServices;
using Talt.Core.Meshing.Models;

namespace Talt.Client.Rendering;

/// <summary>
/// One drawn model (32 bytes; data-model.md ModelInstance, matches <c>models.wgsl</c>).
/// </summary>
/// <param name="PositionYaw">Pivot (centre of the base) in world units; w: yaw in radians.</param>
/// <param name="TintVariation">Linear RGB multiplied into paint colours; w: a 0–1 value per object (lit windows, colour jitter).</param>
[StructLayout(LayoutKind.Sequential)]
internal readonly record struct ModelInstance(Vector4 PositionYaw, Vector4 TintVariation);

/// <summary>A run of instances drawn with one call: a model variant at one detail level.</summary>
internal readonly record struct ModelBucket(int Variant, int Level, int FirstInstance, int Count);

/// <summary>
/// Gathers the model instances of a frame (feature 018, data-model.md ModelBatch, research R6): each
/// <see cref="Add"/> culls the instance against the camera frustum (enlarged by the shadow the object casts,
/// so off-screen objects still shadow the screen) and the fog, picks its detail level from its distance,
/// and <see cref="End"/> sorts the instances by (variant, level) with a counting sort, so every non-empty
/// bucket is one instanced draw. Arrays grow by doubling and are reused: no allocation per frame once warm.
/// </summary>
internal sealed class ModelBatch
{
    /// <summary>How far, relative to its height, an object's shadow may reach sideways.</summary>
    public const float ShadowReach = 0.7f;

    private readonly int _variants;
    private ModelInstance[] _pending = new ModelInstance[1024];
    private int[] _keys = new int[1024];
    private ModelInstance[] _sorted = new ModelInstance[1024];
    private readonly int[] _counts;
    private readonly List<ModelBucket> _buckets = [];
    private Frustum _frustum;
    private Vector3 _camera;
    private float _level1;
    private float _level2;
    private float _level3;
    private float _maxDistance;
    private bool _cull;

    public ModelBatch(int variants)
    {
        _variants = Math.Max(1, variants);
        _counts = new int[(_variants * ModelMesh.LevelCount) + 1];
    }

    /// <summary>Instances added since <see cref="Begin"/> (after culling).</summary>
    public int Count { get; private set; }

    /// <summary>Instances culled since <see cref="Begin"/>.</summary>
    public int Culled { get; private set; }

    /// <summary>Instances per detail level of the last <see cref="End"/>.</summary>
    public (int Level0, int Level1, int Level2, int Level3) PerLevel { get; private set; }

    /// <summary>Sorted instances after <see cref="End"/>.</summary>
    public ReadOnlySpan<ModelInstance> Instances => _sorted.AsSpan(0, Count);

    /// <summary>Non-empty buckets after <see cref="End"/>, in variant and level order.</summary>
    public IReadOnlyList<ModelBucket> Buckets => _buckets;

    /// <summary>
    /// Starts a frame. Objects nearer than <paramref name="level1Distance"/> use level 0, then level 1 up to
    /// <paramref name="level2Distance"/>, level 2 up to <paramref name="level3Distance"/>, then level 3; objects
    /// beyond <paramref name="maxDistance"/> are dropped.
    /// </summary>
    public void Begin(Matrix4x4 viewProjection, Vector3 cameraPosition, float level1Distance, float level2Distance, float level3Distance, float maxDistance, bool cull = true)
    {
        _frustum = new Frustum(viewProjection);
        _camera = cameraPosition;
        _level1 = level1Distance;
        _level2 = level2Distance;
        _level3 = level3Distance;
        _maxDistance = maxDistance;
        _cull = cull;
        Count = 0;
        Culled = 0;
    }

    /// <summary>Adds one object; returns false when it was culled.</summary>
    public bool Add(ModelMeshHandle model, Vector3 position, float yaw, Vector3 tint, float variation)
    {
        if (!model.IsValid || model.Id >= _variants)
        {
            return false;
        }

        var centre = position + new Vector3(0f, model.Height * 0.5f, 0f);
        var distance = Vector3.Distance(centre, _camera);
        if (_cull)
        {
            var reach = model.Radius + (model.Height * ShadowReach);
            var extent = new Vector3(reach, (model.Height * 0.5f) + 0.1f, reach);
            if (distance - reach > _maxDistance || !_frustum.Intersects(centre - extent, centre + extent))
            {
                Culled++;
                return false;
            }
        }

        var level = distance < _level1 ? 0 : distance < _level2 ? 1 : distance < _level3 ? 2 : 3;
        if (Count == _pending.Length)
        {
            Array.Resize(ref _pending, Count * 2);
            Array.Resize(ref _keys, Count * 2);
        }

        _pending[Count] = new ModelInstance(new Vector4(position, yaw), new Vector4(tint, variation));
        _keys[Count] = (model.Id * ModelMesh.LevelCount) + level;
        Count++;
        return true;
    }

    /// <summary>Sorts the instances into buckets.</summary>
    public void End()
    {
        if (_sorted.Length < Count)
        {
            _sorted = new ModelInstance[_pending.Length];
        }

        Array.Clear(_counts);
        for (var i = 0; i < Count; i++)
        {
            _counts[_keys[i] + 1]++;
        }

        _buckets.Clear();
        int level0 = 0, level1 = 0, level2 = 0, level3 = 0;
        for (var k = 0; k < _counts.Length - 1; k++)
        {
            var count = _counts[k + 1];
            var start = _counts[k];
            if (count > 0)
            {
                _buckets.Add(new ModelBucket(k / ModelMesh.LevelCount, k % ModelMesh.LevelCount, start, count));
                switch (k % ModelMesh.LevelCount)
                {
                    case 0: level0 += count; break;
                    case 1: level1 += count; break;
                    case 2: level2 += count; break;
                    default: level3 += count; break;
                }
            }

            _counts[k + 1] = start + count; // Prefix sum: start of the next bucket.
        }

        // _counts[k] is now the start of bucket k; place each instance and advance its bucket's cursor.
        for (var i = 0; i < Count; i++)
        {
            _sorted[_counts[_keys[i]]++] = _pending[i];
        }

        PerLevel = (level0, level1, level2, level3);
    }
}

/// <summary>A static model placement kept between frames (trees, decoration) and added to each frame's batch.</summary>
internal readonly record struct ModelPlacement(ModelMeshHandle Model, Vector3 Position, float Yaw, float Variation);

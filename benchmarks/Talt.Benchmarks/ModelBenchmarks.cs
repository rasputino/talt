using System.Numerics;
using BenchmarkDotNet.Attributes;
using Talt.Client.Rendering;
using Talt.Core.Construction;
using Talt.Core.Maps;

namespace Talt.Benchmarks;

/// <summary>
/// Feature 018 CPU costs: loading and meshing every shipped voxel model (start-up, SC-005), gathering and
/// sorting 60 000 model instances into draw buckets (per frame), and scattering bushes and rocks around the
/// camera on a 1024 × 1024 map (on camera moves).
/// </summary>
[MemoryDiagnoser]
public class ModelBenchmarks
{
    private const int Instances = 60_000;

    private ModelLibrary _library = null!;
    private ModelBatch _batch = null!;
    private ModelMeshHandle[] _handles = null!;
    private Vector3[] _positions = null!;
    private Matrix4x4 _viewProjection;
    private MapData _map = null!;
    private TileObjects _objects = null!;
    private DecorScatter _decor = null!;
    private int _version;

    [GlobalSetup]
    public void Setup()
    {
        _library = ModelLibrary.LoadShipped();
        _batch = new ModelBatch(_library.VariantCount);
        var keys = _library.Keys.ToArray();
        var random = new Random(18);
        _handles = new ModelMeshHandle[Instances];
        _positions = new Vector3[Instances];
        for (var i = 0; i < Instances; i++)
        {
            _library.TryGet(keys[random.Next(keys.Length)], out var entry);
            _handles[i] = entry.Variant(i);
            _positions[i] = new Vector3(random.Next(0, 600), 10, random.Next(0, 600));
        }

        var camera = new Vector3(300, 250, 600);
        _viewProjection = Matrix4x4.CreateLookAt(camera, new Vector3(300, 0, 300), Vector3.UnitY)
            * Matrix4x4.CreatePerspectiveFieldOfView(0.61f, 16f / 9f, 5f, 3000f);
        _map = MapGenerator.Generate(new MapSettings(1024, 1024, Seed: 2026));
        _objects = new TileObjects(_map.Width, _map.Depth);
        _decor = new DecorScatter();
        _decor.UseModels(_library);
    }

    [Benchmark]
    public int LoadAndMeshAllModels() => ModelLibrary.LoadShipped().VariantCount;

    [Benchmark]
    public int Batch60000Instances()
    {
        _batch.Begin(_viewProjection, new Vector3(300, 250, 600), 90, 200, 400, 1500);
        for (var i = 0; i < Instances; i++)
        {
            _batch.Add(_handles[i], _positions[i], 0f, Vector3.One, 0.5f);
        }

        _batch.End();
        return _batch.Buckets.Count;
    }

    [Benchmark]
    public int ScatterDecoration()
    {
        _decor.Update(_map, _objects, new Vector3(512, 0, 512), ++_version, 1f);
        return _decor.Count;
    }
}

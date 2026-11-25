using System.Numerics;
using System.Text.Json;
using Talt.Client.Rendering;
using Talt.Core.Construction;
using Talt.Core.Maps;
using Talt.Core.Meshing.Models;

namespace Talt.Client.Tests.Rendering;

/// <summary>Feature 018: shader includes, lighting maths, water texture, decoration and the day/night clock.</summary>
public class VisualQualityTests
{
    [Fact]
    public void ExpandIncludes_InsertsTheIncludedFile()
    {
        var source = "a\n// #include \"lib.wgsl\"\nb";

        var expanded = WebGpuShaders.ExpandIncludes(source, name => name == "lib.wgsl" ? "LIB" : throw new InvalidOperationException());

        Assert.Equal("a\nLIB\nb\n", expanded);
    }

    [Fact]
    public void ExpandIncludes_RejectsNestedIncludes() =>
        Assert.Throws<InvalidOperationException>(() => WebGpuShaders.ExpandIncludes("// #include \"a.wgsl\"", _ => "// #include \"b.wgsl\""));

    [Fact]
    public void EveryWorldShader_LoadsWithTheLightingInclude()
    {
        foreach (var name in new[] { "voxel.wgsl", "models.wgsl", "roads.wgsl", "towns.wgsl", "boxes.wgsl", "vehicles.wgsl", "industries.wgsl", "highlight.wgsl" })
        {
            var source = WebGpuShaders.Load(name);
            Assert.Contains("fn tone_map", source, StringComparison.Ordinal);
            Assert.DoesNotContain("#include", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ToneMap_IsMonotonicAndBounded()
    {
        var previous = -1f;
        for (var x = 0f; x < 8f; x += 0.25f)
        {
            var y = Lighting.ToneMap(new Vector3(x), 1f).X;
            Assert.InRange(y, 0f, 1f);
            Assert.True(y >= previous);
            previous = y;
        }
    }

    [Fact]
    public void WaterTexture_MeasuresDistanceToLandAndDepth()
    {
        // Left half land at height 10, right half water at level 8 over ground 4.
        var map = SplitMap(32, waterFromX: 16);

        var water = new WaterTexture(map);

        Assert.Equal(0f, water.DistanceAt(10, 5));
        Assert.InRange(water.DistanceAt(16, 5), 0.2f, 0.3f); // One tile from land: 1 / 4.
        Assert.Equal(1f, water.DistanceAt(25, 5), 2);
        Assert.Equal(0.5f, water.DepthAt(25, 5), 2); // Depth 4 of 8.
        Assert.Equal(Talt.Core.Voxels.VoxelMaterial.Grass, water.LandMaterialAt(17, 5)); // The shore's colour, two tiles out.
    }

    [Fact]
    public void WaterTexture_RebuildsARectangle()
    {
        var map = SplitMap(32, waterFromX: 16);
        var water = new WaterTexture(map);

        var (x, z, width, depth) = water.RebuildTiles(20, 20, 22, 22);

        Assert.True(width > 0 && depth > 0);
        Assert.True(x <= 20 && z <= 20 && x + width >= 22 && z + depth >= 22);
    }

    [Fact]
    public void Decoration_IsDeterministicAndRespectsTheMap()
    {
        var map = MapGenerator.Generate(new MapSettings(128, 128, Seed: 11, WaterAmount: 20));
        var objects = new TileObjects(map.Width, map.Depth);
        var bushes = 0;
        for (var z = 0; z < map.Depth; z++)
        {
            for (var x = 0; x < map.Width; x++)
            {
                var kind = DecorScatter.At(map, objects, x, z, 1f, out _);
                Assert.Equal(kind, DecorScatter.At(map, objects, x, z, 1f, out _));
                var index = map.Index(x, z);
                if (kind != DecorKind.None)
                {
                    Assert.False(map.IsWater(index));
                    Assert.Equal(0, Scenery.Kind(map.SceneryData[index]));
                }

                bushes += kind == DecorKind.Bush ? 1 : 0;
                Assert.Equal(DecorKind.None, DecorScatter.At(map, objects, x, z, 0f, out _));
            }
        }

        Assert.True(bushes > 0);
    }

    [Fact]
    public void Decoration_DisappearsFromOccupiedTiles()
    {
        var map = SplitMap(32, waterFromX: 32);
        var objects = new TileObjects(map.Width, map.Depth);
        (int X, int Z)? decorated = null;
        for (var i = 0; i < 32 * 32 && decorated is null; i++)
        {
            if (DecorScatter.At(map, objects, i % 32, i / 32, 1f, out _) != DecorKind.None)
            {
                decorated = (i % 32, i / 32);
            }
        }

        Assert.NotNull(decorated);
        objects.Set(map.Index(decorated.Value.X, decorated.Value.Z), TileObjectKind.Road, TileOwner.Company(0));
        Assert.Equal(DecorKind.None, DecorScatter.At(map, objects, decorated.Value.X, decorated.Value.Z, 1f, out _));
    }

    [Fact]
    public void TimeOfDay_AdvancesOnlyWhenNotPaused()
    {
        var clock = new TimeOfDay();
        var start = clock.Phase;

        clock.Advance(60, paused: true);
        Assert.Equal(start, clock.Phase);

        clock.Advance(TimeOfDay.CycleSeconds / 4, paused: false);
        Assert.Equal((start + 0.25) % 1.0, clock.Phase, 6);
    }

    [Fact]
    public void TimeOfDay_NightIsDarkButReadable_AndNoonIsDay()
    {
        var night = TimeOfDay.At(0.0f);
        var noon = TimeOfDay.At(0.5f);

        Assert.Equal(1f, night.Night);
        Assert.Equal(0f, noon.Night);
        Assert.True(night.SunColor.Length() < noon.SunColor.Length());
        Assert.True(night.SkyAmbient.Length() > 0.05f); // Never fully dark (FR-018).
        Assert.True(TimeOfDay.Direction(0.5f).Y < -0.5f); // High sun at noon.
        for (var p = 0f; p < 1f; p += 0.01f)
        {
            Assert.True(TimeOfDay.Direction(p).Y < 0f); // The light always comes from above.
        }
    }

    internal static MapData SplitMap(int size, int waterFromX)
    {
        var count = size * size;
        var heights = new byte[count];
        var terrain = new byte[count];
        var water = new byte[count];
        var flags = new byte[count];
        for (var i = 0; i < count; i++)
        {
            var isWater = i % size >= waterFromX;
            heights[i] = (byte)(isWater ? 4 : 10);
            water[i] = (byte)(isWater ? 8 : 0);
            terrain[i] = (byte)(isWater ? TerrainType.Water : TerrainType.Grass);
            flags[i] = (byte)(isWater ? TileTraits.Lake : TileTraits.Buildable);
        }

        return new MapData(new MapSettings(size, size, 3), heights, terrain, water, flags, new byte[count], [], default);
    }
}

/// <summary>Feature 018: graphics settings and presets.</summary>
public class GraphicsSettingsTests
{
    [Theory]
    [InlineData(GraphicsPreset.Low)]
    [InlineData(GraphicsPreset.Medium)]
    [InlineData(GraphicsPreset.High)]
    public void ApplyingAPreset_ThenUpdatePreset_KeepsIt(GraphicsPreset preset)
    {
        var settings = GraphicsSettings.For(preset);

        settings.UpdatePreset();

        Assert.Equal(preset, settings.Preset);
    }

    [Fact]
    public void ChangingOneEffect_MakesThePresetCustom()
    {
        var settings = GraphicsSettings.For(GraphicsPreset.High);

        settings.Shadows = false;
        settings.UpdatePreset();

        Assert.Equal(GraphicsPreset.Custom, settings.Preset);
    }

    [Fact]
    public void Presets_GetCheaperFromHighToLow()
    {
        var presets = GraphicsPresets.Default;
        var low = presets.Resolve(GraphicsSettings.For(GraphicsPreset.Low));
        var medium = presets.Resolve(GraphicsSettings.For(GraphicsPreset.Medium));
        var high = presets.Resolve(GraphicsSettings.For(GraphicsPreset.High));

        Assert.True(low.ShadowCascades <= medium.ShadowCascades && medium.ShadowCascades <= high.ShadowCascades);
        Assert.True(low.SceneryDensity <= medium.SceneryDensity && medium.SceneryDensity <= high.SceneryDensity);
        Assert.True(low.ModelLevel1Distance < high.ModelLevel1Distance);
        Assert.InRange(medium.SampleCount, 1, 4);
    }

    [Fact]
    public void Settings_RoundTripThroughThePlayerSettingsFile()
    {
        var settings = new PlayerSettings();
        settings.Graphics.ApplyPreset(GraphicsPreset.High);
        settings.Graphics.SceneryDensity = SceneryDensityLevel.Off;
        settings.Graphics.UpdatePreset();
        settings.Graphics.DayNight = true;
        var path = Path.Combine(Path.GetTempPath(), $"talt-graphics-{Guid.NewGuid():N}.json");
        try
        {
            settings.Save(path);
            Assert.Contains("\"sceneryDensity\": \"off\"", File.ReadAllText(path), StringComparison.Ordinal);

            var loaded = PlayerSettings.Load(path);

            Assert.Empty(loaded.LoadProblems);
            Assert.True(settings.Graphics.SameAs(loaded.Graphics));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void InvalidValues_FallBackOneByOne()
    {
        var settings = new PlayerSettings();

        settings.ReadFrom("""{ "graphics": { "preset": "custom", "shadows": "yes", "antiAliasing": "off", "modelDetail": "ultra" } }""");

        Assert.Equal(2, settings.LoadProblems.Count);
        Assert.Equal(AntiAliasingMode.Off, settings.Graphics.AntiAliasing);
        Assert.True(settings.Graphics.Shadows);
        Assert.Equal(QualityLevel.Medium, settings.Graphics.ModelDetail);
    }

    [Fact]
    public void NamedPreset_UsesTheCurrentTable()
    {
        var settings = new PlayerSettings();

        settings.ReadFrom("""{ "graphics": { "preset": "low", "shadows": true } }""");

        Assert.True(GraphicsSettings.For(GraphicsPreset.Low).SameEffects(settings.Graphics));
    }

    [Fact]
    public void BrokenPresetTable_IsRejected()
    {
        var error = Assert.Throws<InvalidDataException>(() => GraphicsPresets.Parse("""{ "presets": { } }"""));
        Assert.Contains("graphics-presets.json", error.Message, StringComparison.Ordinal);
    }
}

/// <summary>
/// Runs the model tests alone: loading every shipped model is heavy, and the allocation checks of other test
/// classes (counted per thread) were seen to fail when it ran beside them.
/// </summary>
[CollectionDefinition(nameof(ModelPipelineGroup), DisableParallelization = true)]
public sealed class ModelPipelineGroup
{
}

/// <summary>Feature 018: model library validation, shipped models and the instance batch.</summary>
[Collection(nameof(ModelPipelineGroup))]
public class ModelPipelineTests
{
    [Fact]
    public void ShippedModels_AllLoadAndMeshQuickly()
    {
        var library = ModelLibrary.LoadShipped();

        Assert.True(library.KeyCount >= 20);
        Assert.True(library.TryGet("vehicle", out var vehicles) && vehicles.Variants.Count >= 3);
        foreach (var key in new[] { "tree:broadleaf", "tree:conifer", "tree:birch", "bush", "rock", "town-building:cottage", "industry:coal-mine" })
        {
            Assert.True(library.TryGet(key, out _), key);
        }

        Assert.False(library.TryGet("station:bus", out _)); // Placeholder until feature 010 adds one.
        Assert.True(library.LoadMilliseconds < 1000, $"{library.LoadMilliseconds} ms"); // SC-005.
    }

    [Fact]
    public void Library_RejectsMissingFilesAndBadIndices()
    {
        var model = new VoxModel(2, 2, 2);
        model.Set(0, 0, 0, 1);
        var bytes = VoxWriter.Write(model);
        byte[]? Load(string file) => file == "ok.vox" ? bytes : null;

        Assert.Contains("missing.vox", Assert.Throws<InvalidDataException>(() =>
            ModelLibrary.Parse("""{ "models": { "a": [ { "file": "missing.vox" } ] } }""", Load)).Message, StringComparison.Ordinal);
        Assert.Throws<InvalidDataException>(() => ModelLibrary.Parse("""{ "models": { "a": [ { "file": "ok.vox", "paint": [0] } ] } }""", Load));
        Assert.Throws<InvalidDataException>(() => ModelLibrary.Parse("""{ "models": { "a": [] } }""", Load));
        Assert.Throws<InvalidDataException>(() => ModelLibrary.Parse("""{ "models": { "a": [ { "file": "ok.vox", "yaw": 45 } ] } }""", Load));
        Assert.Throws<InvalidDataException>(() => ModelLibrary.Parse("""{ "voxelsPerTile": 16, "models": { } }""", Load));
    }

    [Fact]
    public void Library_EncodesPaletteRoles()
    {
        var model = new VoxModel(2, 2, 2);
        model.Set(0, 0, 0, 1);
        model.Set(1, 0, 0, 2);
        model.Set(0, 1, 0, 3);
        var bytes = VoxWriter.Write(model);

        var library = ModelLibrary.Parse("""{ "models": { "a": [ { "file": "m.vox", "paint": [2], "glow": [3] } ] } }""", _ => bytes);

        Assert.Equal(ModelLibrary.OpaqueAlpha, (byte)(library.Palettes[1] >> 24));
        Assert.Equal(ModelLibrary.PaintAlpha, (byte)(library.Palettes[2] >> 24));
        Assert.Equal(ModelLibrary.GlowAlpha, (byte)(library.Palettes[3] >> 24));
        Assert.All(library.Vertices, v => Assert.Equal(0, v.PaletteRow));
    }

    [Fact]
    public void Variant_IsStablePerObject()
    {
        var library = ModelLibrary.LoadShipped();
        Assert.True(library.TryGet("vehicle", out var entry));

        Assert.Equal(entry.Variant(1234), entry.Variant(1234));
        Assert.True(Enumerable.Range(0, 200).Select(entry.Variant).Distinct().Count() > 1);
    }

    [Fact]
    public void Batch_SortsIntoBucketsAndPicksLevelsByDistance()
    {
        var batch = new ModelBatch(3);
        var camera = new Vector3(0, 50, 0);
        batch.Begin(Matrix4x4.Identity, camera, 10, 20, 40, 1000, cull: false);
        var near = new ModelMeshHandle(2, 0.5f, 1f);
        var far = new ModelMeshHandle(0, 0.5f, 1f);
        batch.Add(near, new Vector3(0, 45, 0), 0, Vector3.One, 0);
        batch.Add(far, new Vector3(100, 0, 0), 0, Vector3.One, 0);
        batch.Add(near, new Vector3(0, 46, 0), 0, Vector3.One, 0);
        batch.Add(far, new Vector3(0, 35, 0), 0, Vector3.One, 0);
        batch.End();

        Assert.Equal(4, batch.Count);
        Assert.Collection(batch.Buckets,
            b => Assert.Equal((0, 1, 1), (b.Variant, b.Level, b.Count)),
            b => Assert.Equal((0, 3, 1), (b.Variant, b.Level, b.Count)),
            b => Assert.Equal((2, 0, 2), (b.Variant, b.Level, b.Count)));
        var total = 0;
        foreach (var bucket in batch.Buckets)
        {
            Assert.Equal(total, bucket.FirstInstance);
            total += bucket.Count;
        }

        Assert.Equal(new Vector4(100, 0, 0, 0), batch.Instances[1].PositionYaw);
    }

    [Fact]
    public void Batch_CullsOutsideTheViewButKeepsShadowCasters()
    {
        var batch = new ModelBatch(1);
        var view = Matrix4x4.CreateLookAt(new Vector3(0, 50, 50), Vector3.Zero, Vector3.UnitY);
        var projection = Matrix4x4.CreatePerspectiveFieldOfView(0.6f, 1f, 1f, 500f);
        batch.Begin(view * projection, new Vector3(0, 50, 50), 10, 20, 40, 400);
        var tall = new ModelMeshHandle(0, 0.5f, 20f);

        Assert.True(batch.Add(tall, Vector3.Zero, 0, Vector3.One, 0));
        Assert.False(batch.Add(tall, new Vector3(0, 0, 400), 0, Vector3.One, 0)); // Behind the camera.
        batch.End();
        Assert.Equal(1, batch.Culled);
    }

    [Fact]
    public void Batch_DoesNotAllocateOnceWarm()
    {
        var batch = new ModelBatch(4);
        var handle = new ModelMeshHandle(1, 0.5f, 1f);
        void Frame()
        {
            batch.Begin(Matrix4x4.Identity, Vector3.Zero, 10, 20, 40, 1000, cull: false);
            for (var i = 0; i < 5000; i++)
            {
                batch.Add(handle, new Vector3(i % 50, 0, i / 50), 0, Vector3.One, 0);
            }

            batch.End();
        }

        Frame();
        var before = GC.GetAllocatedBytesForCurrentThread();
        Frame();
        Assert.Equal(before, GC.GetAllocatedBytesForCurrentThread());
    }
}

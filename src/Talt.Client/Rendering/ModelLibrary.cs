using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Talt.Core.Meshing.Models;

namespace Talt.Client.Rendering;

/// <summary>A meshed model variant ready to draw: where its levels are in the shared vertex buffer and its size.</summary>
/// <param name="Id">Index of the variant in the library (bucket key of <see cref="ModelBatch"/>).</param>
/// <param name="Radius">Horizontal half-diagonal in world units (culling).</param>
/// <param name="Height">Height in world units (culling and shadow reach).</param>
internal readonly record struct ModelMeshHandle(int Id, float Radius, float Height)
{
    public bool IsValid => Height > 0f;
}

/// <summary>The variants of one model key (for example <c>vehicle</c> or <c>town-building:cottage</c>).</summary>
internal sealed class ModelEntry(string key, ModelMeshHandle[] variants)
{
    public string Key { get; } = key;

    public IReadOnlyList<ModelMeshHandle> Variants { get; } = variants;

    /// <summary>The variant for an object, stable for the same <paramref name="objectId"/>.</summary>
    public ModelMeshHandle Variant(int objectId)
    {
        var hash = unchecked((uint)objectId * 2654435761u);
        return Variants[(int)((hash >> 8) % (uint)Variants.Count)];
    }
}

/// <summary>
/// Loads the voxel models (feature 018, contracts/model-pipeline.md): reads <c>models.json</c>, reads each
/// variant's embedded <c>.vox</c> file with <see cref="VoxReader"/>, meshes it with <see cref="ModelMesher"/> at
/// its three detail levels and packs every mesh into one vertex array, with one palette row per variant
/// whose alpha encodes the colour's role (opaque, paint tinted by the instance colour, or glow lit at night).
/// Errors name <c>models.json</c>, the key and the reason, and stop the start (FR-012).
/// </summary>
internal sealed class ModelLibrary
{
    public const string FileName = "models.json";

    /// <summary>Model voxels per tile side (clarification: 8).</summary>
    public const int VoxelsPerTile = 8;

    /// <summary>World units per vertex unit (vertices are in half voxels).</summary>
    public const float VertexScale = 1f / (2f * VoxelsPerTile);

    /// <summary>Palette alpha of an opaque colour, a paint colour and a glow colour (read by models.wgsl).</summary>
    public const byte OpaqueAlpha = 255;
    public const byte PaintAlpha = 128;
    public const byte GlowAlpha = 0;

    private readonly Dictionary<string, ModelEntry> _entries;

    private ModelLibrary(Dictionary<string, ModelEntry> entries, ModelVertex[] vertices, (int FirstVertex, int Quads)[] levels,
        uint[] palettes, int maxQuads, double loadMilliseconds)
    {
        _entries = entries;
        Vertices = vertices;
        Levels = levels;
        Palettes = palettes;
        MaxQuads = maxQuads;
        LoadMilliseconds = loadMilliseconds;
    }

    /// <summary>Every vertex of every variant and level (palette rows filled in).</summary>
    public ModelVertex[] Vertices { get; }

    /// <summary>First vertex and quad count per variant and level: index <c>id * 3 + level</c>.</summary>
    public (int FirstVertex, int Quads)[] Levels { get; }

    /// <summary>256 RGBA colours per variant (palette row = variant id), sRGB with role alpha.</summary>
    public uint[] Palettes { get; }

    public int VariantCount => Levels.Length / ModelMesh.LevelCount;

    /// <summary>Largest quad count of any level (size of the shared index pattern).</summary>
    public int MaxQuads { get; }

    public int KeyCount => _entries.Count;

    public double LoadMilliseconds { get; }

    public IEnumerable<string> Keys => _entries.Keys;

    /// <summary>Triangles of all level-0 meshes.</summary>
    public long Triangles
    {
        get
        {
            long total = 0;
            for (var i = 0; i < Levels.Length; i += ModelMesh.LevelCount)
            {
                total += Levels[i].Quads * 2L;
            }

            return total;
        }
    }

    /// <summary>The model of a key, or false when none is assigned (the caller draws its placeholder).</summary>
    public bool TryGet(string key, out ModelEntry entry) => _entries.TryGetValue(key, out entry!);

    /// <summary>Loads the shipped models (embedded <c>data/models.json</c> and <c>models/*.vox</c>).</summary>
    public static ModelLibrary LoadShipped()
    {
        var assembly = Assembly.GetExecutingAssembly();
        string json;
        using (var stream = DataFiles.Open("data/" + FileName))
        using (var reader = new StreamReader(stream))
        {
            json = reader.ReadToEnd();
        }

        return Parse(json, file =>
        {
            using var stream = assembly.GetManifestResourceStream("models/" + file);
            if (stream is null)
            {
                return null;
            }

            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            return memory.ToArray();
        });
    }

    /// <summary>Parses an assignment file; <paramref name="loadFile"/> returns a model file's bytes or null.</summary>
    public static ModelLibrary Parse(string json, Func<string, byte[]?> loadFile)
    {
        ArgumentNullException.ThrowIfNull(loadFile);
        var start = Stopwatch.GetTimestamp();
        using var document = ParseJson(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("models", out var models) || models.ValueKind != JsonValueKind.Object)
        {
            throw DataFiles.Invalid(FileName, "missing object 'models'.");
        }

        if (root.TryGetProperty("voxelsPerTile", out var scale) && (!scale.TryGetInt32(out var perTile) || perTile != VoxelsPerTile))
        {
            throw DataFiles.Invalid(FileName, $"'voxelsPerTile' must be {VoxelsPerTile}.");
        }

        var entries = new Dictionary<string, ModelEntry>(StringComparer.Ordinal);
        var vertices = new List<ModelVertex>();
        var levels = new List<(int, int)>();
        var palettes = new List<uint>();
        var cache = new Dictionary<string, (VoxModel Model, ModelMesh Mesh)>(StringComparer.Ordinal);
        var maxQuads = 0;
        foreach (var property in models.EnumerateObject())
        {
            var key = property.Name;
            if (entries.ContainsKey(key))
            {
                throw Invalid(key, "is defined twice");
            }

            if (property.Value.ValueKind != JsonValueKind.Array || property.Value.GetArrayLength() == 0)
            {
                throw Invalid(key, "needs a non-empty list of variants");
            }

            var handles = new List<ModelMeshHandle>();
            foreach (var variant in property.Value.EnumerateArray())
            {
                if (variant.ValueKind != JsonValueKind.Object || !variant.TryGetProperty("file", out var fileElement)
                    || fileElement.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(fileElement.GetString()))
                {
                    throw Invalid(key, "every variant needs a 'file'");
                }

                var file = fileElement.GetString()!;
                if (!cache.TryGetValue(file, out var loaded))
                {
                    var bytes = loadFile(file) ?? throw Invalid(key, $"model file '{file}' was not found");
                    VoxModel model;
                    try
                    {
                        model = VoxReader.Read(bytes, file);
                    }
                    catch (VoxFormatException exception)
                    {
                        throw Invalid(key, exception.Message);
                    }

                    loaded = (model, ModelMesher.Build(model));
                    cache[file] = loaded;
                }

                var paint = ReadIndices(variant, "paint", key);
                var glow = ReadIndices(variant, "glow", key);
                var yaw = 0;
                if (variant.TryGetProperty("yaw", out var yawElement) && (!yawElement.TryGetInt32(out yaw) || yaw % 90 != 0))
                {
                    throw Invalid(key, "'yaw' must be a multiple of 90 degrees");
                }

                var id = levels.Count / ModelMesh.LevelCount;
                AppendPalette(palettes, loaded.Model, paint, glow);
                for (var level = 0; level < ModelMesh.LevelCount; level++)
                {
                    var source = loaded.Mesh.Levels[level];
                    levels.Add((vertices.Count, source.Length / 4));
                    maxQuads = Math.Max(maxQuads, source.Length / 4);
                    foreach (var v in source)
                    {
                        vertices.Add(Rotate(v, yaw).WithPaletteRow(id));
                    }
                }

                var mesh = loaded.Mesh;
                var halfX = mesh.SizeX / (float)VoxelsPerTile / 2f;
                var halfZ = mesh.SizeZ / (float)VoxelsPerTile / 2f;
                handles.Add(new ModelMeshHandle(id, MathF.Sqrt((halfX * halfX) + (halfZ * halfZ)), mesh.SizeY / (float)VoxelsPerTile));
            }

            entries[key] = new ModelEntry(key, [.. handles]);
        }

        var milliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        return new ModelLibrary(entries, [.. vertices], [.. levels], [.. palettes], maxQuads, milliseconds);
    }

    private static JsonDocument ParseJson(string json)
    {
        try
        {
            return JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        }
        catch (JsonException exception)
        {
            throw DataFiles.Invalid(FileName, $"not valid JSON ({exception.Message}).");
        }
    }

    private static int[] ReadIndices(JsonElement variant, string name, string key)
    {
        if (!variant.TryGetProperty(name, out var list))
        {
            return [];
        }

        if (list.ValueKind != JsonValueKind.Array)
        {
            throw Invalid(key, $"'{name}' must be a list of palette indices");
        }

        var result = new List<int>();
        foreach (var item in list.EnumerateArray())
        {
            if (!item.TryGetInt32(out var index) || index is < 1 or > 255)
            {
                throw Invalid(key, $"'{name}' palette indices must be 1–255");
            }

            result.Add(index);
        }

        return [.. result];
    }

    private static void AppendPalette(List<uint> palettes, VoxModel model, int[] paint, int[] glow)
    {
        for (var i = 0; i < 256; i++)
        {
            var alpha = glow.Contains(i) ? GlowAlpha : paint.Contains(i) ? PaintAlpha : OpaqueAlpha;
            palettes.Add((model.Palette[i] & 0x00ffffffu) | ((uint)alpha << 24));
        }
    }

    /// <summary>Rotates a vertex by a multiple of 90° around the vertical axis (counter-clockwise from above).</summary>
    private static ModelVertex Rotate(ModelVertex v, int degrees)
    {
        var turns = ((degrees / 90 % 4) + 4) % 4;
        if (turns == 0)
        {
            return v;
        }

        var (x, z, face) = (v.X2, v.Z2, v.FaceIndex);
        for (var t = 0; t < turns; t++)
        {
            (x, z) = ((short)z, (short)-x);
            face = face switch
            {
                0 => 5, // +X → −Z
                5 => 1, // −Z → −X
                1 => 4, // −X → +Z
                4 => 0, // +Z → +X
                _ => face,
            };
        }

        return v with { X2 = x, Z2 = z, FaceAo = (ushort)((v.FaceAo & ~7) | face) };
    }

    private static InvalidDataException Invalid(string key, string problem) => DataFiles.Invalid(FileName, $"model '{key}': {problem}.");
}

# Contract: Map generator API (`Talt.Core.Maps`)

```csharp
public sealed record MapSettings(
    int Width = 1024, int Depth = 1024, long Seed = 1,
    int Roughness = 50, int MaxHeight = 28, int WaterAmount = 25, int SeaLevel = 7)
{
    public const int GeneratorVersion = 1;
    public const int PlayerMinimumSize = 256;
    public void Validate();                                   // ArgumentException with setting name + range
    public string ToText();                                   // "T1-1024x512-s2026-r50-h28-w25-l7"
    public static bool TryParse(string text, out MapSettings? settings, out string? error);
}

public static class MapGenerator
{
    public static MapData Generate(
        MapSettings settings,
        IProgress<MapGenerationProgress>? progress = null,
        CancellationToken cancellationToken = default,
        MapGenerationTuning? tuning = null,
        int? maxDegreeOfParallelism = null);                  // null = all cores; result identical
}

public static class WorldBuilder
{
    public static VoxelWorld Build(MapData map,
        IProgress<MapGenerationProgress>? progress = null, CancellationToken cancellationToken = default);
}

public sealed class MapData { /* arrays and accessors: see data-model.md */ public ulong Fingerprint(); }
```

Guarantees

- `Generate` is a pure function of `(settings, tuning)`; the thread limit never changes the result.
- Progress: `Fraction` monotonic non-decreasing, first report ≥ 0, last report 1.0 when completed;
  callbacks arrive on arbitrary threads (use `Progress<T>` to marshal; the console printer is
  thread-safe). Reports are limited to ~100 per generation.
- Cancellation: throws `OperationCanceledException`; nothing is returned or retained.
- Errors: invalid settings throw `ArgumentException` before any work.
- `Talt.Core` stays free of console output; printing is the caller's job.

Consumers outside this feature: features 008/009 read `MapData.Sites`; the future new-game screen
(after feature 005) builds a `MapSettings`, calls `Generate` with an `IProgress` bound to its
progress bar, then `WorldBuilder.Build`.

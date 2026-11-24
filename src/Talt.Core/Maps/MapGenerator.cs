using Talt.Core.Maps.Stages;

namespace Talt.Core.Maps;

/// <summary>
/// Generates a map from settings, in stages (research R1 to R10). The result is a pure function of
/// the settings and tuning: the thread limit never changes it. Use <see cref="WorldBuilder"/> to
/// turn the result into voxels for rendering.
/// </summary>
public static class MapGenerator
{
    /// <summary>
    /// Generates a map. Reports progress per stage (monotonic, ending at 1) and throws
    /// <see cref="OperationCanceledException"/> when cancelled, returning nothing.
    /// </summary>
    /// <param name="settings">Validated here; invalid settings throw <see cref="ArgumentException"/> before any work.</param>
    /// <param name="progress">Receives stage and overall fraction; may be called from any thread.</param>
    /// <param name="tuning">Thresholds and densities; null for the defaults.</param>
    /// <param name="maxDegreeOfParallelism">Limit on worker threads; null uses all cores. Does not change the result.</param>
    /// <param name="cancellationToken">Cancels the generation.</param>
    public static MapData Generate(
        MapSettings settings,
        IProgress<MapGenerationProgress>? progress = null,
        MapGenerationTuning? tuning = null,
        int? maxDegreeOfParallelism = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate();
        tuning ??= MapGenerationTuning.Default;
        tuning.Validate();

        var builder = new MapBuilder(settings, tuning, maxDegreeOfParallelism, cancellationToken);
        var reporter = new ProgressReporter(progress);
        Drainage? drainage = null;

        // Weights are the measured share of each stage (they only shape the progress bar).
        Step(reporter, builder, MapGenerationStage.Terrain, 0.22f, () => HeightField.Run(builder));
        Step(reporter, builder, MapGenerationStage.Slopes, 0.08f, () => SlopeLimiter.Run(builder));
        Step(reporter, builder, MapGenerationStage.Water, 0.20f, () => drainage = DrainageFill.Run(builder));
        Step(reporter, builder, MapGenerationStage.Rivers, 0.10f, () => RiverCarver.Run(builder, drainage!));
        drainage = null;
        Step(reporter, builder, MapGenerationStage.Surface, 0.12f, () => TerrainClassifier.Run(builder));
        Step(reporter, builder, MapGenerationStage.Scenery, 0.08f, () => ScenerySeeder.Run(builder));
        Step(reporter, builder, MapGenerationStage.Sites, 0.20f, () => SiteSelector.Run(builder));

        reporter.Report(MapGenerationStage.Sites, 1f);
        return builder.ToMapData();
    }

    private static void Step(ProgressReporter reporter, MapBuilder builder, MapGenerationStage stage, float weight, Action action)
    {
        builder.CancellationToken.ThrowIfCancellationRequested();
        reporter.Begin(stage);
        action();
        reporter.Advance(weight);
    }

    /// <summary>Turns stage weights into monotonic overall fractions and keeps the number of reports small.</summary>
    private sealed class ProgressReporter(IProgress<MapGenerationProgress>? progress)
    {
        private float _fraction;

        public void Begin(MapGenerationStage stage)
        {
            Report(stage, _fraction);
        }

        public void Advance(float weight) => _fraction = Math.Min(1f, _fraction + weight);

        public void Report(MapGenerationStage stage, float fraction) =>
            progress?.Report(new MapGenerationProgress(stage, Math.Min(1f, fraction)));
    }
}

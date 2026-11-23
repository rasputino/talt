using Talt.Core.Maps;

namespace Talt.Core.Tests.Maps;

public class MapGeneratorProgressTests
{
    private sealed class Recorder : IProgress<MapGenerationProgress>
    {
        public List<MapGenerationProgress> Reports { get; } = [];

        public void Report(MapGenerationProgress value) => Reports.Add(value);
    }

    [Fact]
    public void Progress_IsMonotonic_EndsAtOne_AndIsNotChatty()
    {
        var recorder = new Recorder();
        MapGenerator.Generate(new MapSettings(256, 256, 5), recorder);

        Assert.NotEmpty(recorder.Reports);
        Assert.True(recorder.Reports.Count <= 100);
        Assert.Equal(1f, recorder.Reports[^1].Fraction);
        for (var i = 1; i < recorder.Reports.Count; i++)
        {
            Assert.True(recorder.Reports[i].Fraction >= recorder.Reports[i - 1].Fraction);
        }

        Assert.Equal(MapGenerationStage.Terrain, recorder.Reports[0].Stage);
        Assert.InRange(recorder.Reports[0].Fraction, 0f, 0.001f);
    }

    [Fact]
    public void WorldBuilder_ReportsItsOwnStage_UpToOne()
    {
        var map = MapTestSettings.Generate(new MapSettings(256, 256, 1));
        var recorder = new Recorder();
        WorldBuilder.Build(map, recorder);

        Assert.All(recorder.Reports, r => Assert.Equal(MapGenerationStage.World, r.Stage));
        Assert.Equal(1f, recorder.Reports[^1].Fraction);
    }

    [Fact]
    public void Cancelled_Throws_AndReturnsNothing()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() =>
            MapGenerator.Generate(new MapSettings(256, 256, 5), cancellationToken: cancellation.Token));
    }

    [Fact]
    public void CancelledDuringGeneration_Throws()
    {
        using var cancellation = new CancellationTokenSource();
        var progress = new CancelOnStage(MapGenerationStage.Water, cancellation);
        Assert.Throws<OperationCanceledException>(() =>
            MapGenerator.Generate(new MapSettings(512, 512, 5), progress, cancellationToken: cancellation.Token));
    }

    [Fact]
    public void InvalidSettings_ThrowBeforeAnyProgress()
    {
        var recorder = new Recorder();
        Assert.Throws<ArgumentException>(() => MapGenerator.Generate(new MapSettings(100, 256), recorder));
        Assert.Empty(recorder.Reports);
    }

    private sealed class CancelOnStage(MapGenerationStage stage, CancellationTokenSource source) : IProgress<MapGenerationProgress>
    {
        public void Report(MapGenerationProgress value)
        {
            if (value.Stage == stage)
            {
                source.Cancel();
            }
        }
    }
}

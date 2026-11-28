using Talt.Core.Saves;

namespace Talt.Core.Tests.Saves;

public class CompatibilityTests
{
    [Fact]
    public void Older_section_migrates_and_absent_section_uses_defaults()
    {
        var game = SaveTestGames.Rich(ticks: 0);
        var id = SaveSectionId.Of("TEST");
        var value = 0;
        var options = new SaveLoadOptions
        {
            AdditionalSections = [new SaveSection(id, 2, _ => true, (_, _) => { },
                (_, reader, version) => value = version == 1 ? reader.ReadByte() : reader.ReadInt())],
        };
        var bytes = RoundTrip.SaveBytes(game);
        Assert.True(SaveGame.Load(new MemoryStream(bytes), options).Succeeded);
        Assert.Equal(0, value);
        using var snapshot = SaveGame.Capture(game, RoundTrip.Details, [new ExtraSection(id, 1, new byte[] { 42 })]);
        using var stream = new MemoryStream();
        SaveFile.Write(stream, snapshot.Details!.ToJson(), snapshot.Thumbnail, snapshot.Sections);
        stream.Position = 0;
        Assert.Equal(SaveLoadStatus.TooNew, SaveGame.Load(stream).Status);
        stream.Position = 0;
        Assert.True(SaveGame.Load(stream, options).Succeeded);
        Assert.Equal(42, value);

        stream.SetLength(0);
        var newer = snapshot.Sections.Select(section => section.Id == id ? section with { Version = 3 } : section).ToArray();
        SaveFile.Write(stream, snapshot.Details.ToJson(), snapshot.Thumbnail, newer);
        stream.Position = 0;
        Assert.Equal(SaveLoadStatus.TooNew, SaveGame.Load(stream, options).Status);
    }

    [Fact]
    public void Legacy_fixture_migrates_without_catalog_policies()
    {
        var result = SaveGame.Load(Fixture("rich-v1.talt"));
        Assert.Equal(SaveLoadStatus.LoadedWithChangedData, result.Status);
        Assert.Equal(0x57A77DB781A03625UL, result.Details!.Fingerprint);
        Assert.Equal(0x0F7B71D994547B77UL, result.Simulation!.ComputeFingerprint());
        Assert.Empty(result.Simulation.Construction!.Player.RailReplacements.Rules);
        for (var i = 0; i < 1000; i++)
        {
            result.Simulation.Tick();
        }

        Assert.Equal(0xD8B2D20809F77FA6UL, result.Simulation.ComputeFingerprint());
    }

    [Fact]
    public void Current_fixture_matches_its_original_and_continues()
    {
        var original = SaveTestGames.Rich();
        var path = Fixture("rich-catalog.talt");
        if (Environment.GetEnvironmentVariable("TALT_GENERATE_SAVE_FIXTURE") is { Length: > 0 } destination)
        {
            Assert.True(SaveGame.Save(original, destination, RoundTrip.Details with { IncludeFingerprint = true }).Succeeded);
            path = destination;
        }

        var result = SaveGame.Load(path);
        Assert.True(result.Status == SaveLoadStatus.Loaded, result.Problem);
        Assert.Equal(result.Details!.Fingerprint, result.Simulation!.ComputeFingerprint());
        Assert.NotEmpty(result.Simulation.Construction!.Player.RoadReplacements.Rules);
        RoundTrip.AssertContinuesIdentically(original, result.Simulation, 1000);
    }

    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Saves", "Fixtures", name);
}

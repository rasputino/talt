namespace Talt.Client.Tests;

public class PlayerSettingsTests
{
    [Fact]
    public void Settings_RoundTripThroughTheFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"talt-settings-{Guid.NewGuid():N}.json");
        try
        {
            var settings = new PlayerSettings
            {
                Language = "es",
                Currency = "JPY",
                UiScale = 1.5f,
                Fullscreen = true,
                VSync = false,
                FastForwardUnlocked = true,
                WindowWidth = 1280,
                WindowHeight = 720,
                MasterVolume = 0.7f,
                MusicVolume = 0.4f,
                EffectsVolume = 0.9f,
                AmbientVolume = 0.3f,
                Muted = true,
            };
            settings.KeyBindings["pause"] = ["P", ""];
            settings.Save(path);

            var loaded = PlayerSettings.Load(path);
            Assert.Empty(loaded.LoadProblems);
            Assert.Equal("es", loaded.Language);
            Assert.Equal("JPY", loaded.Currency);
            Assert.Equal(1.5f, loaded.UiScale);
            Assert.True(loaded.Fullscreen);
            Assert.False(loaded.VSync);
            Assert.True(loaded.FastForwardUnlocked);
            Assert.Equal(1280, loaded.WindowWidth);
            Assert.Equal(0.7f, loaded.MasterVolume);
            Assert.Equal(0.4f, loaded.MusicVolume);
            Assert.Equal(0.9f, loaded.EffectsVolume);
            Assert.Equal(0.3f, loaded.AmbientVolume);
            Assert.True(loaded.Muted);
            Assert.Equal(["P", ""], loaded.KeyBindings["pause"]);
            Assert.False(File.Exists(path + ".tmp"));
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
        settings.ReadFrom("""{ "language": "fr", "uiScale": 3.3, "vsync": false, "windowWidth": 10, "currency": "GBP", "nonsense": 1, "masterVolume": 2, "musicVolume": "loud", "muted": "yes" }""");
        Assert.Null(settings.Language);
        Assert.Null(settings.UiScale);
        Assert.False(settings.VSync);
        Assert.Equal(1600, settings.WindowWidth);
        Assert.Equal("GBP", settings.Currency);
        Assert.Equal(0.8f, settings.MasterVolume);
        Assert.Equal(0.5f, settings.MusicVolume);
        Assert.False(settings.Muted);
        Assert.Equal(7, settings.LoadProblems.Count);
    }

    [Fact]
    public void Version1File_KeepsTheFastForwardSetting()
    {
        // Feature 001 files only had this property.
        var settings = new PlayerSettings();
        settings.ReadFrom("""{ "FastForwardUnlocked": true }""");
        Assert.True(settings.FastForwardUnlocked);
        Assert.Empty(settings.LoadProblems);
        Assert.Equal(PlayerSettings.CurrentVersion, settings.Version);
    }

    [Fact]
    public void BrokenFile_GivesDefaultsAndAProblem()
    {
        var settings = new PlayerSettings();
        settings.ReadFrom("{ not json");
        Assert.Single(settings.LoadProblems);
        Assert.True(settings.VSync);
    }
}

using Talt.Client.Audio;
using Xunit;

namespace Talt.Client.Tests.Audio;

public class AudioDefinitionTests
{
    [Fact]
    public void ValidDefinition_ResolvesEveryAsset()
    {
        var definition = AudioTestData.Definition();
        Assert.Equal(8, definition.VoiceBudget);
        Assert.EndsWith("VehicleEngine.flac", definition.Cue(AudioCue.VehicleEngine).FullPath, StringComparison.Ordinal);
        Assert.EndsWith("Country.flac", definition.AmbientBed(AudioEnvironment.Country).FullPath, StringComparison.Ordinal);
        Assert.Equal(2, definition.Music.FullTracks.Length);
    }

    [Theory]
    [InlineData("version")]
    [InlineData("voiceBudget")]
    [InlineData("oneShotVoices")]
    [InlineData("ambientVoices")]
    [InlineData("maxVehicleDistanceTiles")]
    [InlineData("hysteresisTiles")]
    public void InvalidScalar_IsRejectedWithAFieldName(string field)
    {
        var definition = AudioTestData.Definition();
        switch (field)
        {
            case "version": definition.Version = 99; break;
            case "voiceBudget": definition.VoiceBudget = 64; break;
            case "oneShotVoices": definition.OneShotVoices = 0; break;
            case "ambientVoices": definition.AmbientVoices = 1; break;
            case "maxVehicleDistanceTiles": definition.MaxVehicleDistanceTiles = 0; break;
            case "hysteresisTiles": definition.HysteresisTiles = 50; break;
        }

        var exception = Assert.Throws<InvalidDataException>(() => definition.Validate("audio", _ => true));
        Assert.Contains(field, exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MissingCue_IsRejected()
    {
        var definition = AudioTestData.Definition();
        definition.Cues.Remove(nameof(AudioCue.Construction));
        var exception = Assert.Throws<InvalidDataException>(() => definition.Validate("audio", _ => true));
        Assert.Contains("Construction", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void UnknownCue_IsRejected()
    {
        var definition = AudioTestData.Definition();
        definition.Cues["SomethingElse"] = definition.Cues[nameof(AudioCue.InterfaceClick)];
        var exception = Assert.Throws<InvalidDataException>(() => definition.Validate("audio", _ => true));
        Assert.Contains("unknown cue", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MissingFile_IsRejected()
    {
        var definition = AudioTestData.Definition();
        var exception = Assert.Throws<InvalidDataException>(() => definition.Validate("audio", _ => false));
        Assert.Contains("does not exist", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MusicWithoutTracks_IsRejected()
    {
        var definition = AudioTestData.Definition();
        definition.Music.Tracks = [];
        var exception = Assert.Throws<InvalidDataException>(() => definition.Validate("audio", _ => true));
        Assert.Contains("music.tracks", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AbsoluteOrParentPath_IsRejected()
    {
        var definition = AudioTestData.Definition();
        definition.Cues[nameof(AudioCue.InterfaceClick)].File = "../outside.wav";
        var exception = Assert.Throws<InvalidDataException>(() => definition.Validate("audio", _ => true));
        Assert.Contains("relative path", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}

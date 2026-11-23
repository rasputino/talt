using Talt.Client.Audio;
using Xunit;

namespace Talt.Client.Tests.Audio;

public class AudioSystemTests
{
    [Fact]
    public void Click_PlaysOneInterfaceSound()
    {
        var backend = new FakeAudioBackend();
        using var audio = new AudioSystem(backend, AudioTestData.Definition(), new PlayerSettings());
        audio.PlayInterfaceClick();
        audio.Update(default);
        Assert.Equal(1, backend.VoicePlaying("InterfaceClick.wav")!.PlayCount);
    }

    [Fact]
    public void CommandFeedback_ChoosesTheRightCue()
    {
        var backend = new FakeAudioBackend();
        using var audio = new AudioSystem(backend, AudioTestData.Definition(), new PlayerSettings());
        audio.PlayCommandFeedback(true, false);
        audio.PlayCommandFeedback(false, false);
        audio.PlayCommandFeedback(true, true);
        audio.Update(default);
        Assert.NotNull(backend.VoicePlaying("CommandApplied.wav"));
        Assert.NotNull(backend.VoicePlaying("CommandRejected.wav"));
        Assert.NotNull(backend.VoicePlaying("Construction.wav"));
    }

    [Fact]
    public void Music_StartsInTheMenuAndUsesTheMusicBus()
    {
        var backend = new FakeAudioBackend();
        using var audio = new AudioSystem(backend, AudioTestData.Definition(), new PlayerSettings());
        var music = backend.VoicePlaying("one.flac");
        Assert.NotNull(music);
        Assert.Equal(AudioBus.Music, music.Bus);
        Assert.True(music.Stream);
        Assert.False(music.Loop);
    }

    [Fact]
    public void Settings_UpdateEveryBus()
    {
        var backend = new FakeAudioBackend();
        using var audio = new AudioSystem(backend, AudioTestData.Definition(), new PlayerSettings());
        audio.ApplySettings(new PlayerSettings
        {
            MasterVolume = 0.5f,
            MusicVolume = 0.4f,
            EffectsVolume = 0.3f,
            AmbientVolume = 0.2f,
        });
        Assert.Equal(0.5f, backend.BusVolumes[(int)AudioBus.Master]);
        Assert.Equal(0.4f, backend.BusVolumes[(int)AudioBus.Music]);
        Assert.Equal(0.3f, backend.BusVolumes[(int)AudioBus.Effects]);
        Assert.Equal(0.2f, backend.BusVolumes[(int)AudioBus.Ambient]);
    }

    [Fact]
    public void Mute_SilencesTheMasterBusWithoutLosingVolumes()
    {
        var backend = new FakeAudioBackend();
        var settings = new PlayerSettings { MasterVolume = 0.8f, MusicVolume = 0.5f };
        using var audio = new AudioSystem(backend, AudioTestData.Definition(), settings);
        settings.Muted = true;
        audio.ApplySettings(settings);
        Assert.Equal(0f, backend.BusVolumes[(int)AudioBus.Master]);
        Assert.Equal(0.5f, settings.MusicVolume);
    }

    [Fact]
    public void UnavailableBackend_IsSilentAndDoesNotThrow()
    {
        var backend = new FakeAudioBackend(available: false);
        using var audio = new AudioSystem(backend, AudioTestData.Definition(), new PlayerSettings());
        audio.PlayInterfaceClick();
        audio.PlayCommandFeedback(true, true);
        audio.Update(default);
        Assert.False(audio.IsAvailable);
        Assert.NotNull(audio.UnavailableReason);
    }

    [Fact]
    public void SilentSystem_AcceptsEveryCue()
    {
        using var audio = AudioSystem.CreateSilent();
        audio.PlayInterfaceClick();
        audio.PlayConstruction();
        audio.PlayCommandFeedback(false, false);
        audio.Update(default);
        Assert.False(audio.IsAvailable);
    }

    [Fact]
    public void FailedFile_DisablesOnlyThatCue()
    {
        var backend = new FakeAudioBackend { FailPlayback = true };
        using var audio = new AudioSystem(backend, AudioTestData.Definition(), new PlayerSettings());
        audio.PlayInterfaceClick();
        backend.FailPlayback = false;
        audio.PlayConstruction();
        audio.Update(default);
        Assert.Null(backend.VoicePlaying("InterfaceClick.wav"));
        Assert.NotNull(backend.VoicePlaying("Construction.wav"));
    }
}

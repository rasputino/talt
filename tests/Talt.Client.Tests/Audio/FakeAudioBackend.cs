using Talt.Client.Audio;

namespace Talt.Client.Tests.Audio;

/// <summary>Audio backend that records calls and never touches a sound device.</summary>
internal sealed class FakeAudioBackend(bool available = true) : IAudioBackend
{
    public bool IsAvailable { get; } = available;

    public string? UnavailableReason => IsAvailable ? null : "fake: no device";

    public float[] BusVolumes { get; } = [1f, 1f, 1f, 1f];

    public List<FakeAudioVoice> Voices { get; } = [];

    public bool FailPlayback { get; set; }

    public IAudioVoice? CreateVoice(AudioBus bus)
    {
        if (!IsAvailable)
        {
            return null;
        }

        var voice = new FakeAudioVoice(this, bus);
        Voices.Add(voice);
        return voice;
    }

    public void SetBusVolume(AudioBus bus, float volume) => BusVolumes[(int)bus] = volume;

    public void Update()
    {
    }

    public void Dispose()
    {
    }

    public FakeAudioVoice? LastVoiceOn(AudioBus bus)
    {
        for (var i = Voices.Count - 1; i >= 0; i--)
        {
            if (Voices[i].Bus == bus)
            {
                return Voices[i];
            }
        }

        return null;
    }

    public FakeAudioVoice? VoicePlaying(string fileEndsWith)
    {
        for (var i = Voices.Count - 1; i >= 0; i--)
        {
            if (Voices[i].IsPlaying && Voices[i].File?.EndsWith(fileEndsWith, StringComparison.Ordinal) == true)
            {
                return Voices[i];
            }
        }

        return null;
    }

    internal sealed class FakeAudioVoice(FakeAudioBackend owner, AudioBus bus) : IAudioVoice
    {
        public AudioBus Bus { get; } = bus;

        public string? File { get; private set; }

        public bool Loop { get; private set; }

        public bool Stream { get; private set; }

        public float Volume { get; private set; } = 1f;

        public float Pan { get; private set; }

        public float Pitch { get; private set; } = 1f;

        public bool IsPlaying { get; private set; }

        public int PlayCount { get; private set; }

        public bool Play(string filePath, bool loop, bool stream)
        {
            if (owner.FailPlayback)
            {
                return false;
            }

            File = filePath;
            Loop = loop;
            Stream = stream;
            PlayCount++;
            IsPlaying = true;
            return true;
        }

        public void Stop() => IsPlaying = false;

        public void SetVolume(float volume) => Volume = volume;

        public void SetPan(float pan) => Pan = pan;

        public void SetPitch(float pitch) => Pitch = pitch;

        public void EndPlayback() => IsPlaying = false;

        public void Dispose() => IsPlaying = false;
    }
}

/// <summary>Builds a valid definition without touching disk.</summary>
internal static class AudioTestData
{
    public static AudioDefinition Definition()
    {
        var definition = new AudioDefinition
        {
            Version = 1,
            VoiceBudget = 8,
            OneShotVoices = 4,
            AmbientVoices = 2,
            MaxVehicleDistanceTiles = 220f,
            HysteresisTiles = 4f,
        };
        foreach (var cue in Enum.GetValues<AudioCue>())
        {
            var isEngine = cue == AudioCue.VehicleEngine;
            definition.Cues[cue.ToString()] = new SoundDefinition
            {
                File = $"effects/{cue}.{(isEngine ? "flac" : "wav")}",
                Bus = AudioBus.Effects,
                Gain = 1f,
                Priority = 1,
                Loop = isEngine,
                Stream = isEngine,
            };
        }

        foreach (var environment in Enum.GetValues<AudioEnvironment>())
        {
            definition.Ambient[environment.ToString()] = new SoundDefinition
            {
                File = $"ambient/{environment}.flac",
                Bus = AudioBus.Ambient,
                Gain = 0.5f,
                Loop = true,
                Stream = true,
            };
        }

        definition.Music.Tracks = ["music/one.flac", "music/two.flac"];
        definition.Music.Gain = 0.8f;
        definition.Music.Stream = true;
        definition.Validate("audio", _ => true);
        return definition;
    }
}

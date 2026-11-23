using MiniAudioEx.Core.AdvancedAPI;
using MiniAudioEx.Native;

namespace Talt.Client.Audio;

/// <summary>
/// The only adapter that talks to miniaudio (through MiniAudioEx). It creates one engine, four
/// sound groups (master, music, effects, ambient) and native voices. Initialization failure is not
/// fatal: <see cref="TryCreate"/> returns null and the game uses the silent backend.
/// </summary>
internal sealed class MiniAudioBackend : IAudioBackend
{
    private readonly MaEngine _engine;
    private readonly MaSoundGroup[] _buses;
    private readonly List<MiniAudioVoice> _voices = [];

    private MiniAudioBackend(MaEngine engine, MaSoundGroup[] buses)
    {
        _engine = engine;
        _buses = buses;
    }

    public bool IsAvailable => true;

    public string? UnavailableReason => null;

    /// <summary>Creates the engine and buses; returns null with a reason when audio is unavailable.</summary>
#pragma warning disable CA1031 // Audio is optional: any native failure must degrade to silence, never stop the game.
    public static MiniAudioBackend? TryCreate(out string? reason)
    {
        MaEngine? engine = null;
        MaSoundGroup[]? buses = null;
        var initialized = 0;
        try
        {
            engine = new MaEngine();
            var result = engine.Initialize();
            if (result != ma_result.success)
            {
                reason = $"miniaudio could not initialize ({result})";
                engine.Dispose();
                return null;
            }

            buses = new MaSoundGroup[4];
            for (var i = 0; i < buses.Length; i++)
            {
                buses[i] = new MaSoundGroup();
                var groupResult = i == (int)AudioBus.Master
                    ? buses[i].Initialize(engine)
                    : buses[i].Initialize(engine, (ma_sound_flags)0, buses[(int)AudioBus.Master]);
                if (groupResult != ma_result.success)
                {
                    reason = $"miniaudio could not create the {((AudioBus)i).ToString().ToLowerInvariant()} bus ({groupResult})";
                    Dispose(buses, initialized, engine, null);
                    return null;
                }

                initialized++;
            }

            reason = null;
            return new MiniAudioBackend(engine, buses);
        }
        catch (Exception exception)
        {
            reason = $"audio is unavailable: {exception.Message}";
            Dispose(buses, initialized, engine, null);
            return null;
        }
    }
#pragma warning restore CA1031

    public IAudioVoice? CreateVoice(AudioBus bus)
    {
        if (_buses.Length <= (int)bus)
        {
            return null;
        }

        var voice = new MiniAudioVoice(_engine, _buses[(int)bus]);
        _voices.Add(voice);
        return voice;
    }

    public void SetBusVolume(AudioBus bus, float volume) =>
        _buses[(int)bus].SetVolume(Math.Clamp(volume, 0f, 1f));

    public void Update()
    {
    }

    public void Dispose()
    {
        foreach (var voice in _voices)
        {
            voice.Dispose();
        }

        _voices.Clear();
        for (var i = _buses.Length - 1; i >= 0; i--)
        {
            _buses[i].Dispose();
        }

        _engine.Dispose();
    }

    private static void Dispose(MaSoundGroup[]? groups, int initialized, MaEngine? engine, MiniAudioVoice? voice)
    {
        voice?.Dispose();
        if (groups is not null)
        {
            for (var i = initialized - 1; i >= 0; i--)
            {
                groups[i].Dispose();
            }
        }

        engine?.Dispose();
    }

    /// <summary>One native <see cref="MaSound"/> and the small amount of state needed to reuse it.</summary>
    private sealed class MiniAudioVoice(MaEngine engine, MaSoundGroup group) : IAudioVoice
    {
        private MaSound? _sound;
        private float _volume = 1f;
        private float _pan;
        private float _pitch = 1f;

        public bool IsPlaying => _sound?.IsPlaying() == true;

        public bool Play(string filePath, bool loop, bool stream)
        {
            _sound ??= new MaSound();
            var flags = (ma_sound_flags)0;
            if (loop)
            {
                flags |= ma_sound_flags.looping;
            }

            if (stream)
            {
                flags |= ma_sound_flags.stream;
            }

            if (_sound.InitializeFromFile(engine, filePath, flags, group) != ma_result.success)
            {
                return false;
            }

            _sound.SetVolume(_volume);
            _sound.SetPan(_pan);
            _sound.SetPitch(_pitch);
            return _sound.Start() == ma_result.success;
        }

        public void Stop() => _sound?.Stop();

        public void SetVolume(float volume)
        {
            var next = Math.Clamp(volume, 0f, 1f);
            if (MathF.Abs(next - _volume) < 0.0005f)
            {
                return;
            }

            _volume = next;
            _sound?.SetVolume(_volume);
        }

        public void SetPan(float pan)
        {
            var next = Math.Clamp(pan, -1f, 1f);
            if (MathF.Abs(next - _pan) < 0.0005f)
            {
                return;
            }

            _pan = next;
            _sound?.SetPan(_pan);
        }

        public void SetPitch(float pitch)
        {
            var next = Math.Clamp(pitch, 0.25f, 4f);
            if (MathF.Abs(next - _pitch) < 0.0001f)
            {
                return;
            }

            _pitch = next;
            _sound?.SetPitch(_pitch);
        }

        public void Dispose()
        {
            _sound?.Dispose();
            _sound = null;
        }
    }
}

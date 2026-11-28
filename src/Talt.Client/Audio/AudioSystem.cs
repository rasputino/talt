using System.Diagnostics;
using Talt.Core.Maps;

namespace Talt.Client.Audio;

/// <summary>
/// The client's audio service: loads the validated definition, creates the native backend or the
/// silent fallback, and turns cues, the camera and the vehicle snapshot into voices. It exists only
/// in <c>Talt.Client</c>; the simulation never references it.
/// </summary>
internal sealed class AudioSystem : IDisposable
{
    private const float AmbientFadeSeconds = 1.5f;
    private const float AmbientSampleSeconds = 0.25f;
    private const double MusicPollSeconds = 0.25;

    private readonly IAudioBackend _backend;
    private readonly AudioDefinition? _definition;
    private readonly AudioMixer? _mixer;
    private readonly IAudioVoice?[] _vehicleVoices;
    private readonly float[] _vehicleGain;
    private readonly float[] _vehiclePan;
    private readonly int[] _vehicleVoiceId;
    private readonly SoundDefinition?[] _vehicleSound;
    private readonly IAudioVoice?[] _oneShotVoices;
    private readonly float[] _oneShotGain;
    private readonly float[] _oneShotTarget;
    private readonly IAudioVoice?[] _ambientVoices;
    private readonly IAudioVoice? _musicVoice;
    private readonly bool[] _failedAmbient = new bool[3];
    private readonly HashSet<AudioCue> _failedCues = [];

    private MusicPlaylist? _playlist;
    private AmbientMap? _ambients;
    private AudioEnvironment _ambientEnvironment = AudioEnvironment.Country;
    private int _ambientCurrent = -1;
    private int _ambientNext = -1;
    private float _ambientFade = 1f;
    private float _ambientTimer;
    private double _musicTimer;
    private string? _musicTrack;
    private bool _musicFailedLogged;
    private bool _vehicleSoundFailed;
    private bool _disposed;
    private double _lastUpdateMilliseconds;
    private double _averageUpdateMilliseconds;
    private int _activeVoices;

    internal AudioSystem(IAudioBackend backend, AudioDefinition? definition, PlayerSettings settings)
    {
        _backend = backend;
        _definition = definition;
        if (definition is null)
        {
            _vehicleVoices = [];
            _vehicleGain = [];
            _vehiclePan = [];
            _vehicleVoiceId = [];
            _vehicleSound = [];
            _oneShotVoices = [];
            _oneShotGain = [];
            _oneShotTarget = [];
            _ambientVoices = [];
            _musicVoice = null;
            return;
        }

        _mixer = new AudioMixer(definition, settings);
        _vehicleVoices = new IAudioVoice?[definition.VoiceBudget];
        _vehicleGain = new float[definition.VoiceBudget];
        _vehiclePan = new float[definition.VoiceBudget];
        _vehicleVoiceId = new int[definition.VoiceBudget];
        _vehicleSound = new SoundDefinition?[definition.VoiceBudget];
        for (var i = 0; i < _vehicleVoices.Length; i++)
        {
            _vehicleVoices[i] = backend.CreateVoice(AudioBus.Effects);
            _vehicleVoiceId[i] = -1;
        }

        _oneShotVoices = new IAudioVoice?[definition.OneShotVoices];
        _oneShotGain = new float[definition.OneShotVoices];
        _oneShotTarget = new float[definition.OneShotVoices];
        _ambientVoices = new IAudioVoice?[definition.AmbientVoices];
        for (var i = 0; i < _oneShotVoices.Length; i++)
        {
            _oneShotVoices[i] = backend.CreateVoice(AudioBus.Effects);
        }

        for (var i = 0; i < _ambientVoices.Length; i++)
        {
            _ambientVoices[i] = backend.CreateVoice(AudioBus.Ambient);
        }

        _musicVoice = backend.CreateVoice(AudioBus.Music);
        ApplySettings(settings);
        StartMusic();
    }

    /// <summary>Asset root copied next to the executable.</summary>
    public static string DefaultAssetRoot => AudioAssets.DefaultRoot;

    public bool IsAvailable => _backend.IsAvailable;

    public string? UnavailableReason => _backend.UnavailableReason;

    internal AmbientMap? Ambients => _ambients;

    public AudioStatistics Statistics => new(
        _lastUpdateMilliseconds,
        _averageUpdateMilliseconds,
        _activeVoices,
        _mixer?.VehicleSlots.Length ?? 0,
        _musicTrack,
        _backend.UnavailableReason);

    /// <summary>
    /// Builds the audio service. Never throws: bad data, a missing native library or no device all
    /// produce a working silent system with one console message.
    /// </summary>
    public static AudioSystem TryCreate(PlayerSettings settings, string assetRoot, Action<string> log, bool disabled = false)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(log);
        if (disabled)
        {
            log("Audio: disabled by --no-audio.");
            return CreateSilent("audio disabled by --no-audio");
        }

        AudioDefinition definition;
        try
        {
            definition = AudioDefinition.Load(assetRoot);
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            log($"Audio: {exception.Message} Disabling audio.");
            return CreateSilent(exception.Message);
        }

        var backend = MiniAudioBackend.TryCreate(out var reason);
        if (backend is null)
        {
            log($"Audio: {reason ?? "no output device"}. Continuing silently.");
            return new AudioSystem(new NullAudioBackend(reason), definition, settings);
        }

        return new AudioSystem(backend, definition, settings);
    }

    /// <summary>A silent instance for tests and screens that never play audio.</summary>
    public static AudioSystem CreateSilent(string? reason = null) =>
        new(new NullAudioBackend(reason), null, new PlayerSettings());

    public void ApplySettings(PlayerSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _mixer?.ApplySettings(settings);
        if (_mixer is null || !_backend.IsAvailable)
        {
            return;
        }

        _backend.SetBusVolume(AudioBus.Master, _mixer.BusGain(AudioBus.Master));
        _backend.SetBusVolume(AudioBus.Music, _mixer.BusGain(AudioBus.Music));
        _backend.SetBusVolume(AudioBus.Effects, _mixer.BusGain(AudioBus.Effects));
        _backend.SetBusVolume(AudioBus.Ambient, _mixer.BusGain(AudioBus.Ambient));
    }

    /// <summary>Starts a game's ambience map and stops sounds that belonged to the previous game.</summary>
    public void SetGame(MapData map)
    {
        ArgumentNullException.ThrowIfNull(map);
        if (_definition is null)
        {
            return;
        }

        StopGameSounds();
        _ambients = new AmbientMap(map);
        _ambientEnvironment = AudioEnvironment.Country;
        _ambientCurrent = -1;
        _ambientNext = -1;
        _ambientFade = 1f;
        _ambientTimer = 0f;
        StartAmbient(AudioEnvironment.Country);
    }

    /// <summary>Stops vehicle, ambience and one-shot sounds but keeps music playing across screens.</summary>
    public void ClearGame()
    {
        // Do not stop voices here: the next Update with InGame=false fades the engines and the
        // ambient bed out, so returning to the menu does not click.
        _ambients = null;
        if (_ambientNext >= 0)
        {
            _ambientVoices[_ambientNext]?.Stop();
            _ambientNext = -1;
        }
    }

    public void PlayInterfaceClick() => PlayCue(AudioCue.InterfaceClick);

    public void PlayConstruction() => PlayCue(AudioCue.Construction);

    public void PlayCommandFeedback(bool succeeded, bool construction)
    {
        if (!succeeded)
        {
            PlayCue(AudioCue.CommandRejected);
        }
        else if (construction)
        {
            PlayCue(AudioCue.Construction);
        }
        else
        {
            PlayCue(AudioCue.CommandApplied);
        }
    }

    /// <summary>Once per frame. Builds no managed objects and never blocks on the device.</summary>
    public void Update(in AudioScene scene)
    {
        if (_disposed || _definition is null || _mixer is null || !_backend.IsAvailable)
        {
            return;
        }

        var start = Stopwatch.GetTimestamp();
        var delta = float.IsFinite(scene.DeltaSeconds) ? Math.Clamp(scene.DeltaSeconds, 0f, 0.1f) : 0f;
        _mixer.UpdateVehicles(scene);
        UpdateVehicles(delta, scene.RoadVehicles);
        UpdateOneShots(delta);
        UpdateAmbience(scene, delta);
        UpdateMusic(delta);
        _backend.Update();
        _activeVoices = CountActiveVoices();
        _lastUpdateMilliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        _averageUpdateMilliseconds = _averageUpdateMilliseconds <= 0
            ? _lastUpdateMilliseconds
            : (_averageUpdateMilliseconds * 0.95) + (_lastUpdateMilliseconds * 0.05);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _backend.Dispose();
    }

    private void PlayCue(AudioCue cue)
    {
        if (!_backend.IsAvailable || _mixer is null || _definition is null || _oneShotVoices.Length == 0 || _failedCues.Contains(cue))
        {
            return;
        }

        var sound = _definition.Cue(cue);
        var slot = _mixer.RequestOneShot(cue, sound.Priority, sound.Gain);
        if (slot < 0)
        {
            return;
        }

        var voice = _oneShotVoices[slot];
        voice?.SetVolume(0f);
        if (voice is null || !voice.Play(sound.FullPath, false, false))
        {
            _mixer.ReleaseOneShot(slot);
            if (_failedCues.Add(cue))
            {
                Console.Error.WriteLine($"Audio: could not play '{cue}'. It will stay silent.");
            }

            return;
        }

        _oneShotGain[slot] = 0f;
        _oneShotTarget[slot] = sound.Gain;
        voice.SetPan(0f);
        voice.SetPitch(1f);
    }

    private void UpdateVehicles(float delta, Talt.Core.Vehicles.RoadVehicleSystem? vehicles)
    {
        if (_mixer is null || _definition is null || _vehicleSoundFailed)
        {
            return;
        }

        const float FadeInSeconds = 0.03f;
        const float FadeOutSeconds = 0.08f;
        var defaultEngine = _definition.Cue(AudioCue.VehicleEngine);
        var slots = _mixer.VehicleSlots;
        for (var i = 0; i < slots.Length; i++)
        {
            var voice = _vehicleVoices[i];
            if (voice is null)
            {
                continue;
            }

            var slot = slots[i];
            if (!slot.Active)
            {
                if (voice.IsPlaying)
                {
                    _vehicleGain[i] = MathF.Max(0f, _vehicleGain[i] - (delta / FadeOutSeconds));
                    voice.SetVolume(_vehicleGain[i]);
                    if (_vehicleGain[i] <= 0f)
                    {
                        voice.Stop();
                        _vehicleVoiceId[i] = -1;
                    }
                }
                else
                {
                    _vehicleVoiceId[i] = -1;
                }

                continue;
            }

            var vehicleSlot = vehicles?.SlotOf(slot.VehicleId) ?? -1;
            var engine = vehicleSlot >= 0 ? _definition.Cues[vehicles!.ModelAt(vehicleSlot).SoundId] : defaultEngine;
            if (_vehicleVoiceId[i] != slot.VehicleId || _vehicleSound[i] != engine)
            {
                if (voice.IsPlaying)
                {
                    _vehicleGain[i] = MathF.Max(0f, _vehicleGain[i] - (delta / FadeOutSeconds));
                    voice.SetVolume(_vehicleGain[i]);
                    if (_vehicleGain[i] > 0f)
                    {
                        continue;
                    }

                    voice.Stop();
                }

                voice.SetVolume(0f);
                if (!voice.Play(engine.FullPath, true, true))
                {
                    _vehicleSoundFailed = true;
                    Console.Error.WriteLine("Audio: could not play the vehicle engine sound. Vehicles will stay silent.");
                    return;
                }

                _vehicleVoiceId[i] = slot.VehicleId;
                _vehicleSound[i] = engine;
                _vehicleGain[i] = 0f;
                _vehiclePan[i] = slot.Pan;
            }

            _vehicleGain[i] = MathF.Min(slot.Gain, _vehicleGain[i] + (MathF.Max(slot.Gain, 0.0001f) * delta / FadeInSeconds) + 0.0001f);
            _vehiclePan[i] = MoveTowards(_vehiclePan[i], slot.Pan, delta / 0.05f);
            voice.SetVolume(_vehicleGain[i]);
            voice.SetPan(_vehiclePan[i]);
            voice.SetPitch(1f + ((slot.VehicleId % 7) * 0.015f));
        }
    }

    private void UpdateOneShots(float delta)
    {
        if (_mixer is null)
        {
            return;
        }

        var slots = _mixer.OneShots;
        for (var i = 0; i < slots.Length; i++)
        {
            var voice = _oneShotVoices[i];
            if (!slots[i].Active)
            {
                if (voice?.IsPlaying == true)
                {
                    voice.Stop();
                }

                _oneShotGain[i] = 0f;
                continue;
            }

            if (voice is null || !voice.IsPlaying)
            {
                _mixer.ReleaseOneShot(i);
                _oneShotGain[i] = 0f;
                continue;
            }

            _oneShotGain[i] = MathF.Min(_oneShotTarget[i], _oneShotGain[i] + (_oneShotTarget[i] * delta / 0.01f) + 0.0001f);
            voice.SetVolume(_oneShotGain[i]);
        }
    }

    private void UpdateAmbience(in AudioScene scene, float delta)
    {
        if (_definition is null || _ambientVoices.Length == 0)
        {
            return;
        }

        if (_ambients is null || !scene.InGame)
        {
            FadeAmbienceOut(delta);
            return;
        }

        _ambientTimer += delta;
        if (_ambientTimer >= AmbientSampleSeconds)
        {
            _ambientTimer = 0f;
            var environment = _ambients.EnvironmentAt(scene.CameraTarget.X, scene.CameraTarget.Z);
            if (environment != _ambientEnvironment)
            {
                StartAmbient(environment);
            }
        }

        if (_ambientCurrent < 0)
        {
            return;
        }

        var sound = _definition.AmbientBed(_ambientEnvironment);
        var vehicle = _definition.Vehicle;
        var zoom = Math.Clamp(vehicle.ZoomReference / MathF.Max(scene.CameraDistance, 1f), vehicle.MinZoomGain, 1f);
        var target = sound.Gain * zoom;
        if (_ambientNext < 0)
        {
            _ambientVoices[_ambientCurrent]?.SetVolume(target);
            return;
        }

        _ambientFade = Math.Min(1f, _ambientFade + (delta / AmbientFadeSeconds));
        _ambientVoices[_ambientCurrent]?.SetVolume(target * (1f - _ambientFade));
        _ambientVoices[_ambientNext]?.SetVolume(target * _ambientFade);
        if (_ambientFade >= 1f)
        {
            _ambientVoices[_ambientCurrent]?.Stop();
            _ambientCurrent = _ambientNext;
            _ambientNext = -1;
        }
    }

    private void FadeAmbienceOut(float delta)
    {
        if (_ambientCurrent < 0 || _ambientVoices.Length == 0)
        {
            return;
        }

        _ambientFade = Math.Max(0f, _ambientFade - (delta / AmbientFadeSeconds));
        _ambientVoices[_ambientCurrent]?.SetVolume(_ambientFade);
        if (_ambientFade <= 0f)
        {
            _ambientVoices[_ambientCurrent]?.Stop();
            _ambientCurrent = -1;
        }
    }

    private void StartAmbient(AudioEnvironment environment)
    {
        if (_definition is null || _ambientVoices.Length == 0 || _failedAmbient[(int)environment])
        {
            return;
        }

        var slot = _ambientCurrent < 0 ? 0 : 1 - _ambientCurrent;
        var voice = _ambientVoices[slot];
        if (voice is null)
        {
            return;
        }

        var sound = _definition.AmbientBed(environment);
        voice.SetVolume(0f);
        if (!voice.Play(sound.FullPath, true, true))
        {
            _failedAmbient[(int)environment] = true;
            Console.Error.WriteLine($"Audio: could not play the {environment} ambience. Continuing without it.");
            return;
        }

        _ambientEnvironment = environment;
        voice.SetPan(0f);
        voice.SetPitch(1f);
        if (_ambientCurrent < 0)
        {
            _ambientCurrent = slot;
            _ambientNext = -1;
            _ambientFade = 1f;
            voice.SetVolume(sound.Gain);
        }
        else
        {
            _ambientNext = slot;
            _ambientFade = 0f;
            voice.SetVolume(0f);
        }
    }

    private void StartMusic()
    {
        if (_definition is null || _musicVoice is null)
        {
            return;
        }

        _playlist = new MusicPlaylist(_definition.Music.FullTracks);
        var track = _playlist.Begin();
        if (track is not null)
        {
            StartMusicTrack(track);
        }
    }

    private void StartMusicTrack(string track)
    {
        if (_musicVoice is null || _definition is null)
        {
            return;
        }

        _musicVoice.SetVolume(0f);
        if (_musicVoice.Play(track, false, true))
        {
            _musicTrack = track;
            _musicVoice.SetVolume(_definition.Music.Gain);
            _musicVoice.SetPan(0f);
            _musicVoice.SetPitch(1f);
            return;
        }

        _playlist?.MarkFailed();
        var next = _playlist?.Next();
        if (next is not null)
        {
            StartMusicTrack(next);
            return;
        }

        _musicTrack = null;
        if (!_musicFailedLogged)
        {
            _musicFailedLogged = true;
            Console.Error.WriteLine("Audio: no music track could be played. Music is disabled.");
        }
    }

    private void UpdateMusic(float delta)
    {
        if (_musicVoice is null || _playlist is null || _playlist.IsDisabled || _definition is null)
        {
            return;
        }

        _musicTimer += delta;
        if (_musicTimer < MusicPollSeconds)
        {
            return;
        }

        _musicTimer = 0;
        if (_musicVoice.IsPlaying)
        {
            return;
        }

        var next = _playlist.Next();
        if (next is null)
        {
            _musicTrack = null;
            return;
        }

        StartMusicTrack(next);
    }

    private void StopGameSounds()
    {
        for (var i = 0; i < _vehicleVoices.Length; i++)
        {
            _vehicleVoices[i]?.Stop();
            _vehicleVoiceId[i] = -1;
            _vehicleGain[i] = 0f;
            _vehiclePan[i] = 0f;
        }

        for (var i = 0; i < _oneShotVoices.Length; i++)
        {
            _oneShotVoices[i]?.Stop();
            _oneShotGain[i] = 0f;
            _oneShotTarget[i] = 0f;
        }

        foreach (var voice in _ambientVoices)
        {
            voice?.Stop();
        }

        _mixer?.ClearVehicles();
        _mixer?.ClearOneShots();
    }

    private static float MoveTowards(float current, float target, float maxDelta) =>
        current < target ? MathF.Min(current + maxDelta, target) : MathF.Max(current - maxDelta, target);

    private int CountActiveVoices()
    {        var count = 0;
        foreach (var voice in _vehicleVoices)
        {
            if (voice?.IsPlaying == true)
            {
                count++;
            }
        }

        foreach (var voice in _oneShotVoices)
        {
            if (voice?.IsPlaying == true)
            {
                count++;
            }
        }

        foreach (var voice in _ambientVoices)
        {
            if (voice?.IsPlaying == true)
            {
                count++;
            }
        }

        if (_musicVoice?.IsPlaying == true)
        {
            count++;
        }

        return count;
    }
}

/// <summary>Per-frame audio measurements shown in the F3 statistics (feature 019 SC-002).</summary>
internal readonly record struct AudioStatistics(
    double LastUpdateMilliseconds,
    double AverageUpdateMilliseconds,
    int ActiveVoices,
    int VoiceBudget,
    string? MusicTrack,
    string? UnavailableReason);

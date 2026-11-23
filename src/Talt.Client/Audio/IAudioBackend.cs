namespace Talt.Client.Audio;

/// <summary>
/// One native sound voice. Implemented by <see cref="MiniAudioBackend"/> and by a fake in tests
/// (contracts/audio-api.md); the mixer only sees this interface so no test needs a device.
/// </summary>
internal interface IAudioVoice : IDisposable
{
    bool IsPlaying { get; }

    /// <summary>Loads and starts a sound. Returns false when the file cannot be decoded.</summary>
    bool Play(string filePath, bool loop, bool stream);

    void Stop();

    void SetVolume(float volume);

    /// <summary>-1 is fully left, +1 fully right.</summary>
    void SetPan(float pan);

    /// <summary>1 is the original pitch.</summary>
    void SetPitch(float pitch);
}

/// <summary>
/// The small boundary to the native audio library (contracts/audio-api.md). A null/available=false
/// backend is the silent fallback used when there is no device or audio is disabled.
/// </summary>
internal interface IAudioBackend : IDisposable
{
    bool IsAvailable { get; }

    string? UnavailableReason { get; }

    /// <summary>Creates a voice assigned to one bus, or null when the backend cannot provide it.</summary>
    IAudioVoice? CreateVoice(AudioBus bus);

    /// <summary>Sets a bus volume (0–1); the caller has already folded mute and parent buses in.</summary>
    void SetBusVolume(AudioBus bus, float volume);

    /// <summary>Device house-keeping; may be a no-op.</summary>
    void Update();
}

namespace Talt.Client.Audio;

/// <summary>
/// Silent backend used when no audio device exists, initialization failed, or the player passed
/// <c>--no-audio</c>. It keeps the rest of the audio system running without producing sound.
/// </summary>
internal sealed class NullAudioBackend(string? reason = null) : IAudioBackend
{
    public bool IsAvailable => false;

    public string? UnavailableReason { get; } = reason ?? "audio is disabled";

    public IAudioVoice? CreateVoice(AudioBus bus) => null;

    public void SetBusVolume(AudioBus bus, float volume)
    {
    }

    public void Update()
    {
    }

    public void Dispose()
    {
    }
}

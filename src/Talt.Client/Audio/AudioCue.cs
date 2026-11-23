namespace Talt.Client.Audio;

/// <summary>One of the mixer's four volume buses (feature 019, data-model.md AudioBus).</summary>
internal enum AudioBus
{
    /// <summary>Final gain applied to every sound; mute acts here.</summary>
    Master,

    /// <summary>Streamed background music only.</summary>
    Music,

    /// <summary>Interface, command, construction and vehicle sounds.</summary>
    Effects,

    /// <summary>Countryside, water and town beds.</summary>
    Ambient,
}

/// <summary>Events that can start a sound (data-model.md AudioCue).</summary>
internal enum AudioCue
{
    InterfaceClick,
    CommandApplied,
    CommandRejected,
    Construction,
    VehicleEngine,
}

/// <summary>What the camera is looking at, for the ambient bed (data-model.md AudioEnvironment).</summary>
internal enum AudioEnvironment
{
    Country,
    Water,
    Town,
}

using System.Globalization;
using System.Text.Json.Serialization;

namespace Talt.Client.Audio;

/// <summary>One sound asset and how it is mixed (data-model.md SoundDefinition).</summary>
internal sealed class SoundDefinition
{
    /// <summary>Path relative to the audio root, using forward slashes in the data file.</summary>
    public string File { get; set; } = string.Empty;

    [JsonConverter(typeof(JsonStringEnumConverter<AudioBus>))]
    public AudioBus Bus { get; set; } = AudioBus.Effects;

    /// <summary>Gain applied before the bus volume; 0–2.</summary>
    public float Gain { get; set; } = 1f;

    /// <summary>Higher wins when one-shot voices are scarce; 0–100.</summary>
    public int Priority { get; set; }

    public bool Loop { get; set; }

    public bool Stream { get; set; }

    /// <summary>Absolute path filled in while validating the data file.</summary>
    [JsonIgnore]
    public string FullPath { get; set; } = string.Empty;
}

/// <summary>The streamed music playlist definition (data-model.md MusicDefinition).</summary>
internal sealed class MusicDefinition
{
    public string[] Tracks { get; set; } = [];

    public float Gain { get; set; } = 0.8f;

    public bool Stream { get; set; } = true;

    /// <summary>Absolute track paths filled in while validating the data file.</summary>
    [JsonIgnore]
    public string[] FullTracks { get; set; } = [];
}

/// <summary>Distance, zoom and pan curves for vehicle engines (data-model.md VehicleMixDefinition).</summary>
internal sealed class VehicleMixDefinition
{
    public float FullVolumeDistanceTiles { get; set; } = 18f;

    public float Rolloff { get; set; } = 1.4f;

    public float ZoomReference { get; set; } = 80f;

    public float MinZoomGain { get; set; } = 0.2f;

    public float BehindGain { get; set; } = 0.65f;

    public float PanWidth { get; set; } = 0.8f;
}

/// <summary>
/// Validated contents of <c>Audio/Data/audio.json</c> (feature 019, data-model.md AudioDefinition and
/// contracts/audio-api.md). Every player-facing value lives in the data file; code only refers to
/// cue, bus and environment ids.
/// </summary>
internal sealed class AudioDefinition
{
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;

    public int VoiceBudget { get; set; } = 8;

    public int OneShotVoices { get; set; } = 4;

    public int AmbientVoices { get; set; } = 2;

    public float MaxVehicleDistanceTiles { get; set; } = 220f;

    public float HysteresisTiles { get; set; } = 4f;

    public Dictionary<string, SoundDefinition> Cues { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, SoundDefinition> Ambient { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public MusicDefinition Music { get; set; } = new();

    public VehicleMixDefinition Vehicle { get; set; } = new();

    /// <summary>Cues indexed by <see cref="AudioCue"/> after validation.</summary>
    [JsonIgnore]
    public SoundDefinition[] CueByIndex { get; private set; } = [];

    /// <summary>Ambient beds indexed by <see cref="AudioEnvironment"/> after validation.</summary>
    [JsonIgnore]
    public SoundDefinition[] AmbientByIndex { get; private set; } = [];

    /// <summary>Absolute audio root filled in while loading.</summary>
    [JsonIgnore]
    public string AudioRoot { get; private set; } = string.Empty;

    public SoundDefinition Cue(AudioCue cue) => CueByIndex[(int)cue];

    public SoundDefinition AmbientBed(AudioEnvironment environment) => AmbientByIndex[(int)environment];

    /// <summary>Loads and validates the embedded definition against the audio assets on disk.</summary>
    public static AudioDefinition Load(string audioRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(audioRoot);
        var definition = DataFiles.Load<AudioDefinition>("audio.json");
        definition.Validate(audioRoot);
        return definition;
    }

    /// <summary>Validates every rule in data-model.md; throws a clear <see cref="InvalidDataException"/>.</summary>
    internal void Validate(string audioRoot, Func<string, bool>? fileExists = null)
    {
        fileExists ??= System.IO.File.Exists;
        AudioRoot = audioRoot;
        if (Version != CurrentVersion)
        {
            throw Invalid($"version must be {CurrentVersion}, got {Version.ToString(CultureInfo.InvariantCulture)}");
        }

        if (VoiceBudget is < 1 or > 32)
        {
            throw Invalid($"voiceBudget must be between 1 and 32, got {VoiceBudget.ToString(CultureInfo.InvariantCulture)}");
        }

        if (OneShotVoices is < 1 or > 16)
        {
            throw Invalid($"oneShotVoices must be between 1 and 16, got {OneShotVoices.ToString(CultureInfo.InvariantCulture)}");
        }

        if (AmbientVoices is < 2 or > 8)
        {
            throw Invalid($"ambientVoices must be between 2 and 8, got {AmbientVoices.ToString(CultureInfo.InvariantCulture)}");
        }

        if (!IsPositive(MaxVehicleDistanceTiles))
        {
            throw Invalid("maxVehicleDistanceTiles must be greater than zero");
        }

        if (HysteresisTiles is < 0 or > 20)
        {
            throw Invalid("hysteresisTiles must be between 0 and 20");
        }

        if (Cues is null || Ambient is null)
        {
            throw Invalid("cues and ambient must be objects");
        }

        ValidateVehicle(Vehicle);
        ValidateMusic(Music, fileExists);

        CueByIndex = new SoundDefinition[Enum.GetValues<AudioCue>().Length];
        foreach (var cue in Enum.GetValues<AudioCue>())
        {
            if (!TryGet(Cues, cue.ToString(), out var sound) || sound is null)
            {
                throw Invalid($"cues must define {cue}");
            }

            ValidateSound(sound, $"cues.{cue}", fileExists);
            if (cue == AudioCue.VehicleEngine)
            {
                Require(sound.Loop && sound.Stream, $"cues.{cue} must be a streamed loop");
            }
            else
            {
                Require(!sound.Loop && !sound.Stream, $"cues.{cue} must be a non-streamed one-shot");
            }

            CueByIndex[(int)cue] = sound;
        }

        foreach (var key in Cues.Keys)
        {
            if (!Enum.TryParse<AudioCue>(key, ignoreCase: true, out _))
            {
                throw Invalid($"cues has unknown cue '{key}'");
            }
        }

        AmbientByIndex = new SoundDefinition[Enum.GetValues<AudioEnvironment>().Length];
        foreach (var environment in Enum.GetValues<AudioEnvironment>())
        {
            if (!TryGet(Ambient, environment.ToString(), out var sound) || sound is null)
            {
                throw Invalid($"ambient must define {environment}");
            }

            ValidateSound(sound, $"ambient.{environment}", fileExists);
            Require(sound.Loop && sound.Stream, $"ambient.{environment} must be a streamed loop");
            Require(sound.Bus == AudioBus.Ambient, $"ambient.{environment} must use the ambient bus");
            AmbientByIndex[(int)environment] = sound;
        }

        foreach (var key in Ambient.Keys)
        {
            if (!Enum.TryParse<AudioEnvironment>(key, ignoreCase: true, out _))
            {
                throw Invalid($"ambient has unknown environment '{key}'");
            }
        }
    }

    private static void ValidateVehicle(VehicleMixDefinition vehicle)
    {
        if (vehicle is null)
        {
            throw Invalid("vehicle is required");
        }

        if (vehicle.FullVolumeDistanceTiles < 0 || !float.IsFinite(vehicle.FullVolumeDistanceTiles))
        {
            throw Invalid("vehicle.fullVolumeDistanceTiles must be zero or greater");
        }

        Require(IsPositive(vehicle.Rolloff), "vehicle.rolloff must be greater than zero");
        Require(IsPositive(vehicle.ZoomReference), "vehicle.zoomReference must be greater than zero");
        Require(vehicle.MinZoomGain is >= 0 and <= 1, "vehicle.minZoomGain must be between 0 and 1");
        Require(vehicle.BehindGain is >= 0 and <= 1, "vehicle.behindGain must be between 0 and 1");
        Require(vehicle.PanWidth is >= 0 and <= 1, "vehicle.panWidth must be between 0 and 1");
    }

    private void ValidateMusic(MusicDefinition music, Func<string, bool> fileExists)
    {
        if (music is null || music.Tracks.Length == 0)
        {
            throw Invalid("music.tracks must contain at least one track");
        }

        Require(music.Stream, "music.stream must be true");
        Require(music.Gain is >= 0 and <= 2, "music.gain must be between 0 and 2");
        music.FullTracks = new string[music.Tracks.Length];
        for (var i = 0; i < music.Tracks.Length; i++)
        {
            var path = Resolve(music.Tracks[i], "music.tracks", fileExists);
            music.FullTracks[i] = path;
        }
    }

    private void ValidateSound(SoundDefinition sound, string field, Func<string, bool> fileExists)
    {
        Require(sound.Bus != AudioBus.Master, $"{field}.bus must not be master");
        Require(sound.Gain is >= 0 and <= 2, $"{field}.gain must be between 0 and 2");
        Require(sound.Priority is >= 0 and <= 100, $"{field}.priority must be between 0 and 100");
        sound.FullPath = Resolve(sound.File, field, fileExists);
    }

    private string Resolve(string file, string field, Func<string, bool> fileExists)
    {
        if (string.IsNullOrWhiteSpace(file))
        {
            throw Invalid($"{field}.file must not be empty");
        }

        if (Path.IsPathRooted(file) || file.Split('/').Any(part => part == ".."))
        {
            throw Invalid($"{field}.file must be a relative path without '..'");
        }

        var full = Path.Combine(AudioRoot, file.Replace('/', Path.DirectorySeparatorChar));
        if (!fileExists(full))
        {
            throw Invalid($"{field}.file '{file}' does not exist under 'audio/'");
        }

        return full;
    }

    private static bool IsPositive(float value) => float.IsFinite(value) && value > 0;

    private static bool TryGet(Dictionary<string, SoundDefinition> sounds, string name, out SoundDefinition? sound)
    {
        foreach (var pair in sounds)
        {
            if (string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase))
            {
                sound = pair.Value;
                return true;
            }
        }

        sound = null;
        return false;
    }

    private static void Require(bool condition, string problem)
    {
        if (!condition)
        {
            throw Invalid(problem);
        }
    }

    private static InvalidDataException Invalid(string problem) =>
        DataFiles.Invalid("audio.json", problem);
}

using System.Numerics;
using BenchmarkDotNet.Attributes;
using Talt.Client;
using Talt.Client.Audio;
using Talt.Client.Rendering;

namespace Talt.Benchmarks;

/// <summary>
/// Cost of the audio mixer for the reference vehicle count (feature 019 SC-002: under 0.5 ms per
/// frame and zero allocations after warm-up).
/// </summary>
[MemoryDiagnoser]
public class AudioBenchmarks
{
    private const int VehicleCount = 50_000;

    private AudioMixer _mixer = null!;
    private VehicleInstance[] _vehicles = null!;
    private int[] _ids = null!;

    [GlobalSetup]
    public void Setup()
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
            var engine = cue == AudioCue.VehicleEngine;
            definition.Cues[cue.ToString()] = new SoundDefinition
            {
                File = $"effects/{cue}.{(engine ? "flac" : "wav")}",
                Bus = AudioBus.Effects,
                Gain = 1f,
                Priority = 1,
                Loop = engine,
                Stream = engine,
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

        definition.Music.Tracks = ["music/one.flac"];
        definition.Music.Gain = 0.8f;
        definition.Music.Stream = true;
        definition.Validate("audio", _ => true);

        _mixer = new AudioMixer(definition, new PlayerSettings());
        _vehicles = new VehicleInstance[VehicleCount];
        _ids = new int[VehicleCount];
        for (var i = 0; i < VehicleCount; i++)
        {
            _vehicles[i] = new VehicleInstance(new Vector4((i % 250) * 2f, 0f, (i / 250) * 2f, 0f), default);
            _ids[i] = i;
        }

        for (var i = 0; i < 100; i++)
        {
            _mixer.UpdateVehicles(Scene());
        }
    }

    [Benchmark(Baseline = true)]
    public int UpdateVehicles()
    {
        _mixer.UpdateVehicles(Scene());
        var active = 0;
        foreach (var slot in _mixer.VehicleSlots)
        {
            if (slot.Active)
            {
                active++;
            }
        }

        return active;
    }

    private AudioScene Scene() => new()
    {
        InGame = true,
        CameraTarget = new Vector3(250f, 0f, 100f),
        CameraForward = Vector3.UnitZ,
        CameraRight = Vector3.UnitX,
        CameraDistance = 400f,
        Vehicles = _vehicles,
        VehicleIds = _ids,
        SelectedVehicleId = 123,
        HoveredVehicleId = 321,
        DeltaSeconds = 1f / 60f,
    };
}

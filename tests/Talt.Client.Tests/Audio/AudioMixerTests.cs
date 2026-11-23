using System.Numerics;
using Talt.Client.Audio;
using Talt.Client.Rendering;
using Xunit;

namespace Talt.Client.Tests.Audio;

public class AudioMixerTests
{
    [Fact]
    public void VoiceBudget_KeepsTheEightNearestVehicles()
    {
        var mixer = new AudioMixer(AudioTestData.Definition(), new PlayerSettings());
        var vehicles = new VehicleInstance[20];
        var ids = new int[20];
        for (var i = 0; i < vehicles.Length; i++)
        {
            vehicles[i] = Vehicle(i + 1, 0);
            ids[i] = 100 + i;
        }

        mixer.UpdateVehicles(Scene(vehicles, ids));
        var slots = mixer.VehicleSlots;
        var active = 0;
        for (var i = 0; i < slots.Length; i++)
        {
            if (slots[i].Active)
            {
                active++;
            }
        }

        Assert.Equal(8, active);
        var idsAudible = ActiveIds(slots);
        Array.Sort(idsAudible);
        Assert.Equal([100, 101, 102, 103, 104, 105, 106, 107], idsAudible);
    }

    [Fact]
    public void SelectedVehicle_WinsEvenWhenItIsFartherAway()    {
        var mixer = new AudioMixer(AudioTestData.Definition(), new PlayerSettings());
        var vehicles = new VehicleInstance[10];
        var ids = new int[10];
        for (var i = 0; i < vehicles.Length; i++)
        {
            vehicles[i] = Vehicle(i + 1, 0);
            ids[i] = i;
        }

        vehicles[9] = Vehicle(150, 0);
        var scene = Scene(vehicles, ids, selected: 9);
        mixer.UpdateVehicles(scene);
        Assert.Contains(9, ActiveIds(mixer.VehicleSlots));
    }

    [Fact]
    public void VehiclesToTheRight_ArePannedRight()
    {
        var mixer = new AudioMixer(AudioTestData.Definition(), new PlayerSettings());
        var vehicles = new[] { Vehicle(10, 10) };
        var ids = new[] { 1 };
        mixer.UpdateVehicles(Scene(vehicles, ids));
        Assert.True(mixer.VehicleSlots[0].Pan > 0.2f);
    }

    [Fact]
    public void ZoomingOut_LowersVehicleGain()
    {
        var mixer = new AudioMixer(AudioTestData.Definition(), new PlayerSettings());
        var vehicles = new[] { Vehicle(10, 10) };
        var ids = new[] { 1 };
        mixer.UpdateVehicles(Scene(vehicles, ids, cameraDistance: 80f));
        var close = mixer.VehicleSlots[0].Gain;
        mixer.UpdateVehicles(Scene(vehicles, ids, cameraDistance: 800f));
        var far = mixer.VehicleSlots[0].Gain;
        Assert.True(far < close);
        Assert.True(far > 0f);
    }

    [Fact]
    public void Buses_ApplyMuteAndMaster()
    {
        var mixer = new AudioMixer(AudioTestData.Definition(), new PlayerSettings());
        var settings = new PlayerSettings { MasterVolume = 0.5f, MusicVolume = 0.5f, EffectsVolume = 0.4f, AmbientVolume = 0.3f };
        mixer.ApplySettings(settings);
        Assert.Equal(0.5f, mixer.BusGain(AudioBus.Master));
        Assert.Equal(0.5f, mixer.BusGain(AudioBus.Music));
        Assert.Equal(0.4f, mixer.BusGain(AudioBus.Effects));
        Assert.Equal(0.25f, mixer.EffectiveGain(AudioBus.Music));

        settings.Muted = true;
        mixer.ApplySettings(settings);
        Assert.Equal(0f, mixer.EffectiveGain(AudioBus.Effects));
        Assert.Equal(0.5f, settings.MusicVolume);
    }

    [Fact]
    public void OneShotVoices_DropTheLowestPrioritySound()
    {
        var mixer = new AudioMixer(AudioTestData.Definition(), new PlayerSettings());
        for (var i = 0; i < 4; i++)
        {
            Assert.Equal(i, mixer.RequestOneShot(AudioCue.InterfaceClick, 10, 0.5f));
        }

        Assert.Equal(-1, mixer.RequestOneShot(AudioCue.CommandApplied, 5, 0.5f));
        var replaced = mixer.RequestOneShot(AudioCue.Construction, 20, 0.5f);
        Assert.InRange(replaced, 0, 3);
        Assert.Equal(AudioCue.Construction, mixer.OneShots[replaced].Cue);
    }

    [Fact]
    public void RankChanges_KeepTheSameVoiceSlotForEachVehicle()
    {
        var mixer = new AudioMixer(AudioTestData.Definition(), new PlayerSettings());
        var vehicles = new VehicleInstance[8];
        var ids = new int[8];
        for (var i = 0; i < 8; i++)
        {
            vehicles[i] = Vehicle(0, 10 + i);
            ids[i] = i;
        }

        mixer.UpdateVehicles(Scene(vehicles, ids));
        var before = SlotOf(mixer.VehicleSlots, 0);
        vehicles[0] = Vehicle(0, 100);
        vehicles[7] = Vehicle(0, 1);
        mixer.UpdateVehicles(Scene(vehicles, ids));
        Assert.Equal(before, SlotOf(mixer.VehicleSlots, 0));
    }

    [Fact]
    public void Attenuation_IsOneInsideTheFullVolumeRadius()
    {
        Assert.Equal(1f, AudioMixer.Attenuation(5f, 18f, 1.4f));
        Assert.True(AudioMixer.Attenuation(40f, 18f, 1.4f) < 1f);
        Assert.True(AudioMixer.Attenuation(40f, 18f, 1.4f) > 0f);
    }

    private static int[] ActiveIds(ReadOnlySpan<VoiceSlot> slots)
    {
        var ids = new List<int>();
        for (var i = 0; i < slots.Length; i++)
        {
            if (slots[i].Active)
            {
                ids.Add(slots[i].VehicleId);
            }
        }

        return ids.ToArray();
    }

    private static int SlotOf(ReadOnlySpan<VoiceSlot> slots, int id)
    {
        for (var i = 0; i < slots.Length; i++)
        {
            if (slots[i].Active && slots[i].VehicleId == id)
            {
                return i;
            }
        }

        return -1;
    }

    private static VehicleInstance Vehicle(float x, float z) => new(new Vector4(x, 0f, z, 0f), default);

    private static AudioScene Scene(VehicleInstance[] vehicles, int[] ids, int selected = -1, float cameraDistance = 80f) => new()
    {
        InGame = true,
        CameraTarget = Vector3.Zero,
        CameraForward = Vector3.UnitZ,
        CameraRight = Vector3.UnitX,
        CameraDistance = cameraDistance,
        Vehicles = vehicles,
        VehicleIds = ids,
        SelectedVehicleId = selected,
        HoveredVehicleId = -1,
        DeltaSeconds = 1f / 60f,
    };
}

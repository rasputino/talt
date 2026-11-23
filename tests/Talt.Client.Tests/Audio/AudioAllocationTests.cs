using System.Numerics;
using Talt.Client.Audio;
using Talt.Client.Rendering;
using Xunit;

namespace Talt.Client.Tests.Audio;

public class AudioAllocationTests
{
    [Fact]
    public void MixerUpdate_DoesNotAllocateAfterWarmUp()
    {
        var mixer = new AudioMixer(AudioTestData.Definition(), new PlayerSettings());
        var vehicles = new VehicleInstance[1000];
        var ids = new int[1000];
        for (var i = 0; i < vehicles.Length; i++)
        {
            vehicles[i] = new VehicleInstance(new Vector4((i % 50) * 2f, 0f, (i / 50) * 2f, 0f), default);
            ids[i] = i;
        }

        var scene = new AudioScene
        {
            InGame = true,
            CameraTarget = Vector3.Zero,
            CameraForward = Vector3.UnitZ,
            CameraRight = Vector3.UnitX,
            CameraDistance = 400f,
            Vehicles = vehicles,
            VehicleIds = ids,
            SelectedVehicleId = 999,
            HoveredVehicleId = 998,
            DeltaSeconds = 1f / 60f,
        };

        for (var i = 0; i < 100; i++)
        {
            mixer.UpdateVehicles(scene);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++)
        {
            mixer.UpdateVehicles(scene);
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, allocated);
    }
}

using System.Numerics;
using Talt.Client.Audio;
using Talt.Client.Rendering;
using Xunit;
using Talt.Tests.Support;

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

        // AudioScene is a ref struct, so it cannot be captured by the lambda: build it on each call (on the stack).
        AllocationAssert.NoSteadyStateAllocations(() => mixer.UpdateVehicles(Scene(vehicles, ids)), iterationsPerRound: 1000, "1000 audio vehicle updates");
    }

    private static AudioScene Scene(VehicleInstance[] vehicles, int[] ids) => new()
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
}

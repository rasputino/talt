using Talt.Core.Commands;
using Talt.Core.Scenarios;
using Talt.Core.Simulation;

namespace Talt.Core.Tests.Saves;

public class CoreSectionTests
{
    private static GameSimulation MapLess() => GameSimulation.Create(new ScenarioSettings(Seed: 9, MapSizeTiles: 128, VehicleCount: 300, LineCount: 20));

    [Fact]
    public void Map_less_scenario_round_trips_and_continues_identically()
    {
        var original = MapLess();
        for (var t = 0; t < 137; t++)
        {
            original.Tick(parallel: false);
        }

        var loaded = RoundTrip.SaveAndLoad(original);
        RoundTrip.AssertContinuesIdentically(original, loaded, 600);
    }

    [Fact]
    public void Pending_commands_are_saved_and_applied_after_loading()
    {
        var original = MapLess();
        original.Tick(parallel: false);
        original.Submit(new AddVehicleCommand(LineId: 1, MaxSpeed: 1000));
        original.Submit(new RemoveVehicleCommand(VehicleId: 3));

        var loaded = RoundTrip.SaveAndLoad(original);
        Assert.Equal(2, loaded.PendingCommandCount);
        RoundTrip.AssertContinuesIdentically(original, loaded, 50, every: 1);
        Assert.Equal(original.Vehicles.VehicleCount, loaded.Vehicles.VehicleCount);
    }

    [Fact]
    public void Parallel_ticks_after_loading_equal_sequential_ticks()
    {
        var original = MapLess();
        for (var t = 0; t < 40; t++)
        {
            original.Tick(parallel: true);
        }

        var bytes = RoundTrip.SaveBytes(original);
        var sequential = RoundTrip.Load(bytes);
        var parallel = RoundTrip.Load(bytes);
        for (var t = 0; t < 200; t++)
        {
            sequential.Tick(parallel: false);
            parallel.Tick(parallel: true);
        }

        Assert.Equal(sequential.ComputeFingerprint(), parallel.ComputeFingerprint());
    }
}

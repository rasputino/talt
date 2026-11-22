using Talt.Core.Commands;
using Talt.Core.Simulation;
using Talt.Core.Tests.Simulation;
using Talt.Core.Tests.TestCommands;

namespace Talt.Core.Tests.Commands;

public class VehicleCommandsTests
{
    private static readonly int Speed = SyntheticTraffic.MinCruiseSpeed;

    [Fact]
    public void AddVehicle_IsAppliedAtTheStartOfTheNextTick()
    {
        var simulation = GameSimulation.Create(SimulationTestScenarios.Tiny());
        var before = simulation.Vehicles.VehicleCount;

        simulation.Submit(new AddVehicleCommand(LineId: 0, MaxSpeed: Speed));
        Assert.Equal(before, simulation.Vehicles.VehicleCount); // Not applied on submit.

        simulation.Tick();
        Assert.Equal(before + 1, simulation.Vehicles.VehicleCount);
        Assert.True(simulation.Vehicles.IsAtStop(simulation.Vehicles.SlotOf(simulation.Vehicles.LastCreatedVehicleId)));
    }

    [Fact]
    public void RemoveVehicle_RemovesIt()
    {
        var simulation = GameSimulation.Create(SimulationTestScenarios.Tiny());
        var id = simulation.Vehicles.VehicleIdAt(0);

        simulation.Submit(new RemoveVehicleCommand(id));
        simulation.Tick();

        Assert.False(simulation.Vehicles.ContainsVehicle(id));
    }

    [Theory]
    [InlineData(99, 1000, 0, "line-not-found")]
    [InlineData(0, 0, 0, "invalid-speed")]
    [InlineData(0, int.MaxValue, 0, "invalid-speed")]
    [InlineData(0, 1000, 9999, "invalid-start-edge")]
    public void AddVehicle_InvalidValues_AreRejectedWithoutSideEffects(int line, int speed, int startEdge, string reason)
    {
        var simulation = GameSimulation.Create(SimulationTestScenarios.Tiny());
        var withCommand = GameSimulation.Create(SimulationTestScenarios.Tiny());
        CommandProcessed? processed = null;
        withCommand.CommandProcessed += p => processed = p;

        withCommand.Submit(new AddVehicleCommand(line, speed, startEdge));
        withCommand.Tick();
        simulation.Tick();

        Assert.False(processed!.Value.Result.Succeeded);
        Assert.Equal(reason, processed.Value.Result.ReasonCode);
        Assert.Equal(simulation.ComputeFingerprint(), withCommand.ComputeFingerprint());
    }

    [Fact]
    public void RemoveUnknownVehicle_IsRejected()
    {
        var simulation = GameSimulation.Create(SimulationTestScenarios.Tiny());
        CommandProcessed? processed = null;
        simulation.CommandProcessed += p => processed = p;

        simulation.Submit(new RemoveVehicleCommand(123_456));
        simulation.Tick();

        Assert.Equal("vehicle-not-found", processed!.Value.Result.ReasonCode);
    }

    [Fact]
    public void Commands_AreProcessedInSubmissionOrder()
    {
        var simulation = GameSimulation.Create(SimulationTestScenarios.Tiny());
        var order = new List<long>();
        simulation.CommandProcessed += p => order.Add(p.SubmissionId);

        var first = simulation.Submit(new NoopCommand());
        var second = simulation.Submit(new AddVehicleCommand(0, Speed));
        var third = simulation.Submit(new NoopCommand());
        simulation.Tick();

        Assert.Equal([first, second, third], order);
    }

    [Fact]
    public void RemoveAfterAddInTheSameTick_SeesTheNewVehicle()
    {
        var simulation = GameSimulation.Create(SimulationTestScenarios.Tiny());
        var nextId = simulation.Vehicles.LastCreatedVehicleId + 1;
        var results = new List<bool>();
        simulation.CommandProcessed += p => results.Add(p.Result.Succeeded);

        simulation.Submit(new AddVehicleCommand(0, Speed));
        simulation.Submit(new RemoveVehicleCommand(nextId));
        simulation.Tick();

        Assert.Equal([true, true], results);
        Assert.False(simulation.Vehicles.ContainsVehicle(nextId));
    }

    [Fact]
    public void CommandsSubmittedWhilePaused_ApplyOnTheFirstTickAfterResuming()
    {
        var simulation = GameSimulation.Create(SimulationTestScenarios.Tiny());
        var runner = new SimulationRunner();
        var time = new FakeTimeSource();
        var before = simulation.Vehicles.VehicleCount;
        long? appliedAt = null;
        simulation.CommandProcessed += p => appliedAt = p.Tick;

        runner.TogglePause();
        simulation.Submit(new AddVehicleCommand(0, Speed));
        runner.Advance(1.0, simulation, time);
        Assert.Equal(before, simulation.Vehicles.VehicleCount);
        Assert.Equal(0, simulation.CurrentTick); // Game did not advance to apply it.

        runner.TogglePause();
        runner.Advance(0.05, simulation, time);
        Assert.Equal(before + 1, simulation.Vehicles.VehicleCount);
        Assert.Equal(0, appliedAt);
    }
}

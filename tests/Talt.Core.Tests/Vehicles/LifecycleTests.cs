using System.Text.Json;
using Talt.Core.Commands;
using Talt.Core.Construction;
using Talt.Core.Content;
using Talt.Core.Finance;
using Talt.Core.Roads;
using Talt.Core.Vehicles;

namespace Talt.Core.Tests.Vehicles;

public sealed class LifecycleTests
{
    [Fact]
    public void SendToDepot_ThenSell_RefundsTheValue_AndStopsRunningCosts()
    {
        var kit = VehicleKit.Flat();
        var (west, east, depot) = kit.Corridor();
        var bus = kit.Buy(depot);
        kit.Line([west, east], bus);
        kit.Days(10);
        Assert.Equal(ConstructionReasons.NotInDepot, kit.Run(new SellVehicleCommand(Company.PlayerId, bus)).ReasonCode);

        Assert.True(kit.Run(new SendToDepotCommand(Company.PlayerId, bus)).Succeeded);
        for (var day = 0; day < 40 && kit.Vehicles.DepotAt(kit.Slot(bus)) < 0; day++)
        {
            kit.Days(1);
        }

        var slot = kit.Slot(bus);
        Assert.Equal(depot, kit.Vehicles.DepotAt(slot));
        Assert.Equal(RoadVehicleStatus.InDepot, kit.Vehicles.StatusAt(slot));
        var value = kit.Vehicles.ValueAt(slot);
        var before = kit.State.Player.Balance;
        Assert.True(kit.Run(new SellVehicleCommand(Company.PlayerId, bus)).Succeeded);
        Assert.Equal(before + value, kit.State.Player.Balance);
        Assert.Equal(0, kit.Vehicles.Count);
        Assert.Equal(0, kit.Simulation.Finance!.RunningCosts.ActiveCount);
    }

    [Fact]
    public void Clone_BuysTheSameModel_WithTheSameLine()
    {
        var kit = VehicleKit.Flat();
        var (west, east, depot) = kit.Corridor();
        var bus = kit.Buy(depot, "bus-voltline");
        var line = kit.Line([west, east], bus);
        Assert.True(kit.Run(new CloneVehicleCommand(Company.PlayerId, bus)).Succeeded, kit.Last.ReasonCode);
        var copy = kit.Slot(kit.Vehicles.LastCreatedId);
        Assert.Equal("bus-voltline", kit.Vehicles.ModelAt(copy).Id);
        Assert.Equal(line, kit.Vehicles.LineAt(copy));
        kit.Days(20);
        Assert.Equal(2, kit.Vehicles.Count);
    }

    [Fact]
    public void TruckCargo_IsChosenFromItsCategory()
    {
        var kit = VehicleKit.Flat();
        var (_, _, depot) = kit.Corridor();
        Assert.Equal(ConstructionReasons.CargoNotAllowed, kit.Run(new BuyVehicleCommand(Company.PlayerId, depot, "truck-tanker", "coal")).ReasonCode);
        var truck = kit.Buy(depot, "truck-tipper", "grain");
        Assert.Equal(kit.Simulation.Content.CargoIndex("grain"), kit.Vehicles.CargoAt(kit.Slot(truck)));
        Assert.True(kit.Run(new SetVehicleCargoCommand(Company.PlayerId, truck, "coal")).Succeeded);
        Assert.Equal(kit.Simulation.Content.CargoIndex("coal"), kit.Vehicles.CargoAt(kit.Slot(truck)));
    }

    [Fact]
    public void ReliabilityFalls_BreakdownsHappen_AndServicingRestoresIt()
    {
        var kit = VehicleKit.Flat();
        var (west, east, depot) = kit.Corridor();
        var bus = kit.Buy(depot);
        kit.Run(new SetServiceIntervalCommand(Company.PlayerId, bus, 0));
        kit.Line([west, east], bus);
        var broke = false;
        for (var day = 0; day < 1500 && !broke; day++)
        {
            kit.Days(1);
            broke = kit.Vehicles.StateAt(kit.Slot(bus)).Has(RoadVehicleBits.BrokenDown);
        }

        Assert.True(broke, "an unserviced bus should break down within four years");
        var slot = kit.Slot(bus);
        Assert.True(kit.Vehicles.ReliabilityAt(slot) < kit.Vehicles.ModelAt(slot).Reliability);
        Assert.Equal(VehicleTargetKind.Depot, kit.Vehicles.TargetKindAt(slot));

        // It crawls to the depot and is repaired and serviced there.
        for (var day = 0; day < 30 && kit.Vehicles.StateAt(kit.Slot(bus)).Has(RoadVehicleBits.BrokenDown); day++)
        {
            kit.Days(1);
        }

        slot = kit.Slot(bus);
        Assert.False(kit.Vehicles.StateAt(slot).Has(RoadVehicleBits.BrokenDown));
    }

    [Fact]
    public void ServiceDue_SendsTheVehicleToADepot()
    {
        var kit = VehicleKit.Flat();
        var (west, east, depot) = kit.Corridor();
        var bus = kit.Buy(depot);
        kit.Run(new SetServiceIntervalCommand(Company.PlayerId, bus, 20));
        kit.Line([west, east], bus);
        var serviced = false;
        while (kit.Vehicles.Notices.TryDequeue(out _))
        {
        }

        for (var day = 0; day < 120 && !serviced; day++)
        {
            kit.Days(1);
            while (kit.Vehicles.Notices.TryDequeue(out var n))
            {
                serviced |= n.Kind == VehicleNoticeKind.Serviced && n.VehicleId == bus;
            }
        }

        Assert.True(serviced);
        Assert.True(kit.Vehicles.DaysSinceServiceAt(kit.Slot(bus)) < 100);
    }

    [Fact]
    public void RemovingTheRoadUnderAVehicle_IsRefused()
    {
        var kit = VehicleKit.Flat();
        var (west, east, depot) = kit.Corridor();
        var bus = kit.Buy(depot);
        kit.Line([west, east], bus);
        kit.Days(3);
        var lane = kit.Vehicles.StateAt(kit.Slot(bus)).Lane;
        Assert.True(lane >= 0);
        var tile = lane >> 3;
        var x = tile % kit.State.Map.Width;
        var z = tile / kit.State.Map.Width;
        var result = kit.Run(new RemoveRoadCommand(Company.PlayerId, x, z, x, z));
        Assert.Equal(ConstructionReasons.VehicleInTheWay, result.ReasonCode);
    }

    [Fact]
    public void WarmLaneGeometry_RespectsOneWayEditAndRestoredExit()
    {
        var kit = VehicleKit.Flat();
        var (west, east, depot) = kit.Corridor();
        var slot = kit.Slot(kit.Buy(depot));
        kit.Line([west, east], kit.Vehicles.IdAt(slot));
        var tile = kit.State.Map.Index(18, 20);
        var westbound = (tile * 8) + 6;
        bool DrivesWest() => kit.Vehicles.StateAt(slot).Lane == westbound;
        bool AtEastStop() => kit.Vehicles.StatusAt(slot) == RoadVehicleStatus.Loading && kit.Vehicles.TargetIdAt(slot) == east;
        bool Observe(Func<bool> condition)
        {
            for (var i = 0; i < 4000; i++)
            {
                kit.Ticks(1);
                if (condition())
                {
                    return true;
                }
            }

            return false;
        }

        Assert.True(Observe(DrivesWest));
        Assert.True(Observe(AtEastStop));
        var edit = new SetOneWayCommand(Company.PlayerId, 18, 20);
        Assert.True(kit.Run(edit).Succeeded);
        Assert.False(Observe(DrivesWest));
        Assert.True(kit.Run(edit).Succeeded); // The next setting permits westbound travel.
        Assert.True(Observe(DrivesWest));
    }

    [Fact]
    public void RemovingARoadOnTheRoute_MakesTheVehicleTakeAnotherOne()
    {
        // Two parallel roads between the ends: z = 20 and z = 26, joined at x = 12 and x = 44.
        var kit = VehicleKit.Flat();
        kit.Road(8, 20, 48, 20);
        kit.Road(12, 20, 12, 26);
        kit.Road(44, 20, 44, 26);
        kit.Road(12, 26, 44, 26);
        var west = kit.Stop("bus-bay", 8, 21);
        var east = kit.Stop("bus-bay", 48, 21);
        var depot = kit.Depot(10, 21);
        var bus = kit.Buy(depot);
        kit.Line([west, east], bus);
        kit.Days(2);

        // Cut the northern road in the middle while the bus is far from it.
        Assert.True(kit.Run(new RemoveRoadCommand(Company.PlayerId, 28, 20, 28, 20)).Succeeded, kit.Last.ReasonCode);
        var reachedEast = false;
        for (var day = 0; day < 80 && !reachedEast; day++)
        {
            kit.Days(1);
            var slot = kit.Slot(bus);
            reachedEast = kit.Vehicles.StatusAt(slot) == RoadVehicleStatus.Loading && kit.Vehicles.TargetIdAt(slot) == east;
        }

        Assert.True(reachedEast, "the bus should go round by the southern road");
    }

    [Fact]
    public void FullLoad_WaitsAtTheStop()
    {
        var kit = VehicleKit.Flat();
        var (west, east, depot) = kit.Corridor();
        var bus = kit.Buy(depot);
        var line = kit.Line([west, east], bus);
        Assert.True(kit.Run(new EditLineStopsCommand(Company.PlayerId, line, LineStopEdit.Rule, 0, Rule: StopRule.FullLoad)).Succeeded);
        kit.Days(60);
        var slot = kit.Slot(bus);
        // No passengers on this map: the bus waits for a full load at the west stop forever.
        Assert.Equal(RoadVehicleStatus.Loading, kit.Vehicles.StatusAt(slot));
        Assert.Equal(west, kit.Vehicles.TargetIdAt(slot));
    }

    [Fact]
    public void EveryCommand_SurvivesAJsonRoundTrip()
    {
        ISimulationCommand[] commands =
        [
            new BuildDepotCommand(0, 3, 4), new RemoveDepotCommand(0, 3, 4), new BuyVehicleCommand(0, 2, "truck-box", "mail"),
            new SellVehicleCommand(0, 5), new CloneVehicleCommand(0, 5), new StartVehicleCommand(0, 5, true), new SendToDepotCommand(0, 5),
            new SetVehicleCargoCommand(0, 5, "goods"), new SetServiceIntervalCommand(0, 5, 90), new CreateLineCommand(0, [1, 2, 3], 7),
            new DeleteLineCommand(0, 2), new RenameLineCommand(0, 2, "Harbour run"),
            new SetLineIntervalCommand(0, 2, 3),
            new EditLineStopsCommand(0, 2, LineStopEdit.Move, 1, 4, 0, StopRule.UnloadOnly), new AssignLineCommand(0, 5, -1),
        ];
        foreach (var command in commands)
        {
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
            {
                CommandSerializer.Write(writer, command);
            }

            using var document = JsonDocument.Parse(stream.ToArray());
            Assert.Equal(command, CommandSerializer.Read(document.RootElement));
        }
    }

    [Fact]
    public void Rules_RejectAModelTooLongForTheJunctionSpacing()
    {
        var json = VehicleRules.ReadResource().Replace("\"junctionRadiusMilliTiles\": 220", "\"junctionRadiusMilliTiles\": 320", StringComparison.Ordinal);
        var error = Assert.Throws<InvalidDataException>(() => VehicleRules.FromJson(json, GameContent.Default));
        Assert.Contains("models[bus-townsman].lengthMilliTiles", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DepotMaintenance_IsBookedMonthly()
    {
        var kit = VehicleKit.Flat();
        kit.Corridor();
        kit.Days(62);
        var ledger = kit.State.Player.Ledger!;
        Assert.True(ledger.Month(1, Ledger.Slot(LedgerCategory.PropertyMaintenance)) < 0);
    }
}

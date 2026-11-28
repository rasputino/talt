using System.Numerics;
using System.Reflection;
using Talt.Core.Commands;
using Talt.Core.Content;
using Talt.Core.Construction;
using Talt.Core.Determinism;
using Talt.Core.Industries;
using Talt.Core.Maps;
using Talt.Core.Network;
using Talt.Core.Scenarios;
using Talt.Core.Simulation;

namespace Talt.Core.Tests.Determinism;

/// <summary>
/// Guard for research R1: simulation state must never contain floating-point fields, otherwise
/// results could differ between platforms. Render-only types (VehiclePose, PoseInterpolator) and the
/// runner (real-time bookkeeping, not simulation state) are deliberately not listed.
/// </summary>
public class NoFloatingPointStateTests
{
    private static readonly Type[] SimulationStateTypes =
    [
        typeof(GameSimulation),
        typeof(GameClock),
        typeof(MonthlyHistory<Talt.Core.Towns.TownMonth>),
        typeof(Talt.Core.Stations.StationHistory),
        typeof(Talt.Core.Stations.StationHistory.Snapshot),
        typeof(Talt.Core.Towns.TownMonth),
        typeof(Talt.Core.Stations.StationCargoMonth),
        typeof(TrafficHistory),
        typeof(TrafficHistory.RoadLane),
        typeof(TrafficSegment),
        typeof(GameDate),
        typeof(VehicleFleet),
        typeof(VehicleState),
        typeof(TransportNetwork),
        typeof(GridPoint),
        typeof(DeterministicRandom),
        typeof(ScenarioSettings),
        typeof(CommandInbox),
        typeof(AddVehicleCommand),
        typeof(RemoveVehicleCommand),
        typeof(SimulationEvents),
        typeof(GameContent),
        typeof(CargoDefinition),
        typeof(IndustryKindDefinition),
        typeof(EconomyRules),
        typeof(ProductionEntry),
        typeof(RecipeDefinition),
        typeof(StockDefinition),
        typeof(ModelBox),
        typeof(IndustryFleet),
        typeof(IndustryInfo),
        typeof(IndustryDeliveryResult),
        typeof(Site),
        typeof(MapSettings),
        typeof(ConstructionState),
        typeof(ConstructionRules),
        typeof(Company),
        typeof(TileObjects),
        typeof(TileOwner),
        typeof(UndoRecord),
        typeof(TileSnapshot),
        typeof(MapData),
        typeof(TerraformCommand),
        typeof(ClearAreaCommand),
        typeof(UndoConstructionCommand),
        typeof(Talt.Core.Roads.RoadLayer),
        typeof(Talt.Core.Rails.RailLayer),
        typeof(Talt.Core.Rails.RailPiece),
        typeof(Talt.Core.Rails.RailRules),
        typeof(Talt.Core.Rails.RailType),
        typeof(Talt.Core.Rails.RollingStockModel),
        typeof(Talt.Core.Rails.RailDepotRegistry),
        typeof(Talt.Core.Rails.RailBlocks),
        typeof(Talt.Core.Rails.RailRouter),
        typeof(Talt.Core.Rails.RailRoute),
        typeof(Talt.Core.Rails.RailPlanner),
        typeof(Talt.Core.Rails.Train),
        typeof(Talt.Core.Rails.TrainPart),
        typeof(Talt.Core.Rails.TrainSystem),
        typeof(Talt.Core.Rails.BuildRailCommand),
        typeof(Talt.Core.Rails.BuildRailBridgeCommand),
        typeof(Talt.Core.Rails.BuildRailTunnelCommand),
        typeof(Talt.Core.Rails.RemoveRailCommand),
        typeof(Talt.Core.Rails.ConvertRailCommand),
        typeof(Talt.Core.Rails.SetSignalCommand),
        typeof(Talt.Core.Rails.BuildRailStationCommand),
        typeof(Talt.Core.Rails.BuildRailDepotCommand),
        typeof(Talt.Core.Rails.RemoveRailDepotCommand),
        typeof(Talt.Core.Rails.BuyRailVehicleCommand),
        typeof(Talt.Core.Rails.SellRailVehicleCommand),
        typeof(Talt.Core.Rails.StartTrainCommand),
        typeof(Talt.Core.Rails.SendTrainToDepotCommand),
        typeof(Talt.Core.Rails.AssignTrainLineCommand),
        typeof(Talt.Core.Rails.SetTrainCargoCommand),
        typeof(Talt.Core.Rails.SetTrainServiceIntervalCommand),
        typeof(Talt.Core.Rails.CloneTrainCommand),
        typeof(Talt.Core.Roads.RoadPiece),
        typeof(Talt.Core.Roads.RoadRules),
        typeof(Talt.Core.Roads.RoadType),
        typeof(Talt.Core.Roads.BridgeEra),
        typeof(Talt.Core.Roads.BuildRoadCommand),
        typeof(Talt.Core.Roads.BuildBridgeCommand),
        typeof(Talt.Core.Roads.BuildTunnelCommand),
        typeof(Talt.Core.Roads.RemoveRoadCommand),
        typeof(Talt.Core.Roads.SetOneWayCommand),
        typeof(Talt.Core.Roads.BuildMotorwayCommand),
        typeof(Talt.Core.Roads.BuildMotorwayConnectorCommand),
        typeof(RoadNetwork),
        typeof(RoadRouter),
        typeof(RouteStep),
        typeof(ConstructionPlanner),
        typeof(ConstructionPlan),
        typeof(TileChange),
        typeof(Talt.Core.Towns.TownRegistry),
        typeof(Talt.Core.Towns.TownBuildings),
        typeof(Talt.Core.Towns.TownRules),
        typeof(Talt.Core.Towns.TownGrowthRules),
        typeof(Talt.Core.Towns.TownRatingRules),
        typeof(Talt.Core.Towns.TownSizeBand),
        typeof(Talt.Core.Towns.BuildingType),
        typeof(Talt.Core.Towns.BuildingModel),
        typeof(Talt.Core.Towns.TownSettings),
        typeof(Talt.Core.Towns.TownInfo),
        typeof(Talt.Core.Towns.TownNames),
        typeof(Talt.Core.Towns.TownNameStyleParts),
        typeof(Talt.Core.Stations.StationRegistry),
        typeof(Talt.Core.Stations.StationRules),
        typeof(Talt.Core.Stations.StationPieceType),
        typeof(Talt.Core.Stations.WarehouseType),
        typeof(Talt.Core.Stations.StationRatingRules),
        typeof(Talt.Core.Stations.StationSettings),
        typeof(Talt.Core.Stations.StationPiece),
        typeof(Talt.Core.Stations.CargoHold),
        typeof(Talt.Core.Stations.HeldCargo),
        typeof(Talt.Core.Stations.CargoDelivery),
        typeof(Talt.Core.Stations.UnloadResult),
        typeof(Talt.Core.Stations.StationInfo),
        typeof(Talt.Core.Stations.StationCargoInfo),
        typeof(Talt.Core.Stations.CatchmentPreview),
        typeof(Talt.Core.Stations.BuildStationCommand),
        typeof(Talt.Core.Stations.BuildWarehouseCommand),
        typeof(Talt.Core.Stations.RemoveStationCommand),
        typeof(Talt.Core.Stations.RenameStationCommand),
        typeof(Talt.Core.Finance.FinanceSystem),
        typeof(Talt.Core.Finance.FinanceRules),
        typeof(Talt.Core.Finance.FinanceSettings),
        typeof(Talt.Core.Finance.CargoPayment),
        typeof(Talt.Core.Finance.Ledger),
        typeof(Talt.Core.Finance.RunningCosts),
        typeof(Talt.Core.Finance.PaymentQuote),
        typeof(Talt.Core.Finance.CompanyFinance),
        typeof(Talt.Core.Finance.MoneyEvent),
        typeof(Talt.Core.Finance.MoneyEventFeed),
        typeof(Talt.Core.Finance.ChangeLoanCommand),
        typeof(Talt.Core.Finance.EnableInfiniteMoneyCommand),
        typeof(Talt.Core.Vehicles.RoadVehicleSystem),
        typeof(Talt.Core.Vehicles.RoadVehicleState),
        typeof(Talt.Core.Vehicles.RoadVehicleSystem.LaneGeometry),
        typeof(Talt.Core.Vehicles.LaneCache),
        typeof(Talt.Core.Vehicles.VehicleRules),
        typeof(Talt.Core.Vehicles.VehicleModel),
        typeof(Talt.Core.Vehicles.DepotRegistry),
        typeof(Talt.Core.Vehicles.LineRegistry),
        typeof(Talt.Core.Vehicles.LineYearStatistics),
        typeof(Talt.Core.Vehicles.SetLineIntervalCommand),
        typeof(Talt.Core.Vehicles.LineStop),
        typeof(Talt.Core.Vehicles.VehicleNotice),
        typeof(Talt.Core.Vehicles.VehicleNoticeFeed),
        typeof(Talt.Core.Vehicles.BuildDepotCommand),
        typeof(Talt.Core.Vehicles.RemoveDepotCommand),
        typeof(Talt.Core.Vehicles.BuyVehicleCommand),
        typeof(Talt.Core.Vehicles.SellVehicleCommand),
        typeof(Talt.Core.Vehicles.CloneVehicleCommand),
        typeof(Talt.Core.Vehicles.StartVehicleCommand),
        typeof(Talt.Core.Vehicles.SendToDepotCommand),
        typeof(Talt.Core.Vehicles.SetVehicleCargoCommand),
        typeof(Talt.Core.Vehicles.SetServiceIntervalCommand),
        typeof(Talt.Core.Vehicles.CreateLineCommand),
        typeof(Talt.Core.Vehicles.DeleteLineCommand),
        typeof(Talt.Core.Vehicles.RenameLineCommand),
        typeof(Talt.Core.Vehicles.EditLineStopsCommand),
        typeof(Talt.Core.Vehicles.AssignLineCommand),
    ];

    private static readonly Type[] Forbidden =
    [
        typeof(float), typeof(double), typeof(Half), typeof(decimal),
        typeof(Vector2), typeof(Vector3), typeof(Vector4),
    ];

    [Fact]
    public void SimulationState_HasNoFloatingPointFields()
    {
        var offenders = new List<string>();
        foreach (var type in SimulationStateTypes)
        {
            foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                var fieldType = field.FieldType.IsArray ? field.FieldType.GetElementType()! : field.FieldType;
                if (Forbidden.Contains(fieldType))
                {
                    offenders.Add($"{type.Name}.{field.Name} ({field.FieldType.Name})");
                }
            }
        }

        Assert.True(offenders.Count == 0, "Floating-point simulation state found: " + string.Join(", ", offenders));
    }
}

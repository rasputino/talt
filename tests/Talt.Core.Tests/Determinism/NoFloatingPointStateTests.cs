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

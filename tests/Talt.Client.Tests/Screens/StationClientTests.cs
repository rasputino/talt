using System.Numerics;
using Talt.Client.Construction;
using Talt.Client.Localization;
using Talt.Client.Picking;
using Talt.Client.Rendering;
using Talt.Client.Screens;
using Talt.Client.Ui.Widgets;
using Talt.Core.Construction;
using Talt.Core.Stations;
using Talt.Core.Voxels;

namespace Talt.Client.Tests.Screens;

/// <summary>Feature 010 client parts: station tools, catchment preview, placeholders, picking and the window.</summary>
public class StationClientTests
{
    private static readonly Lazy<(GameSession Session, int StationId)> Game = new(() =>
    {
        var session = new GameSession(
            GameSetup.Create(new NewGameOptions(ChunksPerSide: 8, VehicleCount: 0, Seed: 7, SeedStations: 4)),
            new SessionOptions(),
            fastForwardUnlocked: true);
        session.Simulation.ProcessPendingCommands();
        var stations = session.Simulation.Stations!;
        Assert.True(stations.Count > 0);
        return (session, stations.IdAt(0));
    });

    [Fact]
    public void StationTools_PlanLikeTheirCommands()
    {
        var (session, _) = Game.Value;
        var state = session.Construction;
        var candidate = FindBayTile(state);
        foreach (var tool in new[] { ConstructionTool.BusStop, ConstructionTool.TruckStop })
        {
            var preview = new ConstructionPlan();
            tool.Plan(new ConstructionPlanner(), state, state.Player.Id, candidate, candidate, preview);
            var command = tool.Command(state, state.Player.Id, candidate, candidate);
            var planned = new ConstructionPlan();
            var result = command.Preview(state, new ConstructionPlanner(), planned);
            Assert.True(result.Succeeded, result.ReasonCode);
            Assert.Equal(preview.Cost, planned.Cost);
            Assert.Equal(preview.Changes.ToArray(), planned.Changes.ToArray());
            Assert.IsType<BuildStationCommand>(command);
        }

        Assert.True(ConstructionTool.All.Count(t => t.IsStation) == 4);
    }

    [Fact]
    public void EveryToolAndOptionButton_HasAnIcon()
    {
        // Regression (hand test 2026-11-25): the station icons were outside the "icons" object and the HUD crashed.
        var icons = Talt.Client.Tests.Ui.UiTestKit.Icons;
        foreach (var tool in ConstructionTool.All)
        {
            Assert.True(icons.Contains(tool.Id), tool.Id);
        }

        Assert.True(icons.Contains("warehouseSize"));
        Assert.True(icons.Contains("roadType"));
    }

    [Fact]
    public void WarehouseSizeOption_CyclesAndTravelsInTheCommand()
    {
        var (session, _) = Game.Value;
        var rules = session.Simulation.Stations!.Rules;
        StationToolOptions.Reset();
        Assert.Equal("small", StationToolOptions.SizeId(rules));
        StationToolOptions.Cycle(rules);
        var command = Assert.IsType<BuildWarehouseCommand>(ConstructionTool.Warehouse.Command(session.Construction, 0, (5, 5), (5, 5)));
        Assert.Equal("medium", command.Size);
        StationToolOptions.Reset();
    }

    [Fact]
    public void CatchmentPreview_CoversTheRadiusAndListsCargo()
    {
        var (session, _) = Game.Value;
        var state = session.Construction;
        var stations = session.Simulation.Stations!;
        var (x, z) = FindBayTile(state);
        var preview = new CatchmentPreview();
        stations.PreviewCatchment(state.Player.Id, warehouse: false, stations.Rules.FindPiece("bus-bay"), x, z, preview);
        Assert.True(preview.Tiles.Count >= 4 * 4);
        Assert.True(preview.Tiles.Count <= 7 * 7 + 200); // 7 × 7 alone, more when it joins a station.
        foreach (var language in Strings.SupportedLanguages)
        {
            var s = Strings.Load(language);
            Assert.DoesNotContain("[", StationWindow.CargoList(s, session.Simulation.Content, preview.Receives | preview.Accepts), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Placeholders_HaveBoxesForEveryPiece()
    {
        var (session, _) = Game.Value;
        var stations = session.Simulation.Stations!;
        var boxes = new IndustryInstance[StationPlaceholders.BoxCount(stations)];
        var written = StationPlaceholders.Build(stations, session.Construction, session.Game.Map, boxes, 0);
        Assert.InRange(written, stations.PieceCount, boxes.Length);
        Assert.All(boxes.Take(written), b => Assert.True(b.Extent.X > 0 && b.Extent.Y > 0 && b.Extent.Z > 0));
    }

    [Fact]
    public void RayFromAbove_PicksTheStation()
    {
        var (session, id) = Game.Value;
        var stations = session.Simulation.Stations!;
        var tile = stations.TilesAt(stations.SlotOf(id))[0];
        var map = session.Game.Map;
        var x = tile % map.Width;
        var z = tile / map.Width;
        var context = new PickContext { Heightmap = session.Heightmap, World = session.World, Stations = stations, Roads = session.Construction.Roads };
        var ray = new Ray(new Vector3(x + 0.5f, map.HeightAt(x, z) + 30f, z + 0.5f), -Vector3.UnitY);

        var result = new MapPicker().Pick(ray, context);

        Assert.Equal(PickKind.Station, result.Kind);
        Assert.Equal(id, result.StationId);
        Assert.Equal($"station:{id}", result.Key);
    }

    [Fact]
    public void StationWindow_ShowsNameAndFigures_InBothLanguages()
    {
        var (session, id) = Game.Value;
        var stations = session.Simulation.Stations!;
        var pick = new PickResult(PickKind.Station, 0, 0, 0, VoxelMaterial.Air, -1, -1, default, default, 1, StationId: id);
        var window = ObjectInfoWindow.Create(pick, session);
        Assert.Equal($"station:{id}", window.Key);
        Assert.IsType<ScrollArea>(window.Content);
        foreach (var language in Strings.SupportedLanguages)
        {
            var s = Strings.Load(language);
            var name = StationWindow.Name(s, stations, session.Simulation.Towns, id);
            Assert.DoesNotContain("[", name, StringComparison.Ordinal);
            var (town, _, _, _) = stations.NameAt(stations.SlotOf(id));
            if (town >= 0)
            {
                Assert.Contains(session.Simulation.Towns!.NameOf(town), name, StringComparison.Ordinal);
            }

            Assert.DoesNotContain("[", StationWindow.Kind(s, stations, id), StringComparison.Ordinal);
            Assert.DoesNotContain("[", StationWindow.Group(s, stations, id), StringComparison.Ordinal);
            Assert.DoesNotContain("[", StationWindow.CargoTable(s, stations, id), StringComparison.Ordinal);
        }

        session.ToggleCatchment(id);
        Assert.Equal(id, session.CatchmentStationId);
        session.ToggleCatchment(id);
        Assert.Equal(-1, session.CatchmentStationId);
    }

    private static (int X, int Z) FindBayTile(ConstructionState state)
    {
        var plan = new ConstructionPlan();
        var bay = state.Stations!.Rules.FindPiece("bus-bay");
        for (var z = 0; z < state.Map.Depth; z++)
        {
            for (var x = 0; x < state.Map.Width; x++)
            {
                if (state.Stations.JoinTarget(state.Player.Id, x, z) != -1)
                {
                    continue;
                }

                StationPlanner.PlanPiece(state, state.Player.Id, bay, x, z, plan);
                if (plan.IsValid)
                {
                    return (x, z);
                }
            }
        }

        throw new InvalidOperationException("No free bay tile.");
    }
}

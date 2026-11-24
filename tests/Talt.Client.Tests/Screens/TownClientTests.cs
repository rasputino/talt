using System.Numerics;
using Talt.Client.Localization;
using Talt.Client.Picking;
using Talt.Client.Rendering;
using Talt.Client.Screens;
using Talt.Client.Ui;
using Talt.Client.Ui.Widgets;
using Talt.Core.Construction;
using Talt.Core.Terrain;
using Talt.Core.Towns;
using Talt.Core.Voxels;

namespace Talt.Client.Tests.Screens;

/// <summary>Feature 008 client parts: building boxes, labels, picking and the town window.</summary>
public class TownClientTests
{
    private static readonly Lazy<GameSession> Session = new(() =>
        new GameSession(GameSetup.Create(new NewGameOptions(ChunksPerSide: 8, VehicleCount: 0, Seed: 7)), new SessionOptions(), fastForwardUnlocked: true));

    [Fact]
    public void Buildings_BecomeWallsAndRoofs_InsideTheirFootprint()
    {
        var rules = TownRules.Default;
        foreach (var type in rules.Buildings)
        {
            var walls = new List<TownInstance>();
            var roofs = new List<TownInstance>();
            var top = TownInstances.Append(type, 10, 20, 5, 1, walls, roofs);

            var wall = Assert.Single(walls);
            Assert.NotEmpty(roofs);
            Assert.Equal(5f, wall.Position.Y);
            Assert.Equal(type.Height * TownInstances.StoreyHeight, wall.Extent.Y, 3);
            Assert.True(top > 5f + wall.Extent.Y - 0.001f);
            foreach (var box in walls.Concat(roofs))
            {
                // Every box stays inside the footprint (plus the roof overhang).
                Assert.True(box.Position.X - (box.Extent.X / 2f) >= 10f - 0.01f, type.Id);
                Assert.True(box.Position.X + (box.Extent.X / 2f) <= 10f + type.Width + 0.01f, type.Id);
                Assert.True(box.Position.Z - (box.Extent.Z / 2f) >= 20f - 0.01f, type.Id);
                Assert.True(box.Position.Z + (box.Extent.Z / 2f) <= 20f + type.Depth + 0.01f, type.Id);
            }
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5_000)]
    [InlineData(150_000)]
    [InlineData(int.MaxValue)]
    public void WallColours_StayInRange_ForAnyBuildingId(int id)
    {
        // Regression (hand test 2026-11-24): large ids overflowed the variation and walls turned white.
        var type = TownRules.Default.Buildings[0];
        var walls = new List<TownInstance>();
        TownInstances.Append(type, 0, 0, 0, id, walls, []);
        var wall = Assert.Single(walls);
        Assert.InRange(wall.Color.W, 0f, 1f);
        Assert.InRange(wall.Color.X, 0f, 1.1f);
        Assert.InRange(wall.Color.Y, 0f, 1.1f);
        Assert.InRange(wall.Color.Z, 0f, 1.1f);
    }

    [Fact]
    public void Labels_ProjectPointsAndDetectOverlaps()
    {
        var view = Matrix4x4.CreateLookAt(new Vector3(0, 50, 50), Vector3.Zero, Vector3.UnitY);
        var projection = Matrix4x4.CreatePerspectiveFieldOfView(1f, 16f / 9f, 1f, 1000f);
        var viewport = new Vector2(1600, 900);

        Assert.True(TownLabels.TryProject(view * projection, viewport, Vector3.Zero, out var centre));
        Assert.Equal(800f, centre.X, 1);
        Assert.Equal(450f, centre.Y, 1);
        Assert.False(TownLabels.TryProject(view * projection, viewport, new Vector3(0, 50, 200), out _)); // Behind the camera.

        Assert.True(TownLabels.Overlaps(new RectF(0, 0, 10, 10), new RectF(5, 5, 10, 10)));
        Assert.False(TownLabels.Overlaps(new RectF(0, 0, 10, 10), new RectF(10, 0, 10, 10)));
    }

    [Fact]
    public void RayFromAbove_PicksATownBuilding()
    {
        var session = Session.Value;
        var towns = session.Simulation.Towns!;
        var slot = 0;
        var x = towns.Buildings.XAt(slot);
        var z = towns.Buildings.ZAt(slot);
        var context = new PickContext { Heightmap = session.Heightmap, World = session.World, Towns = towns };
        var ground = towns.Buildings.BaseHeightAt(slot);
        var ray = new Ray(new Vector3(x + 0.5f, ground + 40f, z + 0.5f), -Vector3.UnitY);

        Assert.True(new TownPickLayer().TryPick(ray, context, out var result));
        Assert.Equal(PickKind.Town, result.Kind);
        Assert.Equal(towns.Buildings.TownAt(slot), result.TownId);
        Assert.Equal($"town:{result.TownId}", result.Key);
        Assert.True(result.SameObject(result));

        // A slanted ray from far away along the ground still finds it.
        var target = new Vector3(x + 0.5f, ground + 0.5f, z + 0.5f);
        var origin = target + new Vector3(-60f, 45f, -60f);
        Assert.True(new TownPickLayer().TryPick(new Ray(origin, Vector3.Normalize(target - origin)), context, out var slanted));
        Assert.Equal(PickKind.Town, slanted.Kind);
    }

    [Fact]
    public void TownWindow_ShowsTheTownsFigures_InBothLanguages()
    {
        var session = Session.Value;
        var towns = session.Simulation.Towns!;
        var pick = new PickResult(PickKind.Town, 0, 0, 0, VoxelMaterial.Air, -1, -1, default, default, 1, TownId: 0);
        var window = ObjectInfoWindow.Create(pick, session);

        Assert.Equal("town:0", window.Key);
        var panel = Assert.IsType<StackPanel>(window.Content);
        Assert.Equal(7, panel.ChildCount);
        foreach (var language in Strings.SupportedLanguages)
        {
            var s = Strings.Load(language);
            Assert.Contains(towns.PopulationOf(0).ToString("N0", s.Culture), TownWindow.Population(s, towns, 0), StringComparison.Ordinal);
            Assert.DoesNotContain("[", TownWindow.Buildings(s, towns, 0), StringComparison.Ordinal);
            Assert.DoesNotContain("[", TownWindow.Passengers(s, towns, 0), StringComparison.Ordinal);
            Assert.DoesNotContain("[", TownWindow.Mail(s, towns, 0), StringComparison.Ordinal);
            Assert.DoesNotContain("[", TownWindow.Accepts(s, towns, 0), StringComparison.Ordinal);
            Assert.Contains("500", TownWindow.Rating(s, towns, 0, Company.PlayerId), StringComparison.Ordinal);
            Assert.DoesNotContain("[", TownWindow.Growth(s, towns, 0), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void NewGameOptions_CarryTheTownChoices()
    {
        var source = new DefaultNewGameOptionsSource();
        var values = source.Options.ToDictionary(o => o.Id, o => o.Default);
        values[DefaultNewGameOptionsSource.TownNames] = (int)TownNameStyle.Spanish;
        values[DefaultNewGameOptionsSource.TownSize] = (int)TownSize.Large;

        var options = source.Create(values);

        Assert.Equal(new TownSettings(TownNameStyle.Spanish, TownSize.Large), options.Towns);
        Assert.Equal(options.Towns, options.ToScenario().Towns);
    }
}

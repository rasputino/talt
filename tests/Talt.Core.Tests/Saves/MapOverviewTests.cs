using Talt.Core.Maps;
using Talt.Core.Saves;
using Talt.Core.Scenarios;
using Talt.Core.Simulation;

namespace Talt.Core.Tests.Saves;

/// <summary>US3: the save thumbnail is a deterministic top-down overview of the map (research R14).</summary>
public class MapOverviewTests
{
    [Fact]
    public void Overview_shows_terrain_water_roads_rail_and_towns()
    {
        var game = SaveTestGames.Rich(ticks: 0);
        var thumbnail = MapOverview.Render(game)!;
        Assert.Equal(MapOverview.Size, thumbnail.Width);
        Assert.Equal(MapOverview.Size * MapOverview.Size * 3, thumbnail.Pixels.Length);
        var colors = OverviewColors.Default;
        var seen = new HashSet<(byte, byte, byte)>();
        for (var i = 0; i < thumbnail.Pixels.Length; i += 3)
        {
            seen.Add((thumbnail.Pixels[i], thumbnail.Pixels[i + 1], thumbnail.Pixels[i + 2]));
        }

        Assert.Contains(colors.Road, seen);
        Assert.Contains(colors.Rail, seen);
        Assert.Contains(colors.Town, seen);
        Assert.Contains(colors.Water, seen);
    }

    [Fact]
    public void Overview_is_deterministic_and_reuses_its_buffer()
    {
        var game = SaveTestGames.Rich(ticks: 0);
        var first = MapOverview.Render(game)!;
        var second = MapOverview.Render(game, reuse: new byte[first.Pixels.Length])!;
        Assert.Equal(first.Pixels, second.Pixels);
    }

    [Fact]
    public void Non_square_maps_are_letterboxed_and_map_less_games_have_none()
    {
        var wide = GameSimulation.Create(new ScenarioSettings(Seed: 3, MapSizeTiles: 128, VehicleCount: 0, Map: new MapSettings(256, 128, Seed: 3)));
        var thumbnail = MapOverview.Render(wide)!;
        var background = OverviewColors.Default.Background;
        Assert.Equal(background, (thumbnail.Pixels[0], thumbnail.Pixels[1], thumbnail.Pixels[2])); // Top band.
        var middle = ((MapOverview.Size / 2 * MapOverview.Size) + (MapOverview.Size / 2)) * 3;
        Assert.NotEqual(background, (thumbnail.Pixels[middle], thumbnail.Pixels[middle + 1], thumbnail.Pixels[middle + 2]));

        Assert.Null(MapOverview.Render(GameSimulation.Create(new ScenarioSettings(MapSizeTiles: 64, VehicleCount: 10))));
    }

    [Fact]
    public void Colors_file_is_validated()
    {
        Assert.Throws<InvalidDataException>(() => OverviewColors.FromJson("{\"background\": [1, 2]}"));
        Assert.Throws<InvalidDataException>(() => OverviewColors.FromJson("not json"));
    }
}

using Talt.Core.Content;
using Talt.Core.Determinism;
using Talt.Core.Industries;
using Talt.Core.Maps;
using Talt.Core.Simulation;

namespace Talt.Core.Tests.Industries;

/// <summary>Small deterministic fixtures for industry tests: flat maps, sites and inline content.</summary>
internal static class IndustryTestData
{
    public const string RawCargo = """
        {
          "id": "test-cargo",
          "nameKey": "cargo.test",
          "category": "raw",
          "unitKey": "unit.tonnes",
          "destinationRequired": false,
          "valueDecayPerDayPerMille": 1,
          "minimumValuePercent": 10,
          "color": "#FFFFFF",
          "vehicleKinds": ["road"]
        }
        """;

    public const string MinimalIndustry = """
        {
          "id": "test-industry",
          "nameKey": "industry.test",
          "footprint": [1, 1],
          "siteKinds": ["plain"],
          "era": [1800, 9999],
          "weight": 1,
          "model": [{ "x": 0, "y": 0, "z": 0, "width": 1, "depth": 1, "height": 1, "color": "#FFFFFF" }]
        }
        """;

    public static string CargoFile(string cargo) => $$"""{"cargo":[{{cargo}}]}""";

    public static string IndustryFile(string industries, string economy = "{}") =>
        $$"""{"economy":{{economy}},"industries":[{{industries}}]}""";

    /// <summary>A flat, fully buildable map with the given reserved sites.</summary>
    public static MapData Map(int width, int depth, params Site[] sites)
    {
        var size = width * depth;
        var height = new byte[size];
        var terrain = new byte[size];
        var water = new byte[size];
        var flags = new byte[size];
        var scenery = new byte[size];
        Array.Fill(flags, (byte)TileTraits.Buildable);
        Array.Fill(height, (byte)8);
        return new MapData(
            new MapSettings(width, depth, 1),
            height,
            terrain,
            water,
            flags,
            scenery,
            sites,
            new MapStatistics(0, 0, 0, 0));
    }

    /// <summary>Sites spaced <paramref name="spacing"/> tiles apart, in reading order.</summary>
    public static Site[] Sites(int count, SiteKind kind, int spacing = 8, int width = 512, int depth = 512)
    {
        var result = new Site[count];
        var perRow = Math.Max(1, width / spacing);
        for (var i = 0; i < count; i++)
        {
            var x = spacing / 2 + ((i % perRow) * spacing);
            var z = spacing / 2 + ((i / perRow) * spacing);
            if (z >= depth)
            {
                throw new ArgumentOutOfRangeException(nameof(count), "Too many sites for the map.");
            }

            result[i] = new Site(x, z, kind, 2, 128);
        }

        return result;
    }

    /// <summary>The calendar date that follows the given day, as <see cref="GameDate"/>.</summary>
    public static GameDate After(GameDate date) => new(date.Year, date.Month, date.Day, date.DayNumber);

    /// <summary>Date of the day after <paramref name="day"/>; the parameter AdvanceDay expects.</summary>
    public static GameDate DayAfter(DateOnly day)
    {
        var next = day.AddDays(1);
        return new GameDate(next.Year, next.Month, next.Day, next.DayNumber);
    }

    public static GameDate FirstDay(int year, int month) =>
        new GameDate(year, month, 1, new DateOnly(year, month, 1).DayNumber);

    public static GameContent Content(string cargoFile, string industryFile) =>
        GameContent.FromJson(cargoFile, industryFile, "test-cargo.json", "test-industries.json");

    /// <summary>A fleet on a flat map with the given number of sites, fully occupied by default.</summary>
    public static IndustryFleet Fleet(
        GameContent content,
        int siteCount = 1,
        SiteKind kind = SiteKind.IndustryPlain,
        int? occupancyPerMille = 1000,
        int seed = 1,
        int width = 256)
    {
        var sites = Sites(siteCount, kind, spacing: 8, width: width, depth: width);
        var map = Map(width, width, sites);
        return IndustryFleet.Create(content, map, 1950, new DeterministicRandom((ulong)seed), occupancyPerMille);
    }
}

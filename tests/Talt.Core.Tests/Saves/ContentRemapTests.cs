using System.Text.Json.Nodes;
using Talt.Core.Content;
using Talt.Core.Saves;

namespace Talt.Core.Tests.Saves;

public class ContentRemapTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Saved_industry_ids_survive_catalog_reordering_or_report_a_missing_id(bool remove)
    {
        var game = SaveTestGames.Rich(ticks: 0);
        var json = JsonNode.Parse(Resource("industries"))!;
        var kinds = json["industries"]!.AsArray();
        var first = kinds[0]!;
        var id = first["id"]!.GetValue<string>();
        kinds.RemoveAt(0);
        if (!remove)
        {
            kinds.Add(first);
        }

        var content = GameContent.FromJson(Resource("cargo"), json.ToJsonString());
        var result = SaveGame.Load(new MemoryStream(RoundTrip.SaveBytes(game)), new SaveLoadOptions(content));
        if (remove)
        {
            Assert.Equal(SaveLoadStatus.MissingContent, result.Status);
            Assert.Contains($"IndustryKind:{id}", result.MissingContent);
            Assert.Null(result.Simulation);
            return;
        }

        Assert.Equal(SaveLoadStatus.LoadedWithChangedData, result.Status);
        for (var slot = 0; slot < game.Industries.Count; slot++)
        {
            Assert.Equal(game.Industries.KindIdAt(slot), result.Simulation!.Industries.KindIdAt(slot));
        }
    }

    [Fact]
    public void Reordered_cargo_keeps_vehicle_loads_and_ledger_columns()
    {
        var game = SaveTestGames.Rich();
        var json = JsonNode.Parse(Resource("cargo"))!;
        var cargo = json["cargo"]!.AsArray();
        var first = cargo[0]!;
        cargo.RemoveAt(0);
        cargo.Add(first);
        var content = GameContent.FromJson(json.ToJsonString(), Resource("industries"));
        var result = SaveGame.Load(new MemoryStream(RoundTrip.SaveBytes(game)), new SaveLoadOptions(content));
        Assert.True(result.Status == SaveLoadStatus.LoadedWithChangedData, result.Problem);
        var restored = result.Simulation!;
        var originalFleet = game.RoadVehicles!;
        for (var slot = 0; slot < originalFleet.Count; slot++)
        {
            Assert.Equal(game.Content.Cargo(originalFleet.CargoAt(slot)).Id, content.Cargo(restored.RoadVehicles!.CargoAt(slot)).Id);
        }

        for (var index = 0; index < content.CargoCount; index++)
        {
            var previous = game.Content.CargoIndex(content.Cargo(index).Id);
            Assert.Equal(game.Construction!.Player.Ledger!.Month(0, global::Talt.Core.Finance.Ledger.IncomeSlot(previous)),
                restored.Construction!.Player.Ledger!.Month(0, global::Talt.Core.Finance.Ledger.IncomeSlot(index)));
        }

        for (var i = 0; i < 400; i++)
        {
            restored.Tick();
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Model_indices_resolve_through_saved_road_and_rail_id_tables(bool missing)
    {
        var original = SaveTestGames.Rich(ticks: 0);
        using var file = SaveFile.Read(new MemoryStream(RoundTrip.SaveBytes(original)));
        var section = file.Sections.Single(s => s.Id == SaveSections.Content);
        var reader = new SaveReader(section.Payload, section.Id);
        using var replacement = new SaveBuffer();
        using var writer = new SaveWriter(replacement);
        var count = reader.ReadInt();
        writer.WriteInt(count);
        var road = original.RoadVehicles!;
        var rail = original.Trains!;
        var usedRoad = road.Rules.FindModel(road.ModelAt(0).Id);
        var usedRail = rail.TrainAt(0).Parts[0].Model;
        for (var t = 0; t < count; t++)
        {
            var tableSize = reader.ReadInt();
            var ids = new string[tableSize];
            for (var i = 0; i < tableSize; i++)
            {
                ids[i] = reader.ReadRequiredString();
            }

            var used = (ContentTable)t switch
            {
                ContentTable.RoadVehicleModel => usedRoad,
                ContentTable.RollingStockModel => usedRail,
                _ => -1,
            };
            if (used >= 0)
            {
                var other = (used + 1) % ids.Length;
                (ids[used], ids[other]) = (ids[other], ids[used]);
                if (missing)
                {
                    ids[used] = "removed-model";
                }
            }

            writer.WriteInt(tableSize);
            foreach (var id in ids)
            {
                writer.WriteString(id);
            }
        }

        writer.WriteArray(reader.ReadRequiredArray<ulong>());
        using var stream = new MemoryStream();
        SaveFile.Write(stream, file.DetailsJson, file.Thumbnail,
            file.Sections.Select(s => s == section ? s with { Payload = replacement } : s).ToArray());
        stream.Position = 0;
        var result = SaveGame.Load(stream);
        if (missing)
        {
            Assert.Equal(SaveLoadStatus.MissingContent, result.Status);
            Assert.Contains("RollingStockModel:removed-model", result.MissingContent);
            Assert.Contains("RoadVehicleModel:removed-model", result.MissingContent);
            return;
        }

        Assert.True(result.Status == SaveLoadStatus.LoadedWithChangedData, result.Problem);
        Assert.Equal(road.Rules.Models[(usedRoad + 1) % road.Rules.Models.Count].Id, result.Simulation!.RoadVehicles!.ModelAt(0).Id);
        Assert.Equal((usedRail + 1) % rail.Rules.Models.Count, result.Simulation.Trains!.TrainAt(0).Parts[0].Model);
    }

    private static string Resource(string name)
    {
        using var stream = typeof(GameContent).Assembly.GetManifestResourceStream($"content/{name}.json")!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}

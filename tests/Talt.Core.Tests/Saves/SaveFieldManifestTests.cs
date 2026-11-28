using System.Reflection;
using System.Text;
using Talt.Core.Commands;
using Talt.Core.Construction;
using Talt.Core.Demand;
using Talt.Core.Finance;
using Talt.Core.Industries;
using Talt.Core.Maps;
using Talt.Core.Network;
using Talt.Core.Rails;
using Talt.Core.Roads;
using Talt.Core.Simulation;
using Talt.Core.Stations;
using Talt.Core.Towns;
using Talt.Core.Vehicles;

namespace Talt.Core.Tests.Saves;

/// <summary>
/// The save field manifest (research R12, contracts/save-sections.md rule 2): every instance field of every type whose state
/// a save holds is listed in <c>SaveFieldManifest.txt</c> as <c>saved</c>, <c>derived</c>, <c>scratch</c>, <c>ref</c> or
/// <c>ui</c>. Adding a field without deciding how it is saved fails here, so later features (016, 017…) cannot forget state.
/// Set the environment variable <c>TALT_WRITE_SAVE_MANIFEST=1</c> to regenerate the file: new fields are written as
/// <c>unclassified</c> and still fail until they are classified by hand.
/// </summary>
public class SaveFieldManifestTests
{
    private static readonly string[] Classes = ["saved", "derived", "scratch", "ref", "ui"];

    /// <summary>Types whose fields the save must account for (the owners of every section).</summary>
    internal static readonly Type[] SavedTypes =
    [
        typeof(GameSimulation), typeof(CommandInbox), typeof(VehicleFleet), typeof(TransportNetwork),
        typeof(MapData), typeof(ConstructionState), typeof(Company), typeof(ReplacementPolicy), typeof(TileObjects), typeof(UndoRecord), typeof(Ledger),
        typeof(RoadLayer), typeof(RoadNetwork), Nested(typeof(RoadNetwork), "Region"), typeof(RoadRouter),
        typeof(RailLayer), typeof(RailBlocks), typeof(RailDepotRegistry),
        typeof(TownRegistry), typeof(TownBuildings),
        typeof(IndustryFleet),
        typeof(StationRegistry), typeof(CargoHold),
        typeof(FinanceSystem), typeof(RunningCosts),
        typeof(RoadVehicleSystem), typeof(DepotRegistry), typeof(LineRegistry), Nested(typeof(LineRegistry), "Line"), Type("Talt.Core.Vehicles.LaneIndexMap"),
        typeof(TrainSystem), typeof(Train), typeof(TrainPart), typeof(RailRoute),
        typeof(DestinationTables), Nested(typeof(DestinationTables), "RuleTables"), typeof(CargoRouter), typeof(RouteTable), typeof(RoutingProblem), typeof(LinkCollector),
    ];

    private static Type Nested(Type outer, string name) => outer.GetNestedType(name, BindingFlags.NonPublic | BindingFlags.Public)
        ?? throw new InvalidOperationException($"{outer.Name}.{name} not found");

    private static Type Type(string name) => typeof(GameSimulation).Assembly.GetType(name, throwOnError: true)!;

    /// <summary>"Type.field" for every instance field (auto-property backing fields under the property's name).</summary>
    internal static SortedSet<string> ActualFields()
    {
        var fields = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var type in SavedTypes)
        {
            foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                fields.Add($"{TypeName(type)}.{FieldName(field)}");
            }
        }

        return fields;
    }

    private static string TypeName(Type type) => type.DeclaringType is null ? type.Name : $"{type.DeclaringType.Name}+{type.Name}";

    private static string FieldName(FieldInfo field)
    {
        var name = field.Name;
        if (name.StartsWith('<'))
        {
            var end = name.IndexOf('>', StringComparison.Ordinal);
            return name[1..end];
        }

        return name;
    }

    private static string ManifestPath => Path.Combine(AppContext.BaseDirectory, "Saves", "SaveFieldManifest.txt");

    private static Dictionary<string, (string Class, string Line)> ReadManifest(string path)
    {
        var entries = new Dictionary<string, (string, string)>(StringComparer.Ordinal);
        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var equals = line.IndexOf('=', StringComparison.Ordinal);
            var key = line[..equals].Trim();
            var rest = line[(equals + 1)..];
            var hash = rest.IndexOf('#', StringComparison.Ordinal);
            var cls = (hash >= 0 ? rest[..hash] : rest).Trim();
            entries[key] = (cls, raw);
        }

        return entries;
    }

    [Fact]
    public void Every_field_of_every_saved_type_is_classified()
    {
        var actual = ActualFields();
        if (Environment.GetEnvironmentVariable("TALT_WRITE_SAVE_MANIFEST") == "1")
        {
            Regenerate(actual);
        }

        var manifest = ReadManifest(ManifestPath);
        var missing = actual.Where(f => !manifest.ContainsKey(f)).ToList();
        var stale = manifest.Keys.Where(k => !actual.Contains(k)).ToList();
        var bad = manifest.Where(e => actual.Contains(e.Key) && !Classes.Contains(e.Value.Class)).Select(e => $"{e.Key} = {e.Value.Class}").ToList();
        Assert.True(missing.Count == 0, $"Fields not in SaveFieldManifest.txt (decide how each is saved): {string.Join(", ", missing)}");
        Assert.True(stale.Count == 0, $"Manifest entries for fields that no longer exist: {string.Join(", ", stale)}");
        Assert.True(bad.Count == 0, $"Unclassified manifest entries: {string.Join(", ", bad)}");
    }

    private static void Regenerate(SortedSet<string> actual)
    {
        var source = FindSourceManifest();
        var existing = File.Exists(source) ? ReadManifest(source) : [];
        var text = new StringBuilder();
        text.AppendLine("# Save field manifest (feature 014, research R12). One line per instance field of every saved type:");
        text.AppendLine("#   saved   - written to and read from the save verbatim");
        text.AppendLine("#   derived - rebuilt on load from saved data or settings (the reason says how)");
        text.AppendLine("#   scratch - overwritten before every read; carries nothing between ticks");
        text.AppendLine("#   ref     - wiring to another system, content, rules or immutable configuration");
        text.AppendLine("#   ui      - interface state (feeds for windows), not simulation state; starts empty after loading");
        text.AppendLine("# Regenerate with TALT_WRITE_SAVE_MANIFEST=1 (existing classifications are kept).");
        foreach (var field in actual)
        {
            text.AppendLine(existing.TryGetValue(field, out var entry) ? entry.Line.Trim() : $"{field} = unclassified");
        }

        File.WriteAllText(source, text.ToString());
        File.WriteAllText(ManifestPath, text.ToString());
    }

    private static string FindSourceManifest()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Talt.Core.Tests.csproj")))
        {
            dir = dir.Parent;
        }

        return Path.Combine(dir?.FullName ?? throw new InvalidOperationException("test project folder not found"), "Saves", "SaveFieldManifest.txt");
    }
}

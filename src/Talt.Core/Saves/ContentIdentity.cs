using Talt.Core.Simulation;

namespace Talt.Core.Saves;

/// <summary>Content tables that simulation state refers to by index (research R10).</summary>
public enum ContentTable
{
    Cargo = 0,
    IndustryKind = 1,
    BuildingType = 2,
    StationPiece = 3,
    Warehouse = 4,
    RoadType = 5,
    RailType = 6,
    RoadVehicleModel = 7,
    RollingStockModel = 8,
}

/// <summary>
/// Saved content index → installed content index per <see cref="ContentTable"/> (data-model.md ContentMap). Built
/// from the <c>CNTN</c> section; <see cref="Identity"/> when every table matches the installed data, which lets
/// readers skip remapping.
/// </summary>
public sealed class ContentMap
{
    private readonly int[]?[] _maps;
    private readonly int[] _installedCounts;

    private ContentMap(int[]?[] maps, int[] installedCounts, bool identity)
    {
        _maps = maps;
        _installedCounts = installedCounts;
        IsIdentity = identity;
    }

    /// <summary>Every saved index is the installed index.</summary>
    public static ContentMap Identity { get; } = new(new int[]?[TableCount], [], identity: true);

    /// <summary>True when no table needs remapping.</summary>
    public bool IsIdentity { get; }

    internal const int TableCount = 9;

    /// <summary>The installed index of saved index <paramref name="savedIndex"/> in <paramref name="table"/> (negative values pass through: "none").</summary>
    public int Map(ContentTable table, int savedIndex)
    {
        var map = _maps[(int)table];
        if (map is null || savedIndex < 0)
        {
            return savedIndex;
        }

        if (savedIndex >= map.Length)
        {
            throw new SaveFormatException($"Content index {savedIndex} of {table} is outside the saved table ({map.Length} entries).");
        }

        return map[savedIndex];
    }

    /// <summary>True when <paramref name="table"/> keeps every index.</summary>
    public bool IsIdentityFor(ContentTable table) => _maps[(int)table] is null;

    /// <summary>Remaps every element of <paramref name="indices"/> in place (no-op for an identity table).</summary>
    public void MapInPlace(ContentTable table, Span<int> indices)
    {
        if (IsIdentityFor(table))
        {
            return;
        }

        for (var i = 0; i < indices.Length; i++)
        {
            indices[i] = Map(table, indices[i]);
        }
    }

    internal int SavedCount(ContentTable table, int installedCount) => _maps[(int)table]?.Length ?? installedCount;

    internal T[] Rows<T>(ContentTable table, T[] values, T empty = default) where T : unmanaged
    {
        if (_maps[(int)table] is not { } map || values.Length == 0)
        {
            return values;
        }

        if (map.Length == 0)
        {
            throw new SaveFormatException($"Unexpected data for empty table {table}.");
        }

        var width = _installedCounts[(int)table];
        var rows = (values.Length + map.Length - 1) / map.Length;
        var result = new T[checked(rows * width)];
        Array.Fill(result, empty);
        for (var i = 0; i < values.Length; i++)
        {
            result[(i / map.Length * width) + map[i % map.Length]] = values[i];
        }

        return result;
    }

    internal byte PieceType(ContentTable table, byte stored) => stored == 0 ? (byte)0 : checked((byte)(Map(table, stored - 1) + 1));

    internal void RemapPieces(ContentTable table, byte[] types)
    {
        if (IsIdentityFor(table))
        {
            return;
        }

        for (var i = 0; i < types.Length; i++)
        {
            types[i] = PieceType(table, types[i]);
        }
    }

    internal void CargoMasks(ulong[] masks)
    {
        if (_maps[(int)ContentTable.Cargo] is not { } map)
        {
            return;
        }

        for (var i = 0; i < masks.Length; i++)
        {
            var previous = masks[i];
            ulong mask = 0;
            for (var c = 0; c < map.Length; c++)
            {
                if ((previous & (1UL << c)) != 0)
                {
                    mask |= 1UL << map[c];
                }
            }

            masks[i] = mask;
        }
    }

    internal void CargoKeys<T>(Dictionary<long, T> values, int shift)
    {
        if (IsIdentityFor(ContentTable.Cargo))
        {
            return;
        }

        var entries = values.ToArray();
        values.Clear();
        foreach (var (key, value) in entries)
        {
            var cargo = Map(ContentTable.Cargo, (int)((key >> shift) & 255));
            values.Add((key & ~(255L << shift)) | ((long)cargo << shift), value);
        }
    }

    internal static ContentMap Create(int[]?[] maps, int[] installedCounts)
    {
        var identity = true;
        foreach (var map in maps)
        {
            identity &= map is null;
        }

        return identity ? Identity : new ContentMap(maps, installedCounts, identity: false);
    }
}

/// <summary>
/// The <c>CNTN</c> section (research R10): the stable ids of every content table in installed order, and the
/// fingerprints of the content and rules files. On load it yields the <see cref="ContentMap"/>, the ids missing
/// from the installed data and whether any data file changed.
/// </summary>
internal static class ContentIdentity
{
    public static void Write(GameSimulation simulation, SaveWriter writer)
    {
        // Content and rules never change during a game: the lists are built once per game (capture stays allocation-free).
        var cache = simulation.SaveCache ??= new SaveCaptureCache(GameSimulation.SettingsToJson(simulation.Settings), simulation.Settings.Map?.ToText(), InstalledIds(simulation), Fingerprints(simulation));
        var tables = cache.ContentIds;
        writer.WriteInt(tables.Length);
        for (var t = 0; t < tables.Length; t++)
        {
            var ids = tables[t];
            writer.WriteInt(ids.Length);
            for (var i = 0; i < ids.Length; i++)
            {
                writer.WriteString(ids[i]);
            }
        }

        writer.WriteSpan<ulong>(cache.Fingerprints);
    }

    internal static List<string> MissingBasicContent(Content.GameContent content, SaveReader reader)
    {
        reader.ReadInt(ContentMap.TableCount, ContentMap.TableCount);
        var missing = new List<string>();
        for (var t = 0; t <= (int)ContentTable.IndustryKind; t++)
        {
            var count = reader.ReadCount(4);
            for (var i = 0; i < count; i++)
            {
                var id = reader.ReadRequiredString();
                if ((t == (int)ContentTable.Cargo ? content.CargoIndex(id) : content.IndustryKindIndex(id)) < 0)
                {
                    missing.Add($"{(ContentTable)t}:{id}");
                }
            }
        }

        return missing;
    }

    /// <summary>Reads the section and compares it with the installed data.</summary>
    public static (ContentMap Map, IReadOnlyList<string> Missing, bool Changed) Read(GameSimulation simulation, SaveReader reader)
    {
        var installed = InstalledIds(simulation);
        var tableCount = reader.ReadInt(0, 64);
        var maps = new int[]?[ContentMap.TableCount];
        var missing = new List<string>();
        var changed = false;
        for (var t = 0; t < tableCount; t++)
        {
            var count = reader.ReadCount(4);
            var saved = new string[count];
            for (var i = 0; i < count; i++)
            {
                saved[i] = reader.ReadRequiredString();
            }

            if (t >= installed.Length)
            {
                continue; // A table this version does not know: refused earlier as a newer section version.
            }

            var current = installed[t];
            var map = new int[count];
            var identity = count == current.Length;
            for (var i = 0; i < count; i++)
            {
                var index = IndexOf(current, saved[i]);
                if (index < 0)
                {
                    missing.Add($"{(ContentTable)t}:{saved[i]}");
                }

                map[i] = index;
                identity &= index == i;
            }

            changed |= count != current.Length || !identity;
            maps[t] = identity ? null : map;
        }

        var savedFingerprints = reader.ReadRequiredArray<ulong>();
        var installedFingerprints = Fingerprints(simulation);
        changed |= !savedFingerprints.AsSpan().SequenceEqual(installedFingerprints);
        return (ContentMap.Create(maps, installed.Select(ids => ids.Length).ToArray()), missing, changed);
    }


    private static int IndexOf(string[] ids, string id)
    {
        for (var i = 0; i < ids.Length; i++)
        {
            if (string.Equals(ids[i], id, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>Id lists in <see cref="ContentTable"/> order; empty lists for systems the game does not have.</summary>
    internal static string[][] InstalledIds(GameSimulation simulation)
    {
        var content = simulation.Content;
        var construction = simulation.Construction;
        var tables = new string[ContentMap.TableCount][];
        tables[(int)ContentTable.Cargo] = [.. content.AllCargo.Select(c => c.Id)];
        tables[(int)ContentTable.IndustryKind] = [.. content.AllIndustryKinds.Select(k => k.Id)];
        tables[(int)ContentTable.BuildingType] = simulation.Towns is { } towns ? [.. towns.Buildings.Rules.Buildings.Select(b => b.Id)] : [];
        tables[(int)ContentTable.StationPiece] = simulation.Stations is { } stations ? [.. stations.Rules.Pieces.Select(p => p.Id)] : [];
        tables[(int)ContentTable.Warehouse] = simulation.Stations is { } s ? [.. s.Rules.Warehouses.Select(w => w.Id)] : [];
        tables[(int)ContentTable.RoadType] = construction is null ? [] : [.. construction.RoadRules.Types.Select(r => r.Id)];
        tables[(int)ContentTable.RailType] = construction is null ? [] : [.. construction.RailRules.Types.Select(r => r.Id)];
        tables[(int)ContentTable.RoadVehicleModel] = simulation.Trains is { } trains ? [.. trains.VehicleRules.Models.Select(m => m.Id)] : [];
        tables[(int)ContentTable.RollingStockModel] = simulation.Trains is { } t ? [.. t.Rules.Models.Select(m => m.Id)] : [];
        return tables;
    }

    private static ulong DataHash<T>(T rules) => System.IO.Hashing.XxHash3.HashToUInt64(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(rules));

    /// <summary>Content and rules fingerprints: a change means the game may play differently (FR-018).</summary>
    internal static ulong[] Fingerprints(GameSimulation simulation) =>
    [
        simulation.Content.Fingerprint(),
        simulation.Towns?.Rules.Fingerprint ?? 0,
        simulation.Stations?.Rules.Fingerprint ?? 0,
        simulation.Finance?.Rules.Fingerprint ?? 0,
        simulation.Trains?.VehicleRules.Fingerprint ?? 0,
        simulation.Destinations?.Rules.Fingerprint ?? 0,
        simulation.Construction is { } state ? DataHash(state.Rules) : 0,
        simulation.Construction is { } roads ? DataHash(roads.RoadRules) : 0,
        simulation.Trains is { } trains ? DataHash(trains.Rules) : 0,
        simulation.Towns is { } towns ? DataHash(towns.Buildings.Rules.Buildings) : 0,
    ];
}

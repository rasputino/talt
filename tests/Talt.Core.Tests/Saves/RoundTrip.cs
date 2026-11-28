using Talt.Core.Saves;
using Talt.Core.Simulation;

namespace Talt.Core.Tests.Saves;

/// <summary>
/// The proof of identical continuation (research R13): save → load → same fingerprint → byte-identical re-save of every
/// section → identical fingerprints and sections after further ticks.
/// </summary>
internal static class RoundTrip
{
    public static readonly SaveDetailsInput Details = new("Test save", SaveKind.Manual, new DateTimeOffset(2026, 11, 28, 12, 0, 0, TimeSpan.Zero), "test");

    public static byte[] SaveBytes(GameSimulation simulation)
    {
        using var stream = new MemoryStream();
        SaveGame.Save(simulation, stream, Details);
        return stream.ToArray();
    }

    public static GameSimulation Load(byte[] bytes)
    {
        var result = SaveGame.Load(new MemoryStream(bytes));
        Assert.True(result.Succeeded, $"{result.Status}: {result.Problem}");
        return result.Simulation!;
    }

    /// <summary>Saves and loads, checking the fingerprint at load and a byte-identical re-save.</summary>
    public static GameSimulation SaveAndLoad(GameSimulation simulation)
    {
        var fingerprint = simulation.ComputeFingerprint();
        var loaded = Load(SaveBytes(simulation));
        Assert.Equal(fingerprint, loaded.ComputeFingerprint());
        Assert.Equal(simulation.CurrentTick, loaded.CurrentTick);
        AssertSameSections(simulation, loaded);
        return loaded;
    }

    /// <summary>Asserts both games capture to the same bytes, section by section (covers state the fingerprint misses).</summary>
    public static void AssertSameSections(GameSimulation expected, GameSimulation actual)
    {
        using var a = new SaveSnapshot();
        using var b = new SaveSnapshot();
        SaveGame.CaptureSections(a, expected);
        SaveGame.CaptureSections(b, actual);
        Assert.Equal(a.Sections.Select(s => s.Id.ToString()), b.Sections.Select(s => s.Id.ToString()));
        for (var s = 0; s < a.Sections.Count; s++)
        {
            var left = Flatten(a.Sections[s].Payload);
            var right = Flatten(b.Sections[s].Payload);
            var at = left.AsSpan().CommonPrefixLength(right);
            if (at != left.Length || at != right.Length)
            {
                var id = a.Sections[s].Id.ToString();
                var field = StateDiff.FirstDifference(SystemOf(expected, id), SystemOf(actual, id), id);
                Assert.Fail($"Section {id} differs at byte {at} (lengths {left.Length} and {right.Length}); first different field: {field ?? "none found"}.");
            }
        }
    }

    /// <summary>Runs both games <paramref name="ticks"/> ticks, comparing fingerprints every <paramref name="every"/> ticks and sections at the end.</summary>
    public static void AssertContinuesIdentically(GameSimulation original, GameSimulation loaded, int ticks, int every = 100, bool parallel = false, Action<GameSimulation>? commands = null)
    {
        for (var t = 1; t <= ticks; t++)
        {
            commands?.Invoke(original);
            commands?.Invoke(loaded);
            original.Tick(parallel);
            loaded.Tick(parallel);
            if (t % every == 0 || t == ticks)
            {
                Assert.True(original.ComputeFingerprint() == loaded.ComputeFingerprint(), $"Fingerprints diverged by tick {original.CurrentTick} ({t} ticks after loading).");
            }
        }

        AssertSameSections(original, loaded);
    }

    private static object? SystemOf(GameSimulation simulation, string id) => id switch
    {
        "CORE" => simulation.Vehicles,
        "MAP_" => simulation.Construction?.Map,
        "CNST" => simulation.Construction?.Companies,
        "ROAD" => simulation.Construction?.Network,
        "RAIL" => simulation.Construction?.Rails,
        "TOWN" => simulation.Towns,
        "INDY" => simulation.Industries,
        "STNS" => simulation.Stations,
        "FINA" => simulation.Finance,
        "RVEH" => simulation.RoadVehicles,
        "TRAI" => simulation.Trains,
        "DMND" => simulation.Router,
        _ => null,
    };

    private static byte[] Flatten(SaveBuffer buffer)
    {
        var bytes = new byte[buffer.Length];
        var at = 0;
        for (var i = 0; i < buffer.SegmentCount; i++)
        {
            buffer.Segment(i).CopyTo(bytes.AsSpan(at));
            at += buffer.SegmentLength(i);
        }

        return bytes;
    }
}

using Talt.Core.Maps;

namespace Talt.Core.Tests.Maps;

public class MapDeterminismTests
{
    [Theory]
    [MemberData(nameof(MapTestSettings.MatrixData), MemberType = typeof(MapTestSettings))]
    public void SameSettings_GiveTheSameMap(MapSettings settings)
    {
        var a = MapGenerator.Generate(settings);
        var b = MapGenerator.Generate(settings);
        Assert.Equal(a.Fingerprint(), b.Fingerprint());
        Assert.True(a.Heights.SequenceEqual(b.Heights));
        Assert.Equal(a.Sites, b.Sites);
    }

    [Theory]
    [MemberData(nameof(MapTestSettings.MatrixData), MemberType = typeof(MapTestSettings))]
    public void ThreadCount_NeverChangesTheMap(MapSettings settings)
    {
        var single = MapGenerator.Generate(settings, maxDegreeOfParallelism: 1).Fingerprint();
        var three = MapGenerator.Generate(settings, maxDegreeOfParallelism: 3).Fingerprint();
        var all = MapGenerator.Generate(settings).Fingerprint();
        Assert.Equal(single, three);
        Assert.Equal(single, all);
    }

    [Fact]
    public void DifferentSeeds_GiveSubstantiallyDifferentMaps()
    {
        var a = MapTestSettings.Generate(new MapSettings(256, 256, 1));
        var b = MapTestSettings.Generate(new MapSettings(256, 256, 2));
        Assert.NotEqual(a.Fingerprint(), b.Fingerprint());

        var different = 0;
        for (var i = 0; i < a.Heights.Length; i++)
        {
            if (a.Heights[i] != b.Heights[i])
            {
                different++;
            }
        }

        Assert.True(different > a.Heights.Length * 3 / 10, $"Only {different} tiles differ.");
    }

    [Fact]
    public void EveryTunedSetting_ChangesTheMap()
    {
        var baseline = MapTestSettings.Generate(new MapSettings(256, 256, 4)).Fingerprint();
        var variants = new[]
        {
            new MapSettings(256, 256, 4, Roughness: 51),
            new MapSettings(256, 256, 4, MaxHeight: 29),
            new MapSettings(256, 256, 4, WaterAmount: 26),
            new MapSettings(256, 256, 4, SeaLevel: 8),
            new MapSettings(288, 256, 4),
        };
        Assert.All(variants, v => Assert.NotEqual(baseline, MapTestSettings.Generate(v).Fingerprint()));
    }

    /// <summary>
    /// Floating point in the generator is limited to operations that are bit-exact on every platform
    /// (research R1). Transcendental functions are not, so they must not appear in the generator.
    /// </summary>
    [Fact]
    public void Generator_DoesNotUsePlatformDependentMath()
    {
        var mapsFolder = FindMapsFolder();
        var forbidden = new[] { "Math.Pow", "MathF.Pow", "Math.Sin", "MathF.Sin", "Math.Cos", "MathF.Cos", "Math.Exp", "MathF.Exp", "Math.Log", "MathF.Log", "Math.Tan", "MathF.Tan", "Math.Atan", "MathF.Atan", "Math.Sqrt", "Math.FusedMultiplyAdd", "MathF.FusedMultiplyAdd" };
        foreach (var file in Directory.EnumerateFiles(mapsFolder, "*.cs", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(file);
            foreach (var word in forbidden)
            {
                Assert.DoesNotContain(word, text, StringComparison.Ordinal);
            }
        }
    }

    private static string FindMapsFolder()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "src", "Talt.Core", "Maps");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not find src/Talt.Core/Maps above the test output folder.");
    }
}

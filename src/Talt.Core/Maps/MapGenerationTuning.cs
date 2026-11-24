namespace Talt.Core.Maps;

/// <summary>
/// Thresholds and densities of the generator, kept in one place so they can be tuned without
/// hunting for constants (constitution VII). Changing a default alters generated maps, so it
/// requires bumping <see cref="MapSettings.GeneratorVersion"/>.
/// </summary>
public sealed record MapGenerationTuning
{
    public static MapGenerationTuning Default { get; } = new();

    /// <summary>Width in tiles of the bands that parallel stages process (fixed, so results never depend on core count).</summary>
    public int BandRows { get; init; } = 16;

    /// <summary>Fraction (percent) of the land height range above which rock may form cliffs.</summary>
    public int RockLinePercent { get; init; } = 55;

    /// <summary>Fraction (percent) of the land height range at which snow starts.</summary>
    public int SnowLinePercent { get; init; } = 72;

    /// <summary>Snow never starts closer than this many voxels above the sea.</summary>
    public int MinSnowAboveSea { get; init; } = 8;

    /// <summary>Largest voxel step between neighbouring land tiles outside cliff zones.</summary>
    public int MaxStep { get; init; } = 1;

    /// <summary>Largest voxel step inside cliff zones.</summary>
    public int CliffStep { get; init; } = 2;

    /// <summary>Depressions shallower than this are levelled into the terrain; deeper ones become lakes.</summary>
    public int LakeMinDepth { get; init; } = 3;

    /// <summary>River threshold: drainage area (tiles) at 25% water on a 4096 x 4096 map is this times the area divisor.</summary>
    public int RiverMinCatchment { get; init; } = 500;

    /// <summary>Map area (tiles) per extra tile of river catchment threshold.</summary>
    public int RiverAreaDivisor { get; init; } = 6000;

    /// <summary>Beach width in tiles.</summary>
    public int BeachWidth { get; init; } = 2;

    /// <summary>Forest noise threshold in thousandths, at 0% water; wetter maps lower it.</summary>
    public int ForestThresholdPerMille { get; init; } = 620;

    /// <summary>Tree chance per mille on forest tiles.</summary>
    public int ForestTreesPerMille { get; init; } = 550;

    /// <summary>Tree chance per mille on open grass.</summary>
    public int OpenTreesPerMille { get; init; } = 30;

    /// <summary>Land tiles per town site (sets the spacing between towns).</summary>
    public int TownAreaPerSite { get; init; } = 25_000;

    /// <summary>Land tiles per industry site (denser than towns: only land near a town may host one).</summary>
    public int IndustryAreaPerSite { get; init; } = 12_000;

    /// <summary>Smallest allowed distance between two towns.</summary>
    public int MinTownDistance { get; init; } = 48;

    /// <summary>Smallest allowed distance between two industries.</summary>
    public int MinIndustryDistance { get; init; } = 24;

    /// <summary>
    /// Smallest allowed distance between a town and an industry. It leaves room for a town to grow to the
    /// clearance population of <c>Towns/Data/towns.json</c> without reaching the industry (a test checks it).
    /// </summary>
    public int MinTownIndustryDistance { get; init; } = 60;

    /// <summary>Largest allowed distance between an industry and the nearest town, the town it belongs to.</summary>
    public int MaxTownIndustryDistance { get; init; } = 120;

    /// <summary>Land components smaller than this host no industry sites.</summary>
    public int MinIndustryComponentArea { get; init; } = 2_000;

    public int TownFreeRadius { get; init; } = 4;

    public int IndustryFreeRadius { get; init; } = 2;

    /// <summary>Largest height difference inside the free square of a town.</summary>
    public int TownMaxRelief { get; init; } = 2;

    /// <summary>Largest height difference inside the free square of an industry.</summary>
    public int IndustryMaxRelief { get; init; } = 3;

    /// <summary>Throws <see cref="ArgumentException"/> when the town–industry distances are inconsistent.</summary>
    public void Validate()
    {
        if (MinTownIndustryDistance < 0)
        {
            throw new ArgumentException($"{nameof(MinTownIndustryDistance)} must not be negative (is {MinTownIndustryDistance}).");
        }

        if (MaxTownIndustryDistance <= MinTownIndustryDistance)
        {
            throw new ArgumentException(
                $"{nameof(MaxTownIndustryDistance)} ({MaxTownIndustryDistance}) must be greater than {nameof(MinTownIndustryDistance)} ({MinTownIndustryDistance}).");
        }
    }
}

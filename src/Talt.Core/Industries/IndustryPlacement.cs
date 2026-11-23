using Talt.Core.Content;
using Talt.Core.Determinism;
using Talt.Core.Maps;

namespace Talt.Core.Industries;

/// <summary>
/// Decides which industry kind may stand on a reserved site and picks one by weight (research R4).
/// The result is a pure function of the content, the site and the random stream.
/// </summary>
internal static class IndustryPlacement
{
    /// <summary>Index of a fitting kind, or -1 when no kind suits the site in this year.</summary>
    public static int ChooseKind(
        GameContent content,
        Site site,
        int year,
        int mapWidth,
        int mapDepth,
        DeterministicRandom random)
    {
        var total = 0;
        for (var i = 0; i < content.IndustryKindCount; i++)
        {
            if (Supports(content.IndustryKind(i), site, year, mapWidth, mapDepth))
            {
                total += content.IndustryKind(i).Weight;
            }
        }

        if (total <= 0)
        {
            return -1;
        }

        var pick = random.NextInt(total);
        for (var i = 0; i < content.IndustryKindCount; i++)
        {
            var kind = content.IndustryKind(i);
            if (!Supports(kind, site, year, mapWidth, mapDepth))
            {
                continue;
            }

            pick -= kind.Weight;
            if (pick < 0)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>True when the kind accepts the site's kind, its era contains the year and it fits.</summary>
    public static bool Supports(IndustryKindDefinition kind, Site site, int year, int mapWidth, int mapDepth)
    {
        if (site.Kind == SiteKind.Town || year < kind.EraStart || year > kind.EraEnd)
        {
            return false;
        }

        var suitable = false;
        for (var i = 0; i < kind.SiteKinds.Count; i++)
        {
            if (kind.SiteKinds[i] == site.Kind)
            {
                suitable = true;
                break;
            }
        }

        return suitable && Fits(kind.FootprintWidth, kind.FootprintDepth, site.X, site.Z, mapWidth, mapDepth);
    }

    /// <summary>Tile bounds of a footprint centred on a site: minimum inclusive, maximum exclusive.</summary>
    public static (int MinX, int MinZ, int MaxX, int MaxZ) Bounds(int x, int z, int width, int depth)
    {
        var minX = x - (width / 2);
        var minZ = z - (depth / 2);
        return (minX, minZ, minX + width, minZ + depth);
    }

    /// <summary>Whether a footprint centred on a tile stays inside the map.</summary>
    public static bool Fits(int width, int depth, int x, int z, int mapWidth, int mapDepth)
    {
        var (minX, minZ, maxX, maxZ) = Bounds(x, z, width, depth);
        return minX >= 0 && minZ >= 0 && maxX <= mapWidth && maxZ <= mapDepth;
    }
}

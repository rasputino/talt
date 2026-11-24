using Talt.Core.Determinism;

namespace Talt.Core.Maps.Stages;

/// <summary>
/// Stage 7: reserves sites for towns and industries (research R10). Land is split into connected
/// components; towns go only on the largest one, industries on any component big enough. Positions
/// come from Poisson-disc sampling (Bridson's algorithm, integer arithmetic only) over spots whose
/// surrounding square is buildable and nearly flat, so spacing is even and nothing depends on the
/// order tiles are visited.
/// </summary>
internal static class SiteSelector
{
    private const int GridCell = 16;
    private const int CandidatesPerPoint = 20;
    private const int SeedAttempts = 400;
    private const int ReseedRounds = 3;
    private const int CoastSearch = 8;

    public static void Run(MapBuilder b)
    {
        var tuning = b.Tuning;
        var components = Label(b, out var sizes, out var largest);
        if (largest < 0)
        {
            b.Sites = [];
            return;
        }

        var townDistance = Spacing(tuning.TownAreaPerSite, tuning.MinTownDistance);
        var industryDistance = Spacing(tuning.IndustryAreaPerSite, tuning.MinIndustryDistance);
        var rules = new SpacingRules(townDistance, industryDistance, tuning.MinTownIndustryDistance, tuning.MaxTownIndustryDistance);
        var grid = new SiteGrid(b.Width, b.Depth, rules);
        var random = new DeterministicRandom(unchecked((ulong)b.Settings.Seed)).Fork(100);

        Sample(
            b,
            grid,
            random,
            SiteKind.Town,
            townDistance,
            tuning.TownFreeRadius,
            tuning.TownMaxRelief,
            (x, z) => components[x + z * b.Width] == largest);

        Sample(
            b,
            grid,
            random,
            SiteKind.IndustryPlain,
            industryDistance,
            tuning.IndustryFreeRadius,
            tuning.IndustryMaxRelief,
            (x, z) => sizes[components[x + z * b.Width]] >= tuning.MinIndustryComponentArea && grid.NearTown(x, z));

        var sites = grid.Sites.Select(site => Classify(b, site)).ToList();
        sites.Sort(static (a, c) =>
        {
            var byKind = a.Kind.CompareTo(c.Kind);
            if (byKind != 0)
            {
                return byKind;
            }

            var byZ = a.Z.CompareTo(c.Z);
            return byZ != 0 ? byZ : a.X.CompareTo(c.X);
        });
        b.Sites = sites;
    }

    /// <summary>Distance between sites that gives about one site per <paramref name="areaPerSite"/> land tiles.</summary>
    private static int Spacing(int areaPerSite, int minimum) =>
        Math.Max(minimum, (int)(MathF.Sqrt(areaPerSite) * 0.8f));

    private static void Sample(
        MapBuilder b,
        SiteGrid grid,
        DeterministicRandom random,
        SiteKind kind,
        int distance,
        int radius,
        int maxRelief,
        Func<int, int, bool> allowed)
    {
        var active = new List<int>();
        var spots = new SpotCheck(b, radius, maxRelief, allowed);

        for (var round = 0; round <= ReseedRounds; round++)
        {
            for (var attempt = 0; attempt < SeedAttempts; attempt++)
            {
                var x = random.NextInt(b.Width);
                var z = random.NextInt(b.Depth);
                if (spots.IsValid(x, z) && !grid.Conflicts(x, z, kind))
                {
                    active.Add(grid.Add(new Site(x, z, kind, radius, 0)));
                    Grow(b, grid, random, kind, distance, spots, active);
                }
            }

            b.CancellationToken.ThrowIfCancellationRequested();
        }
    }

    private static void Grow(MapBuilder b, SiteGrid grid, DeterministicRandom random, SiteKind kind, int distance, SpotCheck spots, List<int> active)
    {
        while (active.Count > 0)
        {
            var slot = random.NextInt(active.Count);
            var origin = grid.Sites[active[slot]];
            var found = false;
            for (var k = 0; k < CandidatesPerPoint && !found; k++)
            {
                // Random offset in the ring between distance and twice the distance (rejection sampling).
                int dx, dz;
                long squared;
                do
                {
                    dx = random.NextInt(-2 * distance, 2 * distance + 1);
                    dz = random.NextInt(-2 * distance, 2 * distance + 1);
                    squared = ((long)dx * dx) + ((long)dz * dz);
                }
                while (squared < (long)distance * distance || squared > 4L * distance * distance);

                var x = origin.X + dx;
                var z = origin.Z + dz;
                if (b.Width > x && x >= 0 && b.Depth > z && z >= 0 && spots.IsValid(x, z) && !grid.Conflicts(x, z, kind))
                {
                    active.Add(grid.Add(new Site(x, z, kind, spots.Radius, 0)));
                    found = true;
                }
            }

            if (!found)
            {
                active[slot] = active[^1];
                active.RemoveAt(active.Count - 1);
            }

            b.CancellationToken.ThrowIfCancellationRequested();
        }
    }

    /// <summary>Connected components of land (4-neighbour). Returns the id of every tile (-1 for water), sizes, and the largest id.</summary>
    internal static int[] Label(MapBuilder b, out List<int> sizes, out int largest)
    {
        var count = b.TileCount;
        var ids = new int[count];
        Array.Fill(ids, -1);
        var queue = new int[count];
        sizes = [];
        largest = -1;
        const byte water = (byte)(TileTraits.Sea | TileTraits.Lake | TileTraits.River);

        for (var start = 0; start < count; start++)
        {
            if (ids[start] != -1 || (b.Flags[start] & water) != 0)
            {
                continue;
            }

            var id = sizes.Count;
            var size = 0;
            var head = 0;
            var tail = 0;
            queue[tail++] = start;
            ids[start] = id;
            while (head < tail)
            {
                var c = queue[head++];
                size++;
                var x = c % b.Width;
                var z = c / b.Width;
                Try(x > 0, c - 1);
                Try(x < b.Width - 1, c + 1);
                Try(z > 0, c - b.Width);
                Try(z < b.Depth - 1, c + b.Width);

                void Try(bool inside, int n)
                {
                    if (inside && ids[n] == -1 && (b.Flags[n] & water) == 0)
                    {
                        ids[n] = id;
                        queue[tail++] = n;
                    }
                }
            }

            sizes.Add(size);
            if (largest < 0 || size > sizes[largest])
            {
                largest = id;
            }

            b.CancellationToken.ThrowIfCancellationRequested();
        }

        return ids;
    }

    private static Site Classify(MapBuilder b, Site site)
    {
        var relief = Relief(b, site.X, site.Z, site.FreeRadius);
        var flatness = Math.Clamp(255 - (relief * 40), 0, 255);
        if (site.Kind == SiteKind.Town)
        {
            return site with { Score = flatness };
        }

        var sea = b.SeaLevel;
        int waterTiles = 0, trees = 0, total = 0;
        for (var z = Math.Max(0, site.Z - CoastSearch); z <= Math.Min(b.Depth - 1, site.Z + CoastSearch); z++)
        {
            for (var x = Math.Max(0, site.X - CoastSearch); x <= Math.Min(b.Width - 1, site.X + CoastSearch); x++)
            {
                var i = x + z * b.Width;
                total++;
                if ((b.Flags[i] & (byte)(TileTraits.Sea | TileTraits.Lake)) != 0)
                {
                    waterTiles++;
                }

                if (b.Scenery[i] != Scenery.None)
                {
                    trees++;
                }
            }
        }

        var height = b.Height[site.X + (site.Z * b.Width)];
        var mid = sea + ((b.Settings.MaxHeight - sea) / 2);
        if (waterTiles > 0)
        {
            return site with { Kind = SiteKind.IndustryCoast, Score = Math.Clamp(flatness - (waterTiles * 255 / total / 4), 0, 255) };
        }

        if (trees * 100 >= total * 25)
        {
            return site with { Kind = SiteKind.IndustryForest, Score = Math.Clamp(trees * 255 / total, 0, 255) };
        }

        if (height >= mid)
        {
            return site with { Kind = SiteKind.IndustryHills, Score = flatness };
        }

        return site with { Kind = SiteKind.IndustryPlain, Score = flatness };
    }

    private static int Relief(MapBuilder b, int cx, int cz, int radius)
    {
        int min = int.MaxValue, max = int.MinValue;
        for (var z = cz - radius; z <= cz + radius; z++)
        {
            for (var x = cx - radius; x <= cx + radius; x++)
            {
                var h = b.Height[x + (z * b.Width)];
                min = Math.Min(min, h);
                max = Math.Max(max, h);
            }
        }

        return max - min;
    }

    /// <summary>Whether a square of tiles around a spot is inside the map, buildable and nearly flat.</summary>
    private sealed class SpotCheck(MapBuilder b, int radius, int maxRelief, Func<int, int, bool> allowed)
    {
        public int Radius => radius;

        public bool IsValid(int cx, int cz)
        {
            if (cx < radius || cz < radius || cx >= b.Width - radius || cz >= b.Depth - radius)
            {
                return false;
            }

            if ((b.Flags[cx + (cz * b.Width)] & (byte)TileTraits.Buildable) == 0 || !allowed(cx, cz))
            {
                return false;
            }

            int min = int.MaxValue, max = int.MinValue;
            for (var z = cz - radius; z <= cz + radius; z++)
            {
                for (var x = cx - radius; x <= cx + radius; x++)
                {
                    var i = x + (z * b.Width);
                    if ((b.Flags[i] & (byte)TileTraits.Buildable) == 0)
                    {
                        return false;
                    }

                    min = Math.Min(min, b.Height[i]);
                    max = Math.Max(max, b.Height[i]);
                }
            }

            return max - min <= maxRelief;
        }
    }

    private readonly record struct SpacingRules(int TownTown, int IndustryIndustry, int TownIndustry, int MaxTownIndustry);

    /// <summary>Sites bucketed in a coarse grid so spacing checks only look at nearby sites.</summary>
    private sealed class SiteGrid
    {
        private readonly int _cellsX;
        private readonly int _cellsZ;
        private readonly int[] _first;
        private readonly List<int> _next = [];
        private readonly SpacingRules _rules;

        public SiteGrid(int width, int depth, SpacingRules rules)
        {
            _cellsX = (width + GridCell - 1) / GridCell;
            _cellsZ = (depth + GridCell - 1) / GridCell;
            _first = new int[_cellsX * _cellsZ];
            Array.Fill(_first, -1);
            _rules = rules;
        }

        public List<Site> Sites { get; } = [];

        public int Add(Site site)
        {
            var index = Sites.Count;
            Sites.Add(site);
            var cell = (site.X / GridCell) + (site.Z / GridCell * _cellsX);
            _next.Add(_first[cell]);
            _first[cell] = index;
            return index;
        }

        public bool Conflicts(int x, int z, SiteKind kind)
        {
            var reach = Math.Max(_rules.TownTown, Math.Max(_rules.IndustryIndustry, _rules.TownIndustry));
            var cells = (reach / GridCell) + 1;
            var cx = x / GridCell;
            var cz = z / GridCell;
            for (var gz = Math.Max(0, cz - cells); gz <= Math.Min(_cellsZ - 1, cz + cells); gz++)
            {
                for (var gx = Math.Max(0, cx - cells); gx <= Math.Min(_cellsX - 1, cx + cells); gx++)
                {
                    for (var s = _first[gx + (gz * _cellsX)]; s >= 0; s = _next[s])
                    {
                        var other = Sites[s];
                        long min = MinDistance(kind, other.Kind);
                        long dx = other.X - x;
                        long dz = other.Z - z;
                        if ((dx * dx) + (dz * dz) < min * min)
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        /// <summary>Whether a town site lies within the largest town–industry distance of a tile.</summary>
        public bool NearTown(int x, int z)
        {
            long max = _rules.MaxTownIndustry;
            var cells = (_rules.MaxTownIndustry / GridCell) + 1;
            var cx = x / GridCell;
            var cz = z / GridCell;
            for (var gz = Math.Max(0, cz - cells); gz <= Math.Min(_cellsZ - 1, cz + cells); gz++)
            {
                for (var gx = Math.Max(0, cx - cells); gx <= Math.Min(_cellsX - 1, cx + cells); gx++)
                {
                    for (var s = _first[gx + (gz * _cellsX)]; s >= 0; s = _next[s])
                    {
                        var other = Sites[s];
                        long dx = other.X - x;
                        long dz = other.Z - z;
                        if (other.Kind == SiteKind.Town && (dx * dx) + (dz * dz) <= max * max)
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        private int MinDistance(SiteKind a, SiteKind b) =>
            (a == SiteKind.Town, b == SiteKind.Town) switch
            {
                (true, true) => _rules.TownTown,
                (false, false) => _rules.IndustryIndustry,
                _ => _rules.TownIndustry,
            };
    }
}

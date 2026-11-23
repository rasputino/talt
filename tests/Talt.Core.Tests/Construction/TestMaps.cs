using Talt.Core.Construction;
using Talt.Core.Maps;

namespace Talt.Core.Tests.Construction;

/// <summary>Small hand-made maps for the construction rules (flat land, water, trees, objects).</summary>
internal sealed class TestMap
{
    private readonly byte[] _height;
    private readonly byte[] _terrain;
    private readonly byte[] _water;
    private readonly byte[] _flags;
    private readonly byte[] _scenery;

    public TestMap(int size = 64, int height = 10)
    {
        Size = size;
        var count = size * size;
        _height = new byte[count];
        _terrain = new byte[count];
        _water = new byte[count];
        _flags = new byte[count];
        _scenery = new byte[count];
        Array.Fill(_height, (byte)height);
        Array.Fill(_flags, (byte)TileTraits.Buildable);
    }

    public int Size { get; }

    public MapSettings Settings => new(Size, Size, 1);

    public static ConstructionRules Rules { get; } = new()
    {
        StartingBalance = 1_000_000,
        TerraformStepCost = 100,
        ClearTreeCost = 10,
        DemolishCosts = new Dictionary<TileObjectKind, long> { [TileObjectKind.Test] = 500 },
        UndoWindowTicks = 200,
        MaxHeight = 60,
        MaxTilesPerAction = 4096,
        MaxActionCost = 1_000_000_000,
    };

    public TestMap Height(int x, int z, int height)
    {
        _height[x + z * Size] = (byte)height;
        return this;
    }

    public TestMap Water(int x, int z, int bed, int level)
    {
        var i = x + z * Size;
        _height[i] = (byte)bed;
        _water[i] = (byte)level;
        _terrain[i] = (byte)TerrainType.Water;
        _flags[i] = (byte)TileTraits.Lake;
        return this;
    }

    public TestMap Tree(int x, int z)
    {
        _scenery[x + z * Size] = Scenery.Pack(Scenery.KindConifer, 3);
        return this;
    }

    public TestMap Terrain(int x, int z, TerrainType type)
    {
        _terrain[x + z * Size] = (byte)type;
        return this;
    }

    public MapData Build()
    {
        var map = new MapData(Settings, (byte[])_height.Clone(), (byte[])_terrain.Clone(), (byte[])_water.Clone(), (byte[])_flags.Clone(), (byte[])_scenery.Clone(), [], default);
        for (var z = 0; z < Size; z++)
        {
            for (var x = 0; x < Size; x++)
            {
                map.RecomputeTraits(x, z);
            }
        }

        return map;
    }

    public ConstructionState State(ConstructionRules? rules = null) => new(rules ?? Rules, Build());
}

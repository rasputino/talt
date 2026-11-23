using Talt.Core.Construction;
using Talt.Core.Maps;
using Talt.Core.Terrain;
using Talt.Core.Voxels;

namespace Talt.Client.Construction;

/// <summary>
/// Brings the drawn world in line with the simulation's tile data after construction commands
/// (research R6): rewrites the voxel columns of changed tiles, refreshes the heightmap used by the
/// camera and vehicles, and lists the changed voxels for the renderer's dirty marking. Buffers are
/// reused; nothing is allocated in frames without changes.
/// </summary>
internal sealed class TerrainSync
{
    private readonly ConstructionState _state;
    private readonly VoxelWorld _world;
    private readonly Heightmap _heightmap;
    private readonly List<int> _tiles = [];
    private readonly List<(int X, int Y, int Z)> _voxels = [];

    public TerrainSync(ConstructionState state, VoxelWorld world, Heightmap heightmap)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _heightmap = heightmap ?? throw new ArgumentNullException(nameof(heightmap));
    }

    /// <summary>Incremented whenever trees or objects may have changed (tree instances are rebuilt then).</summary>
    public int SceneryVersion { get; private set; }

    /// <summary>
    /// Applies pending tile changes to the voxels and the heightmap. Returns the changed voxels (empty
    /// when nothing changed); the list is reused by the next call.
    /// </summary>
    public IReadOnlyList<(int X, int Y, int Z)> Sync()
    {
        _voxels.Clear();
        _tiles.Clear();
        if (!_state.TryTakeChangedTiles(_tiles))
        {
            return _voxels;
        }

        var map = _state.Map;
        var width = map.Width;
        foreach (var tile in _tiles)
        {
            var x = tile % width;
            var z = tile / width;
            var previous = _heightmap.Get(x, z);
            if (previous == map.SurfaceHeightAt(x, z))
            {
                continue; // Only scenery or objects changed on this tile.
            }

            var (minY, maxY) = WorldBuilder.RebuildColumn(map, _world, x, z, previous);
            _heightmap.Refresh(map, x, z);
            for (var y = minY; y <= maxY; y++)
            {
                _voxels.Add((x, y, z));
            }
        }

        SceneryVersion++;
        return _voxels;
    }
}

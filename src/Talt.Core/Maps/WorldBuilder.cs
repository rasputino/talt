using Talt.Core.Voxels;

namespace Talt.Core.Maps;

/// <summary>
/// Turns generated tile data into the voxels the renderer draws. Chunks are created first (the
/// world's dictionary is not thread safe), then filled in parallel, one chunk column per task.
/// </summary>
public static class WorldBuilder
{
    /// <summary>
    /// Builds the voxel world of a map. Progress reports use the <see cref="MapGenerationStage.World"/>
    /// stage with a fraction of this step alone (0 to 1).
    /// </summary>
    public static VoxelWorld Build(
        MapData map,
        IProgress<MapGenerationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(map);

        var chunksX = map.Width / Chunk.Size;
        var chunksZ = map.Depth / Chunk.Size;
        var world = new VoxelWorld();
        var columns = new Chunk[chunksX * chunksZ][];

        // Sequential: create every chunk that holds ground or water.
        for (var cz = 0; cz < chunksZ; cz++)
        {
            for (var cx = 0; cx < chunksX; cx++)
            {
                var top = 0;
                for (var z = cz * Chunk.Size; z < (cz + 1) * Chunk.Size; z++)
                {
                    for (var x = cx * Chunk.Size; x < (cx + 1) * Chunk.Size; x++)
                    {
                        top = Math.Max(top, map.SurfaceHeightAt(x, z));
                    }
                }

                var layers = (top + Chunk.Size - 1) / Chunk.Size;
                var column = new Chunk[layers];
                for (var cy = 0; cy < layers; cy++)
                {
                    column[cy] = world.GetOrCreateChunk(new ChunkCoord(cx, cy, cz));
                }

                columns[cx + (cz * chunksX)] = column;
            }

            cancellationToken.ThrowIfCancellationRequested();
        }

        progress?.Report(new MapGenerationProgress(MapGenerationStage.World, 0.2f));

        var done = 0;
        var options = new ParallelOptions { CancellationToken = cancellationToken };
        Parallel.For(0, columns.Length, options, index =>
        {
            FillColumn(map, columns[index], index % chunksX, index / chunksX);
            var finished = Interlocked.Increment(ref done);
            if (finished % Math.Max(1, columns.Length / 10) == 0)
            {
                progress?.Report(new MapGenerationProgress(MapGenerationStage.World, 0.2f + (0.8f * finished / columns.Length)));
            }
        });

        progress?.Report(new MapGenerationProgress(MapGenerationStage.World, 1f));
        return world;
    }

    private static void FillColumn(MapData map, Chunk[] layers, int cx, int cz)
    {
        for (var lz = 0; lz < Chunk.Size; lz++)
        {
            for (var lx = 0; lx < Chunk.Size; lx++)
            {
                var x = (cx * Chunk.Size) + lx;
                var z = (cz * Chunk.Size) + lz;
                var i = map.Index(x, z);
                var height = map.Heights[i];
                var waterLevel = map.WaterLevels[i];
                var type = (TerrainType)map.Terrain[i];
                var flags = (TileTraits)map.Flags[i];
                var topVoxel = height - 1;

                for (var y = 0; y < Math.Max(height, (int)waterLevel); y++)
                {
                    var material = y >= height ? VoxelMaterial.Water : Material(type, flags, y, topVoxel);
                    layers[y >> Chunk.SizeBits].WritableVoxels[lx + (lz << Chunk.SizeBits) + ((y & Chunk.Mask) << (Chunk.SizeBits * 2))] = material;
                }
            }
        }
    }

    private static VoxelMaterial Material(TerrainType type, TileTraits flags, int y, int top)
    {
        var depth = top - y;
        if ((flags & (TileTraits.Sea | TileTraits.Lake | TileTraits.River)) != 0)
        {
            // Bed under water: sand on top, then dirt, then stone.
            return depth == 0 ? VoxelMaterial.Sand : depth <= 2 ? VoxelMaterial.Dirt : VoxelMaterial.Stone;
        }

        return type switch
        {
            TerrainType.Beach => depth <= 2 ? VoxelMaterial.Sand : VoxelMaterial.Stone,
            TerrainType.Rock => VoxelMaterial.Stone,
            TerrainType.Snow => depth == 0 ? VoxelMaterial.Snow : VoxelMaterial.Stone,
            _ => depth == 0 ? VoxelMaterial.Grass : depth <= 2 ? VoxelMaterial.Dirt : VoxelMaterial.Stone,
        };
    }
}

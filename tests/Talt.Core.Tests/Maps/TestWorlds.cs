using Talt.Core.Maps;
using Talt.Core.Voxels;

namespace Talt.Core.Tests.Maps;

/// <summary>Small generated worlds for tests that need terrain (meshing, editing).</summary>
internal static class TestWorlds
{
    /// <summary>A square world of <paramref name="chunks"/> x <paramref name="chunks"/> chunks with default settings.</summary>
    public static VoxelWorld Square(int chunks, int seed) =>
        WorldBuilder.Build(MapGenerator.Generate(new MapSettings(chunks * Chunk.Size, chunks * Chunk.Size, Seed: seed)));
}

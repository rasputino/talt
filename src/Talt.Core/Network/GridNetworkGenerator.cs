using Talt.Core.Determinism;

namespace Talt.Core.Network;

/// <summary>
/// Generates a synthetic road grid for testing and benchmarking: nodes on a regular grid connected
/// to their neighbours, with some roads randomly removed so routes are not trivial.
/// </summary>
public static class GridNetworkGenerator
{
    /// <param name="nodesX">Number of nodes along X.</param>
    /// <param name="nodesZ">Number of nodes along Z.</param>
    /// <param name="spacingTiles">Distance between neighbouring nodes, in tiles.</param>
    /// <param name="seed">Random seed for road removal.</param>
    /// <param name="removalChancePerMille">Probability (0..1000) that each road is left out.</param>
    /// <param name="originTileX">Tile X of node (0, 0).</param>
    /// <param name="originTileZ">Tile Z of node (0, 0).</param>
    public static TransportNetwork Generate(
        int nodesX,
        int nodesZ,
        int spacingTiles,
        int seed = 1,
        int removalChancePerMille = 150,
        int originTileX = 0,
        int originTileZ = 0)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(nodesX, 2);
        ArgumentOutOfRangeException.ThrowIfLessThan(nodesZ, 2);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(spacingTiles);
        ArgumentOutOfRangeException.ThrowIfNegative(removalChancePerMille);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(removalChancePerMille, 1000);

        var builder = new TransportNetworkBuilder();
        for (var z = 0; z < nodesZ; z++)
        {
            for (var x = 0; x < nodesX; x++)
            {
                builder.AddNodeAtTile(originTileX + x * spacingTiles, originTileZ + z * spacingTiles);
            }
        }

        var random = new DeterministicRandom(unchecked((ulong)seed));
        for (var z = 0; z < nodesZ; z++)
        {
            for (var x = 0; x < nodesX; x++)
            {
                var node = NodeIndex(x, z, nodesX);
                if (x + 1 < nodesX && random.NextInt(1000) >= removalChancePerMille)
                {
                    builder.AddRoad(node, NodeIndex(x + 1, z, nodesX));
                }

                if (z + 1 < nodesZ && random.NextInt(1000) >= removalChancePerMille)
                {
                    builder.AddRoad(node, NodeIndex(x, z + 1, nodesX));
                }
            }
        }

        return builder.Build();
    }

    public static int NodeIndex(int x, int z, int nodesX) => x + z * nodesX;
}

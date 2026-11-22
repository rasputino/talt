using System.Numerics;

namespace Talt.Core.Network;

/// <summary>
/// Generates a synthetic road grid for testing and benchmarking: nodes on a regular grid connected
/// to their neighbours, with some roads randomly removed so routes are not trivial.
/// </summary>
public static class GridNetworkGenerator
{
    /// <param name="nodesX">Number of nodes along X.</param>
    /// <param name="nodesZ">Number of nodes along Z.</param>
    /// <param name="spacing">Distance between neighbouring nodes, in tiles.</param>
    /// <param name="seed">Random seed for road removal.</param>
    /// <param name="removalChance">Probability (0..1) that each road is left out.</param>
    /// <param name="origin">Ground position of node (0, 0), in tiles.</param>
    public static TransportNetwork Generate(
        int nodesX,
        int nodesZ,
        float spacing,
        int seed = 1,
        float removalChance = 0.15f,
        Vector2 origin = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(nodesX, 2);
        ArgumentOutOfRangeException.ThrowIfLessThan(nodesZ, 2);

        var builder = new TransportNetworkBuilder();
        for (var z = 0; z < nodesZ; z++)
        {
            for (var x = 0; x < nodesX; x++)
            {
                builder.AddNode(origin + new Vector2(x * spacing, z * spacing));
            }
        }

        var random = new Random(seed);
        for (var z = 0; z < nodesZ; z++)
        {
            for (var x = 0; x < nodesX; x++)
            {
                var node = NodeIndex(x, z, nodesX);
                if (x + 1 < nodesX && random.NextSingle() >= removalChance)
                {
                    builder.AddRoad(node, NodeIndex(x + 1, z, nodesX));
                }

                if (z + 1 < nodesZ && random.NextSingle() >= removalChance)
                {
                    builder.AddRoad(node, NodeIndex(x, z + 1, nodesX));
                }
            }
        }

        return builder.Build();
    }

    public static int NodeIndex(int x, int z, int nodesX) => x + z * nodesX;
}

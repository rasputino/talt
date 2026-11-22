using System.Numerics;
using Talt.Core.Network;

namespace Talt.Core.Tests.Network;

public class PathFinderTests
{
    [Fact]
    public void Network_OutEdges_ListsEdgesLeavingEachNode()
    {
        var builder = new TransportNetworkBuilder();
        var a = builder.AddNode(new Vector2(0, 0));
        var b = builder.AddNode(new Vector2(3, 4));
        var c = builder.AddNode(new Vector2(3, 0));
        builder.AddRoad(a, b);
        builder.AddEdge(a, c);
        var network = builder.Build();

        Assert.Equal(2, network.OutEdges(a).Length);
        Assert.Equal(1, network.OutEdges(b).Length);
        Assert.Equal(0, network.OutEdges(c).Length);
        Assert.Equal(5f, network.EdgeLength(network.OutEdges(b)[0]), 3);
    }

    [Fact]
    public void FullGrid_ShortestPath_HasManhattanLength()
    {
        var network = GridNetworkGenerator.Generate(10, 10, spacing: 8, removalChance: 0);
        var pathFinder = new PathFinder(network);
        var path = new List<int>();

        var found = pathFinder.TryFindPath(
            GridNetworkGenerator.NodeIndex(1, 2, 10),
            GridNetworkGenerator.NodeIndex(7, 9, 10),
            path);

        Assert.True(found);
        Assert.Equal(6 + 7, path.Count);
        Assert.Equal((6 + 7) * 8f, path.Sum(network.EdgeLength), 3);
    }

    [Fact]
    public void Path_IsConnectedFromStartToGoal()
    {
        var network = GridNetworkGenerator.Generate(20, 20, spacing: 4, seed: 3, removalChance: 0.3f);
        var pathFinder = new PathFinder(network);
        var path = new List<int>();
        var start = GridNetworkGenerator.NodeIndex(0, 0, 20);
        var goal = GridNetworkGenerator.NodeIndex(19, 19, 20);

        Assert.True(pathFinder.TryFindPath(start, goal, path));
        Assert.Equal(start, network.EdgeFrom(path[0]));
        Assert.Equal(goal, network.EdgeTo(path[^1]));
        for (var i = 1; i < path.Count; i++)
        {
            Assert.Equal(network.EdgeTo(path[i - 1]), network.EdgeFrom(path[i]));
        }
    }

    [Fact]
    public void UnreachableGoal_ReturnsFalse()
    {
        var builder = new TransportNetworkBuilder();
        var a = builder.AddNode(Vector2.Zero);
        var b = builder.AddNode(Vector2.One);
        var c = builder.AddNode(new Vector2(5, 5));
        builder.AddRoad(a, b);
        var network = builder.Build();
        var path = new List<int> { 99 };

        Assert.False(new PathFinder(network).TryFindPath(a, c, path));
        Assert.Empty(path);
    }

    [Fact]
    public void RepeatedSearches_GiveSameResult()
    {
        var network = GridNetworkGenerator.Generate(30, 30, spacing: 4, seed: 9, removalChance: 0.2f);
        var pathFinder = new PathFinder(network);
        var first = new List<int>();
        var again = new List<int>();

        pathFinder.TryFindPath(5, 800, first);
        pathFinder.TryFindPath(123, 456, again); // Different search in between reuses the buffers.
        pathFinder.TryFindPath(5, 800, again);

        Assert.Equal(first, again);
    }

    [Fact]
    public void SameStartAndGoal_IsAnEmptyPath()
    {
        var network = GridNetworkGenerator.Generate(3, 3, spacing: 1);
        var path = new List<int>();

        Assert.True(new PathFinder(network).TryFindPath(4, 4, path));
        Assert.Empty(path);
    }
}

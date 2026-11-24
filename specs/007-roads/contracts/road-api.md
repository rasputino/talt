# Contract: road API (Talt.Core)

```csharp
// Talt.Core.Roads
public sealed class RoadLayer : IStateHashable
{
    int Width, Depth; byte LinksAt(int tile); byte ExitsAt(int tile); int TypeAt(int tile); // -1 none
    RoadFeatures FlagsAt(int tile); int SpanAt(int tile); RoadPiece PieceAt(int tile); bool HasRoad(int tile);
    int RoadTileCount { get; }
    bool TryTakeChangedTiles(List<int> into);       // client drawing; not state
}
public sealed class RoadPlanner                       // one per consumer; ConstructionPlanner.Roads
{
    void PlanRoad(ConstructionState s, int company, int type, (int X,int Z) a, (int X,int Z) b, ConstructionPlan plan);
    void PlanBridge(...); void PlanTunnel(...); void PlanRemoval(...); void PlanOneWay(...);
    void PlanMotorway(...); void PlanConnector(...);
}
public sealed record RoadRules { IReadOnlyList<RoadType> Types; int AvailableTypeCount(int year); int MaxBridgeSpan(int year); ... }

// Talt.Core.Network
public sealed class RoadNetwork
{
    static RoadNetwork Build(RoadLayer layer);
    void Update(ReadOnlySpan<int> changedTiles, ...);  // called by ConstructionState
    int Version; int NodeCount; bool TryGetNode(int tile, out int node);
    ReadOnlySpan<int> OutEdgeTargets(int node) ...;    // traversal via region CSR
    IReadOnlyList<RoadEdgeInfo> Canonical();            // tests
}
public sealed class RoadRouter                        // one per thread
{
    RoadRouter(RoadNetwork network, bool cache = true);
    bool TryFindRoute(int fromTile, int toTile, List<RouteStep> steps, out long length); // node tiles
}
```

Client: `ConstructionTool` adds road tools (shape `Path`, `Line8`, `Line`, `Tile`); `RoadToolOptions`
(selected type index, version); `RoadMesher` (GPU-free) and `RoadRenderer`.

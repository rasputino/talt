# Contract: Construction API (Core) and Tool Controller (Client)

## Core (`Talt.Core.Construction`)

```csharp
public sealed class ConstructionState            // GameSimulation.Construction (null without a map)
{
    ConstructionRules Rules; MapData Map; TileObjects Objects; Company Player; UndoRecord Undo;
    int Version;                                  // bumped per applied action (not hashed)
    Company? FindCompany(int id);
    bool TryTakeChangedTiles(List<int> into);     // tiles changed since the last call (client sync)
}

public sealed class ConstructionPlanner           // one per thread / consumer; reuses buffers
{
    void PlanTerraform(ConstructionState s, TerraformMode mode, TileRect area, int startX, int startZ, ConstructionPlan plan);
    void PlanClear(ConstructionState s, int companyId, TileRect area, ConstructionPlan plan);
}

public abstract class ConstructionCommand : ISimulationCommand
{
    int CompanyId;
    // Shared by Validate and the client preview: plan + limits + money check.
    CommandResult Preview(ConstructionState s, ConstructionPlanner planner, ConstructionPlan plan);
    protected abstract void Plan(ConstructionState s, ConstructionPlanner planner, ConstructionPlan plan);
}
```

Later features add an action by subclassing `ConstructionCommand` (their own `Plan`, plus extra apply
steps for their objects through an `ApplyObjects` hook) and registering it; the preview, money, undo
and change log steps are shared (FR-017).

`GameSimulation` additions:

- `ConstructionState? Construction`
- `static GameSimulation Create(ScenarioSettings settings, MapData? map = null)`
- `void ProcessPendingCommands()` — applies queued commands now without advancing time (paused).

`WorldBuilder.RebuildColumn(MapData map, VoxelWorld world, int x, int z)` returns `(int MinY, int MaxY)`
of rewritten voxels. `Heightmap.Refresh(MapData map, int x, int z)` updates one column.

## Client (`Talt.Client.Construction`)

```csharp
internal sealed class ToolController
{
    ConstructionTool? Active; bool IsDragging; ConstructionPlan Preview; TileRect PreviewArea;
    void Select(ConstructionTool? tool);
    void Hover((int X, int Z)? tile);              // every frame from picking
    void Press((int X, int Z) tile);  void Release();  bool Cancel();  // Cancel: true if something was cancelled
    // Re-plans only when tool, rectangle or state version changed; returns true when the preview changed.
    bool UpdatePreview(ConstructionState state);
}
```

- Submitting: `Release()` submits `tool.Create(company, start, end)` to the simulation.
- Input while a tool is active: left drag = tool drag (no rotation); right button or `back` cancels;
  `back` with no drag deselects the tool.
- Preview text: cost formatted with `MoneyFormatter` in the display currency; reason from
  `construction.reason.<code>`.

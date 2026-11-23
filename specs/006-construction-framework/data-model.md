# Data Model: Construction and Terraforming Framework

All simulation types use integers only (registered in `NoFloatingPointStateTests`).

## ConstructionRules (data, immutable)

Loaded from `Talt.Core/Construction/Data/construction.json` (contracts/data-files.md).

| Field | Type | Rule |
|---|---|---|
| StartingBalance | long | ≥ 0; player company balance at game start (500 000) |
| TerraformStepCost | long | ≥ 0; per tile per height step changed |
| ClearTreeCost | long | ≥ 0; per tree removed (also when terraforming removes it) |
| DemolishCosts | map TileObjectKind → long | ≥ 0; only known kinds |
| UndoWindowTicks | int | ≥ 0; 200 |
| MaxHeight | int | 2–127; highest ground a terraform may create |
| MaxTilesPerAction | int | 1–1 048 576; 16 384 |
| MaxActionCost | long | > 0; refuses absurd totals |

## Company (state)

| Field | Type | Notes |
|---|---|---|
| Id | int | 0 = the player |
| Balance | long | internal units (1 = 1 USD); never negative after a construction action |

## TileObjects (state, dense per tile)

| Field | Type | Notes |
|---|---|---|
| Kind[i] | TileObjectKind (byte) | None, PublicRoad, Test (255); later features add kinds |
| Owner[i] | TileOwner (ushort) | bits 14–15 kind: None, Company, Town, Industry; bits 0–13 index |

Created from the map: tiles crossed by the prototype network get `PublicRoad` owned by `Town(0)`
(same tile walk as `NetworkTerrainPainter`). `Kind == None` ⇔ `Owner == None`.

## Map tile data (state, from feature 004 `MapData`)

Heights, terrain type, water level, flags, scenery per tile. Construction changes height, terrain
(Rock → Grass on changed tiles), flags (Cliff, Buildable recomputed around changes) and scenery
(trees removed). Water tiles never change in this feature.

## ConstructionAction (command input, immutable)

| Command | Fields | Shape |
|---|---|---|
| TerraformCommand | CompanyId, Mode (Raise, Lower, Level), X0, Z0, X1, Z1, (Level) StartX, StartZ | area (inclusive rectangle, any corner order) |
| ClearAreaCommand | CompanyId, X0, Z0, X1, Z1 | area |
| UndoConstructionCommand | CompanyId | — |

## ConstructionPlan (planner output, reusable, not state)

| Field | Type | Notes |
|---|---|---|
| Changes | list of TileChange | tile index, old/new height, old/new scenery, old/new object kind and owner |
| Problems | list of TileProblem | tile index, reason code |
| Cost | long | total |
| Reason | string? | first problem's reason (or `not-enough-money`, `too-large`, `nothing-to-do`) |
| IsValid | bool | no problems and something changes |
| AreaX0..Z1 | int | clipped rectangle (for the preview) |

## UndoRecord (state)

| Field | Type | Notes |
|---|---|---|
| HasValue | bool | |
| CompanyId | int | |
| AppliedAtTick | long | |
| Cost | long | refunded on undo |
| Tiles | list of (index, before, after) | height, terrain, flags, scenery, object kind, owner |

State transitions: none → recorded (any applied action of a company, replacing the previous record)
→ undone/cleared (undo applied). Undo valid iff record exists for the company, within the window and
every tile equals its after-values.

## ConstructionState (state, owned by GameSimulation when the scenario has a map)

`Rules`, `Map` (MapData), `Objects`, `Companies` (one for now), `Undo`, `Version` (bumped per applied
action, for preview caching, not hashed), `ChangedTiles` (log drained by the client, not hashed).
Hashed: balances, map tile arrays, objects/owners, undo record.

## Client-side (not simulation state)

- **ConstructionTool**: id, icon, shape (Tile, Line, Area), action id, factory
  `(company, start, end) → ISimulationCommand`.
- **ToolController**: active tool, drag start tile, current tile, dragging flag, last planned key
  (tool, rectangle, state version), the preview plan.
- **PreviewBox / TreeInstance**: render-only floats (position, size, colour).

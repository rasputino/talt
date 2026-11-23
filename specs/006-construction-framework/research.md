# Research: Construction and Terraforming Framework

Decisions for feature 006. Each entry: decision, rationale, alternatives considered.

## R1 — The map becomes simulation state

**Decision**: `ScenarioSettings` gains an optional `MapSettings? Map`. `GameSimulation.Create(settings,
map = null)` builds a `ConstructionState` when `settings.Map` is set: it takes the given `MapData`
(the client generates it with progress reporting and passes it in; it must have exactly
`settings.Map` as its settings) or generates it itself (headless replay). The simulation then owns
the map: tile data changes only through commands. Recorded sessions write the map as its shareable
text (`MapSettings.ToText()`) in an optional `map` setting; format version stays 1 because readers
ignore unknown properties and old sessions have no map.

**Rationale**: terraforming changes heights that later features (roads, stations, vehicles on
slopes) read; replays and multiplayer need them reproduced exactly from settings + commands. Feature
004 already made generation deterministic and cheap enough (4096² in ~2 s) to regenerate on replay.

**Alternatives**: keep the map client-side and log edits outside commands (not replayable, violates
constitution III); store the whole map in sessions (tens of MB per session, pointless when the
generator is deterministic).

## R2 — Hash construction state only when a map exists

**Decision**: `GameSimulation.AppendTo` appends `ConstructionState` (company balances, map tile
arrays, object/owner layer, undo record) only when the scenario has a map. Map-less scenarios
(headless defaults, benchmarks, CI goldens) have no construction state, so their golden fingerprints
(`C9F573FD7DE5CBE3`, `C0C656C5AB2D3302`, the Windows reference session) do not change. A new golden
pins a small map scenario with construction commands (cross-platform check of SC-006). The client
smoke-test fingerprint (`1475835573081C3A`, client with a generated map) changes on purpose to
`578FD3BEDBF498BC` (only documented; the CI smoke test checks the line's format, not the value).

**Rationale**: fingerprints must cover all state that affects the future; a game without a map has
nothing to build on. Avoiding churn in the CI goldens also avoids merge conflicts with features 009
and 019 developed in parallel.

**Alternatives**: always create a company and hash it (changes every golden for no behavioural
reason); hash the map through `MapData.Fingerprint()` (that function stays the generation golden of
004 and does not include the new object layer).

## R3 — Commands while paused

**Decision**: `GameSimulation.ProcessPendingCommands()` validates and applies queued commands
without advancing `CurrentTick` or running systems; `GameSession` calls it every frame while the
runner is paused and commands are queued. Commands processed this way are recorded with the current
tick, exactly like commands processed at the start of the next `Tick()`; on replay they are submitted
before that tick and processed at its start, before the systems run, so state and order are the same.
While running, commands keep being processed at the start of the next tick (≤ 50 ms).

**Rationale**: the clarified spec requires immediate effect while paused; recording semantics stay
"processed at the start of tick T". The undo window counts ticks, so it does not run out while paused.

**Alternatives**: a special zero-length tick (would fire tick-based systems); queue until unpause
(rejected by the clarification).

## R4 — One planner, used by preview and by the command

**Decision**: `ConstructionPlanner.Plan(state, action, plan)` fills a reusable `ConstructionPlan`
(changed tiles with old/new height, scenery and object; per-tile problems with reason codes; total
cost; first reason). `ConstructionCommand.Validate` plans with the simulation's planner and adds the
money check; `Apply` commits the plan that the same tick's validation produced (the contract says
`Apply` is only called right after a successful `Validate`). The client's preview owns a separate
planner instance and calls the same `Plan` + the same money check through
`ConstructionCommand.Preview(state, plan)` — the preview can never disagree with the application in
the same state (SC-002).

**Rationale**: one code path; allocation-free; a test asserts preview cost == money spent for every
command kind.

**Alternatives**: separate preview logic in the client (drifts); re-plan inside `Apply` (double work;
fine but needless).

## R5 — Terraform rules and the cascade

**Decision**: heights are per-tile ground heights in voxels (feature 004 `MapData.Heights`), with the
one-step rule over the 4-neighbourhood as in 004's classifier.

- *Raise* area: each area tile target = height + 1. *Lower*: height − 1. *Level*: every area tile =
  height of the drag's start tile.
- Cascade (fixpoint, FIFO worklist seeded in row-major order): for a changed tile `t` with new height
  `H` and a land neighbour `n`, `n` must end within `[H−1, H+1]`; if not, `n` is clamped into that
  range (raise only raises, lower only lowers, level may do either — the bounds from a uniform level
  area are always consistent) and queued. Raise/lower cascades may also adjust tiles inside the area
  (for example an existing cliff inside a raised area). Each tile's final height is the least/greatest
  fixpoint, so the order of processing does not change the result (property test).
- Pairs where neither tile changes keep their difference (existing cliffs stay, SC-004).
- A tile that must change is refused (whole action refused, red) when it is: water (sea, lake, river
  or any water level > 0) → `water`; under an object (any owner in v1: there are no foundations yet)
  → `object-in-the-way` (own object) or `owned-by-other` (town or other company); would go below 1 or
  above `maxHeight` (data, ≤ 127) → `height-limit`; would end below the water surface of a
  neighbouring water tile → `would-flood`.
- Changed tiles lose a tree (its clearing cost is added). Changed tiles of type Rock become Grass;
  Cliff and Buildable flags are recomputed for changed tiles and their neighbours (Buildable = land,
  not cliff, not rock).
- Limits: the area is clipped to the map; at most `maxTilesPerAction` changed tiles (data, default
  16 384) → `too-large`.
- Cost = Σ |new − old| × `terraformStepCost` + trees removed × `clearTreeCost`.

**Rationale**: the clarified spec chose the cascade; monotone clamping is simple, terminates
(heights bounded) and is order-independent, which is what determinism needs.

**Alternatives**: corner-height terrain with slopes per tile (a different terrain model from 004);
reject instead of cascade (rejected in clarify).

## R6 — Voxel updates and new chunks while meshing

**Decision**: the client drains `ConstructionState.ChangedTiles` after commands are processed and
calls `WorldBuilder.RebuildColumn(map, world, x, z)` (Core) for each tile: it rewrites the column from
the tile data with the same materials as generation (clears voxels above the new surface) and returns
the changed voxel range; `Heightmap.Refresh(map, x, z)` updates the camera/vehicle heightmap; the
changed voxels go to `WorldRenderer.NotifyVoxelsChanged`. Raising a column above its chunk needs a
new chunk: `VoxelWorld` switches to a `ConcurrentDictionary` so mesh workers can read while the main
thread creates chunks, and `WorldRenderer` registers chunks that appear after setup (its region grid
already covers Y 0–127 because heights are ≤ 127). The prototype road voxels are never under a
changed tile (roads block terraform), so no repainting is needed.

**Rationale**: reuses the feature 003 edit path (measured 29–34 ms to screen). The meshing benchmark
is re-run to check the dictionary change costs nothing measurable (chunk lookups happen per chunk,
not per voxel, in the greedy mesher's padded copy).

**Alternatives**: pre-create all chunks up to height 127 (up to 2 GB on 4096 maps); a lock around
chunk creation and every lookup (contention in workers).

## R7 — Ownership model

**Decision**: a dense `TileObjects` layer in `ConstructionState`: one byte `TileObjectKind` and one
`TileOwner` (ushort: 2-bit kind None/Company/Town/Industry + 14-bit index) per tile. 006 kinds:
`None`, `PublicRoad` (the prototype road network, owner Town 0), `Test` (tests only). Trees stay in
the scenery array (owner nobody). Later features add kinds (road pieces, station tiles, depots) and
their demolish costs in `construction.json`. Memory: 3 bytes per tile (48 MB at 4096²; the voxel
world dominates anyway).

**Rationale**: O(1) checks in the planner hot loop; works for every later object; mirrors
`MapData`'s byte-per-tile layout.

**Alternatives**: sparse dictionary of objects (slower checks, allocations); owners inside later
features' own structures (no shared rule for terraform/demolish).

## R8 — Allocation-free planning

**Decision**: the planner keeps an open-addressing `TileSet` (tile index → slot, generation stamp so
clearing is O(1)), growable arrays for changes/problems and a FIFO queue, all sized on first use and
reused; capacities grow to `maxTilesPerAction` and are pre-sized at state creation. No LINQ, no
closures. Previews are re-planned only when the hovered tile, the drag rectangle, the tool or the
state version (bumped by every applied command) changes.

**Rationale**: SC-001/SC-007 (500-tile drag < 1 ms, 0 B per frame) on 4096 maps, where a per-tile
scratch array would cost 64 MB.

**Alternatives**: `HashSet<int>`/`Dictionary` (allocate when growing, slower); per-map stamp array
(memory).

## R9 — Money

**Decision**: `Company` with `long Balance` in internal units (1 unit = 1 US dollar, as feature 005).
Player company id 0, created with `startingBalance` from `construction.json` (500 000, the old
`company.json` value, which is removed). Validation refuses with `not-enough-money` when cost >
balance; costs are summed in `long` with a cap `maxActionCost` → `too-large`. The HUD shows the
player company's balance. Full economy (loans, income) is feature 011.

## R10 — Undo

**Decision**: after applying an action, `ConstructionState.Undo` stores the company, the tick, the
cost, and per changed tile its index and both before/after values (height, scenery, flags, terrain,
object, owner), reusing its buffers. `UndoConstructionCommand(companyId)` is valid when a record
exists for that company, `CurrentTick − tick ≤ undoWindowTicks` (data, 200 = 10 s at normal speed),
and every recorded tile still equals its after-values (`changed-since`); applying restores the before
values, refunds the cost and clears the record (one level). Any later applied action of the company
replaces the record. Undo is recorded and replayed like any command; the record is hashed.

**Rationale**: exact restore without re-planning; the "nothing else changed it" rule is a direct
comparison, which also covers later actors (towns, other companies).

**Alternatives**: inverse commands (lossy with cascades and trees); multi-level history (not asked).

## R11 — Client tool controller and input

**Decision**: `ToolController` (GPU-free, unit-tested) holds the active tool and drag state. Tools:
Raise (PageUp), Lower (PageDown), Level (L), Clear/Demolish (X); Undo (Backspace) — all rebindable in
`actions.json`; the debug `raiseTerrain`/`lowerTerrain` actions are removed (saved overrides for them
are reported once as unknown and dropped, the existing behaviour). With a tool active: left press on
the map starts a drag (no camera rotation), the shape is the rectangle (area tools) or a straight
horizontal/vertical line (path tools, for 007+) between press and cursor tiles, release submits the
command, the right button or `back` (Esc) cancels the drag; Esc with no drag deselects the tool
before closing windows. Hovering without a drag previews the single tile. Picking supplies the tile
under the cursor (terrain layer of feature 005).

## R12 — Preview and tree drawing

**Decision**: one new instanced `BoxRenderer` (`boxes.wgsl`: unit cube × per-instance size, colour
with alpha) with two pipelines: opaque (trees, depth write) and translucent (preview, alpha blend, no
depth write, drawn after the world). Preview: one thin box per affected tile at its *new* surface
height, green (valid change), red (problem tile), capped at `maxTilesPerAction` instances. Cursor
label: a HUD label that follows the cursor with the cost (display currency) and, when invalid, the
localized reason. Trees: two boxes each (trunk, crown; shape and colour per kind and variant),
gathered from scenery data within a radius around the camera target (default 160 tiles, capped at
48 000 trees) and rebuilt only when the target moves by 16 tiles or scenery changes — not per frame.

**Rationale**: instancing already proved itself for 50 000 vehicles; reusing the unit cube keeps the
new GPU code small.

**Alternatives**: trees as terrain voxels (conflicts with 004's decision that scenery is removable
data and with greedy meshing); per-tile wire boxes with `HighlightRenderer` (4 boxes max, lines
unreadable for 500 tiles).

## R13 — Data file

**Decision**: `construction.json` embedded in `Talt.Core` (both client and headless need the same
costs to replay), loaded once by `ConstructionRules.LoadDefault()`, validated: all costs ≥ 0,
`startingBalance` ≥ 0, `undoWindowTicks` ≥ 0, `maxHeight` 2–127, `maxTilesPerAction` 1–1 048 576,
`demolishCosts` keys must name known object kinds; errors name the field. Tests can pass their own
`ConstructionRules`. JSON with comments allowed (as the client's data files).

## R14 — Benchmarks

**Decision**: `ConstructionBenchmarks` on a generated 1024² hilly map: preview of Level 25 × 20,
Raise 25 × 20 (with cascade), Clear 25 × 20 with trees, plus `Apply` of a level; memory diagnoser
must show 0 B for previews. `ChunkMeshingBenchmarks` re-run after R6. Results go to
`docs/ARCHITECTURE.md`.

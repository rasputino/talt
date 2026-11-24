# Research: Roads (feature 007)

Each entry: decision, rationale, alternatives considered.

## R1 — Two graphs: the prototype network stays for map-less scenarios

**Decision**: Keep the immutable `TransportNetwork` (+ `GridNetworkGenerator`, `SyntheticTraffic`,
`VehicleFleet` lines) unchanged for **map-less** scenarios (benchmarks, CI goldens, the windows-reference
session). Games **with a map** get an empty `TransportNetwork` and an empty fleet (clarification 2), plus
the new tile road layer (`RoadLayer`) and its incremental routing graph (`RoadNetwork`, Talt.Core.Network).
`ScenarioBuilder.Build` returns the empty network/fleet when `settings.Map` is set.

**Rationale**: the golden fingerprints hash `TransportNetwork`'s arrays and the vehicles' line edge ids;
any change to that class or its edge numbering changes every golden on every platform for no gameplay
gain. Vehicles on lines with fixed edge ids cannot survive road removal; real road vehicles (012) will
route on `RoadNetwork`. Keeping both is the smallest correct change.

**Alternatives**: (a) make `TransportNetwork` itself mutable and keep its hash identical for grids built
by the builder — possible but fragile (any reorder breaks goldens) and it still would not give vehicles
stable routes; (b) convert the prototype grid into tile roads on map games — rejected by the user.

**Golden impact**: CI goldens (`GoldenFingerprintTests`, `ci/golden-fingerprints.json`,
`ci/sessions/windows-reference.json`) are **unchanged**. Games with a map change on purpose (no grid, no
synthetic vehicles, town roads, road state hashed): the map golden in
`tests/Talt.Core.Tests/Determinism/ConstructionReplayTests.cs` is regenerated and a new road replay golden
is added; the commit message must say so.

## R2 — Tile road data (`RoadLayer`)

**Decision**: dense per-tile arrays (indexed like `MapData`, `x + z·width`), owned by
`ConstructionState` and hashed with it:

| Array | Type | Meaning |
|---|---|---|
| `Links` | byte | bit d = the piece connects towards direction d (Dir8: 0 N(−z), 1 NE, 2 E(+x), 3 SE, 4 S, 5 SW, 6 W, 7 NW) |
| `Exits` | byte | bit d = travel from this tile towards d is allowed (subset of `Links`) |
| `Type` | byte | road type index + 1 (0 = no road) |
| `Flags` | byte | `BridgeEnd`, `TunnelPortal`, `Connector`, `OverBridgeX`, `OverBridgeZ`, `UnderTunnelX`, `UnderTunnelZ` |
| `Span` | byte | for bridge ends and tunnel portals: distance in tiles to the other end (the long link) |

Owner and object presence stay in feature 006's `TileObjects` (kind `Road`, renamed from `PublicRoad`,
same value 1, owner company or town), so terraform/clear rules of 006 apply unchanged. A two-way link has
the link bit on both tiles and the exit bit on both; a one-way link A→B has both link bits and only A's
exit bit. Bridge/tunnel long links are an axis link bit whose `Span` > 0 on an end tile; span/interior
tiles only carry `Over*`/`Under*` flags (a ground road may exist below a bridge).

**Rationale**: five bytes per tile (80 MB on 4096², same order as `MapData`), O(1) access, trivially
hashed and captured by undo; one representation serves straight, diagonal, one-way, motorway and
connector links. Owners reuse the 006 layer.

**Alternatives**: a sparse dictionary of pieces (smaller on empty maps, slower and non-trivial to hash
deterministically); storing bridges as a separate object list (undo/hash become more complex; the
tile-only form makes "full rebuild from tiles" exact).

## R3 — Geometry rules (one validity function)

**Decision**: `RoadRules` holds the pure checks used by the planner, the seeder, tests and
`ConstructionState` assertions:

- Straight (axis) link A→B: both land (not water), |hA − hB| ≤ 1. If hB = hA + 1, A is a **ramp**: A's
  links must be exactly ⊆ {d, opposite d}; same for B when hB = hA − 1.
- Diagonal link: hA = hB, the two side tiles (A+axis1, A+axis2) are not higher than hA, the crossing
  diagonal (between the two side tiles) is not built, neither A nor B is a ramp.
- A tile with a ramp link, a bridge end or a tunnel portal is straight (two opposite links at most).
- Turns of 135° (sharp) are not proposed by the planner; junctions and corners only on non-ramp tiles.
- Terraform (006) additionally refuses tiles with `Over*`/`Under*` flags and raising a tile above the
  height of a diagonal link it is a side tile of (`object-in-the-way`).
- Slopes live on the lower tile (the ramp rises from h at its back edge to h + 1 at its front edge), so a
  road is always on or slightly above its voxel column and needs no terrain change.

**Rationale**: the voxel terrain is one height per tile; ramps on the lower tile keep road surfaces on
the terrain. Diagonal side-tile and crossing rules keep diagonal strips from cutting into hills or into
each other. (Spec edge case on terraforming under bridge spans is simplified to "blocked".)

## R4 — Road path proposal (`RoadPlanner`)

**Decision**: weighted A* over the tile grid inside the bounding box of the two ends grown by 48 tiles,
state = (tile, arrival direction 0–7, bridge-arrival 8–15, start 16), 8 moves, deterministic tie-break
(priority, state). Integer costs: axis step 1000, diagonal 1414, turn 45° +150 / 90° +400, ramp +300,
reusing an existing road of the same or a better type 400 per step, tree +60, auto-bridge (water only,
axis, up to the era's span) 3000 per tile. Heuristic: octile distance × 1000, weighted × 3/2; among equal
priorities the deeper state (higher cost so far) is expanded first. The proposal need not be the cheapest
path, it must be deterministic and cheap: the first implementation (unweighted, ties by state number) took
19 ms for a 200-tile drag on a hilly map; weighted with depth-first ties it takes 0.21 ms. Limits:
`maxSearchNodes` from data (default 60 000 expansions) → "no-path". Buffers reused (`TileSet` + growing
arrays), no allocation once grown. The reconstructed path becomes `TileChange`s carrying the new road
pieces; costs come only from `roads.json`.

**Rationale**: satisfies FR-010 (straight preferred, reuse, slopes, avoidance) and SC-004 (a 200-tile
drag plans in well under 2 ms on open ground: the heuristic is tight, expansions ≈ path length × small
factor). Planning is part of `Validate`, so replay re-plans on the same state and gets the same path.

**Alternatives**: two-segment L paths only (cheap, but cannot avoid obstacles or follow terrain);
admissible heuristic (explores far more on maps with many roads).

## R5 — Incremental routing graph (`RoadNetwork`)

**Decision**: junction-contracted graph derived from `RoadLayer`:

- **Node** = road tile where the number of links ≠ 2, or a linked neighbour has a different road type,
  or it is a connector, or it anchors a node-less loop (lowest tile index of the loop). Pure function of
  tile data.
- **Edge** = the chain of degree-2 tiles between two nodes, one per allowed travel direction: length in
  units (axis 65 536, diagonal 92 682, long link span × 65 536), speed limit (km/h of the chain's type, 0 =
  none), first direction (so vehicles can retrace tiles), and whether the chain is all-exit.
- **Storage**: stable node ids from a free list (`nodeTile`, `nodeRegion`, first edge, edge count);
  tile → node through a `Dictionary<int,int>` used only by rebuilds. **Regions** of 64 × 64 tiles each own
  a CSR block (`edgeTo`, `edgeLength`, `edgeSpeed`, `edgeFirstDir`) of the out-edges of their nodes, in
  (tile, direction) order. Traversal: `OutEdges(node)` is a span of the region's flat arrays.
- **Update**: commit/undo report changed tiles. Affected tiles = changed tiles + their linked neighbours
  (before and after). Before applying, chains through affected tiles are walked to their end nodes and
  those regions marked dirty; after applying, the same walk on the new data. Dirty regions are rebuilt:
  pass 1 recomputes their node sets (free / allocate ids), pass 2 retraces the out-edges of their nodes.
  Cost ∝ road tiles in dirty regions + walked chains, independent of map size (FR-003, SC-003).
- `Version` increments per update; the route cache is cleared on change.
- A canonical dump (`(fromTile, firstDir, toTile, length, speed)` sorted) lets tests compare an
  incrementally updated network with `RoadNetwork.Build(layer)` from scratch (FR-004).

**Rationale**: contraction removes ~90 % of tiles from searches; per-region CSR keeps traversal on flat
arrays while letting one action rebuild only a few regions.

**Alternatives**: one global CSR rebuilt on every change (O(map), violates FR-003); per-tile graph (16×
more nodes, searches too slow); splitting edges at region borders (doubles node count for no gain).

## R6 — Long-route search: contraction + cached routes; coarse region graph rejected

**Decision**: `RoadRouter` runs A* (octile heuristic — admissible because every road step is straight or
45° and long links are straight — cached per node per search, binary heap ordered by (priority, higher
cost, node tile)) on the contracted graph; ties never use node ids, so routes are a pure function of tile data (identical after incremental update or full rebuild, and after future
save/load). A bounded route cache (4 096 entries, FIFO) keyed by (start node tile, goal node tile)
returns repeated queries without searching and is cleared when `RoadNetwork.Version` changes.

**Rationale**: on the reference network (a full road grid every 16 tiles over 4096², 65 536 junctions),
the prototype A* on the same graph shape measures 1.3–1.7 ms per random whole-map query, with 15 %
removals. Measured on the fully connected grid: straight-line heuristic with ties by tile 2.0 ms (at the
budget); octile heuristic with depth-first ties 1.05 ms. Junction contraction is the hierarchical step the plan
prompt names. Exact coarse region graphs (HPA* with exact portal-to-portal distances) were evaluated on
paper: with 64-tile regions the abstract graph has as many nodes as the junction graph and 4× the edges,
and each region rebuild must recompute O(portals²) distances; approximate HPA* breaks FR-005 (shortest
route). Benchmarks (R11) confirm or refute; if SC-002 fails, the fallback is bidirectional A* (also exact).

**Alternatives**: contraction hierarchies (fast queries, but incremental updates are complex); ALT
landmarks (preprocessing invalidated by every change).

## R7 — Motorways

**Decision**: the motorway type has `motorway: true`. The motorway tool takes an 8-direction straight
line (start → end, dominant octant). Carriageway 1 = the line, travel start → end; carriageway 2 = offset
to the left (d − 90° for axis lines, d − 135° — an axis neighbour — for diagonals, giving 0.71 tile
spacing) with travel end → start (right-hand traffic). Each lane tile gets links to its lane neighbours
with the exit bit only in the travel direction. Lane endpoints join: another motorway lane end of the
same flow (a lane tile with one link, exits consistent), or any road tile (motorways join at ends).
`RoadRules` refuses any new link to a motorway tile that is not a lane end or a connector
(`motorway-junction`). Axis lines whose ground fails (water, crossing road, too steep) get **elevated
sections**: both lanes bridge from the last good flat tile to the next tile at the same height, up to
`elevatedMaxSpan`, at `elevatedCostPerTile`. Connector tool (exit or entry): on a straight, flat
carriageway tile, link to the outer side neighbour (away from the other lane), exit bit carriageway →
ramp (exit) or ramp → carriageway (entry); the ramp tile becomes a one-way piece of the best available
non-motorway type; the carriageway tile gets the `Connector` flag (drawn with an acceleration lane, and a
node in the graph).

**Rationale**: uses the same link/exit model; no special graph code.

## R8 — Bridges and tunnels

**Decision**: Bridge: axis line, both ends flat land at the same height h, ends straight; span tiles
(between) have surface (max(ground, water)) ≤ h, no other bridge over them, no industry; roads/objects
below are allowed; deck at h + 1. Max span by era (`bridgeEras`, clarification 4). Tunnel: portal tile at
h whose next tile (in the drag direction, or the single higher axis neighbour) is higher; interior tiles
higher than h with no tunnel already under them; exit = first tile at height h, which must be land, not
an industry, and its own next tile lower or equal; max 64. Both ends get the long link; spans/interior get
`Over*`/`Under*` flags. Removal of any end removes both ends and the flags.

## R9 — Town roads and town road removal

**Decision**: `RoadSeeder` lays a street grid on each town site (lines at offsets −4, 0, +4 in both axes,
9 tiles long, clipped to valid links by `RoadRules`, avoiding industries and water), type = best
available non-motorway type in the start year, owner `TileOwner.Town(siteIndex)`, trees on those tiles
removed. Town road removal (clarification 3): town pieces in a removal action are processed in drag order;
each is removed only if the town's remaining pieces stay connected through links between that town's
tiles (BFS over the town's tiles only); otherwise it is skipped with `town-needs-road`; cost
`townRoadRemoveCost`.

## R10 — Rendering roads

**Decision**: `RoadMesher` (client, GPU-free, unit-tested) builds triangles per 32 × 32-tile block:
flat pieces (hub + one arm per link; inner corner fill triangles for smoothed curves), ramps (surface from
h to h + 1 with side wedges), diagonal arms to corners, bridge decks at h + 1 with railings and pillars,
tunnel portal frames, connector acceleration lanes, one-way arrows, centre/edge markings by type colour
(`look` in roads.json). Vertex: position (3 × f32), normal (3 × f32), colour (unorm8×4) = 28 bytes;
`roads.wgsl` lights like `boxes.wgsl`. `RoadRenderer` keeps a detailed mesh for blocks within 256 tiles of
the camera target (built nearest-first within 2 ms per frame, released after 5 s out of range) and a
simplified mesh (merged straight runs at equal height, no markings) for the rest; both culled by frustum
and fog. Rebuilds only blocks touched by road changes (`RoadLayer.TryTakeChangedTiles`). Voxels are no
longer recoloured for roads on map games.

**Rationale**: meets FR-050 with a budget: detail is bounded by distance, far blocks are cheap.

## R11 — Benchmarks and budgets

`RoadNetworkBenchmarks` (Talt.Benchmarks): `FullBuild` (4096² grid every 16 tiles), `IncrementalUpdate`
(build then remove a 100-tile road crossing 3 regions: RoadLayer edit + network update), `LongRouteQuery`
(average over 256 random far pairs, cache off), `CachedRouteQuery`, `PlanRoad200` (RoadPlanner, 200-tile
drag on a hilly 1024² map). Budgets: SC-002 < 2 ms, SC-003 < 1 ms, SC-004 < 2 ms and 0 B. Client: `--road-grid
<spacing>` debug option seeds a full grid on the map for the 60 FPS check (SC-005).

## R12 — Year and type availability

`ConstructionState.Year` is set by `GameSimulation` from `Date.Year` before processing commands (not
hashed; derived from the tick). Road type availability, bridge era limits and the seeder use it.

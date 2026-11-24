# Research: Towns (feature 008)

All Technical Context unknowns are resolved below. Each entry: decision, rationale, alternatives.

## R1 — Where town state lives

- **Decision**: a new `TownRegistry` in `Talt.Core.Towns`, created by `GameSimulation` only when the game
  has construction state (a map in the scenario). Towns are stored structure-of-arrays (centre, pattern,
  name index, counters, monthly statistics) with the town id equal to its slot (towns are never
  removed). Buildings live in a separate `TownBuildings` SoA store with stable ids and swap-with-last
  removal, plus a tile → building id dictionary used only by rare queries (demolition, picking,
  replacement). Ratings are a flat `int[]` indexed `town * companyCapacity + company`.
- **Rationale**: matches `IndustryFleet` and `VehicleFleet`; town id = slot keeps `TileOwner.Town(id)`
  stable, which feature 007 already writes on the seeded streets (site order).
- **Alternatives**: per-town object graphs with building lists (pointer chasing, harder hashing); a
  dense per-tile building-id `int[]` (64 MB on a 4096 map, not justified for rare lookups).

## R2 — Town buildings as tile objects

- **Decision**: add `TileObjectKind.TownBuilding` (value 2) with owner `TileOwner.Town(id)` on every
  footprint tile. All feature 006/007/009 rules that look at `TileObjects` (terraform blocking, road
  planning blockers, industry openings via `IndustryFleet.TileBlocked`) then treat buildings correctly
  without changes. `construction.json` gains `demolishCosts.townBuilding` (per footprint tile).
- **Rationale**: one source of truth for "something stands here"; no new checks in old code.
- **Alternatives**: a private occupancy set like industries (009) — would need hooks in every planner.

## R3 — Founding and initial size

- **Decision**: at creation, after industries and `RoadSeeder.SeedTowns`, every town site becomes a town
  (sites beyond `TileOwner.MaxIndex` are ignored, as in 007). The target population is drawn from the
  size distribution in `towns.json` (villages 100–500 at 70 %, towns 1 000–3 000 at 25 %, cities
  5 000–12 000 at 5 %, weighted up slightly by site score), scaled by the size option (Small 50 %,
  Normal 100 %, Large 200 %). The town is then grown with the normal growth code (R5, R6) until the
  target is reached or an attempt budget runs out, with network updates deferred to one
  `RebuildNetwork` at the end. Founding does not depend on service (growth points are not used);
  it runs growth actions directly until the target is met.
- **Rationale**: one growth algorithm for founding and play means founding exercises and validates it;
  the deferred rebuild keeps creation fast.
- **Alternatives**: hand-made layouts per size (not data-driven, repetitive).

## R4 — Names

- **Decision**: `town-names.json` holds, per style (english, spanish), lists of starts, middles, ends,
  standalone words and pattern weights ("start+end", "start+middle+end", "prefix word + name",
  "name + suffix word"). Every part was invented for TALT. The generator draws from the town's name
  stream; duplicates are redrawn (up to 32 times, then a deterministic numeral suffix). Mixed style picks
  the style per town from the stream (50/50). Names are stored as an index into a string table built at
  founding (strings are not simulation state; their hash is: the table is hashed as UTF-8 bytes).
- **Rationale**: thousands of combinations per style; deterministic; no localization of proper nouns
  (FR-004).
- **Alternatives**: fixed name lists (run out on large maps; risk of copying).

## R5 — Plot search and building placement

- **Decision**: a growth attempt draws a candidate tile from the town's stream within the town radius
  (radius = `radiusBase + sqrt(population) * radiusPerSqrtPopulation / 100`, integer square root),
  biased toward the centre (minimum of two uniform draws per axis). A plot is valid when every footprint
  tile is inside the map, land, has no object, no road piece and no bridge/tunnel passing, is not an
  industry, the footprint's heights differ by at most one step, and at least one footprint tile is
  orthogonally next to a road piece owned by this town. Trees on the plot are removed. The building type
  is chosen by weight among types whose era range contains the year, whose minimum town population is
  met, whose footprint fits at the candidate, and whose zone (centre / inner / outer by distance ÷
  radius) matches the tile. Buildings stand on the highest corner of their footprint (foundation).
- **Rationale**: sampling is O(attempts) per town, independent of town size; centre bias plus zones give
  the denser, taller centre of US5.
- **Alternatives**: scanning every road tile each month (O(area), slower for 500 towns); random walks
  along roads (needs road enumeration per town, more state).

## R6 — Street extension patterns

- **Decision**: each town gets a pattern at founding (grid 60 %, organic 40 %, from data). When a month's
  plot attempts fail more than `failuresBeforeStreet` times, the town extends a street: it samples one of
  its road tiles near the edge of its radius that has a free orthogonal direction, then builds a segment
  outward. Grid towns extend straight along X or Z, with cross streets only on lines at multiples of
  `gridSpacing` from the centre; organic towns build segments of 3–7 tiles that may turn once and branch
  anywhere at least 3 tiles from another junction. Links use the same terrain and ramp rules as
  `RoadSeeder` (height step ≤ 1, ramps straight), refuse water, objects, industries and bridges, use the
  best ordinary road type of the year, are owned by the town, and update `RoadNetwork` through
  `BeforeChange`/`AfterChange` (one batch per town per month).
- **Rationale**: simple, recognisable layouts; reuses 007 rules; incremental network updates.
- **Alternatives**: towns using `RoadPlanner` paths (expensive A*, needs company context and money).

## R7 — Growth rate and service

- **Decision**: service per town = transported ÷ generated passengers+mail last month, in per mille.
  With service 0 a town does not grow (clarification). Otherwise the town adds growth points each month:
  `points += actionsPerMonthAtFullService(size) * service`, where the action count grows with the number of
  buildings (`1 + buildings * growthPerMilleOfBuildings / 1000`, from data); each 1 000 points is one
  growth action (a new building or a replacement). Replacement chance rises in the centre zone of towns
  above `replaceMinPopulation`. Transported amounts come from `RecordTransported(town, cargo, amount)`
  (feature 010's entry point); the debug setting `DebugServicePercent` calls it at month end with that
  share of the month's generated amount before growth is evaluated.
- **Rationale**: deterministic, tunable in data, matches SC-003 (≥ 5 %/year at 60 %).
- **Alternatives**: growth timers per town (harder to tune by size).

## R8 — Monthly update order, parallelism and determinism

- **Decision**: on the month event the registry (1) in parallel over towns (or sequentially, same code)
  rotates statistics, computes generated passengers and mail from per-town production totals kept up to
  date on placement/removal, applies debug service, computes service and growth points, and moves
  ratings toward their targets; (2) sequentially in town id order runs growth actions. Each town has its
  own stream forked from the registry stream by town id, so phase 1 draws nothing shared and phase 2 is
  independent of thread count. `parallel` comes from `GameSimulation.Tick(parallel)`.
- **Rationale**: Constitution III (parallel = sequential, verified by a test); growth touches shared tiles.
- **Alternatives**: see plan Complexity Tracking.

## R9 — Ratings

- **Decision**: ratings range 0–1 000, start 500 (neutral). Bands: hostile < 200 ≤ poor < 400 ≤ neutral
  < 600 ≤ good < 800 ≤ excellent. Penalties (data): demolishing a building −60 per building, removing a
  town road piece −25, removing a tree inside the town zone (radius + margin) −8, capped at −250 per
  action per town. Each month the rating moves toward `500 + service * 400 / 1000` by at most
  `recoveryPerMonth` (20). Demolition and town-road removal require rating ≥ 400 (above poor); station
  building (010) requires ≥ 200. Ratings are stored for every company (only the player exists now).
- **Rationale**: own scale and names; simple to show and test.
- **Alternatives**: unbounded scores; per-action timers.

## R10 — Construction integration

- **Decision**: `ConstructionState.AttachTowns(TownRegistry)`. `PlanClear` treats a town building tile by
  expanding to the whole footprint, checking the rating rule (else non-blocking problem
  `town-refuses`) and adding `demolishCosts.townBuilding × tiles`. `RoadPlanner.PlanRemoval` checks the
  rating rule before the 007 connectivity rule. `ConstructionState.Commit` reports the plan to the
  registry before applying it: buildings whose tiles are cleared are removed from the store, penalties
  are applied per town, and when any building was removed the undo record is cleared after the commit
  (plan Complexity Tracking). Undo never restores ratings. Previews show the rating change in the cost
  line through `ConstructionPlan.RatingChange` (sum over towns for the player, display only).
- **Rationale**: keeps feature 006's one-commit model; rating changes apply with the action (FR-017).
- **Alternatives**: a separate demolish-building command (duplicates clear-area logic).

## R11 — Statistics and acceptance

- **Decision**: each building type has monthly passengers and mail production and goods/food acceptance
  in eighths. The registry keeps per-town totals updated on placement/removal (no per-month building
  loop). Generated = total production (no randomness; deterministic and testable). Acceptance of a cargo
  is true when the town's total eighths ≥ 8. The figures exposed for the window: population, buildings,
  passengers/mail generated and transported last month, accepted cargo list, rating per company.
- **Rationale**: O(towns) per month; 010 will compute catchment acceptance itself from buildings.

## R12 — Rendering

- **Decision**: `TownRenderer` keeps one instance buffer per 128 × 128-tile area (the road renderer's
  areas), rebuilt on the render thread within 2 ms per frame when the area's version changed (Core
  exposes changed building tiles like `RoadLayer.TryTakeChangedTiles`). Each building is two instances:
  walls (shader draws window rows on vertical faces from world position, coloured by category) and a
  roof box (pitched look via a narrower, darker top box for houses; flat slab for apartments, shops and
  offices). Areas beyond the detail distance draw from a second buffer with walls only. Areas outside the
  frustum or fog are skipped. Labels: `TownLabels` projects town centres within fog distance, sorts by
  population, and draws at most 64 non-overlapping name + population plates through the UI draw list.
- **Rationale**: ~100 000 buildings × 2 instances is fine per area with culling; matches 007's area grid.
- **Alternatives**: voxel meshes per building (much more memory, slower updates; left for 018).

## R13 — Picking and window

- **Decision**: `TownPickLayer` walks the tiles under the cursor ray (DDA over the ground grid within
  pick range) and intersects the box of each town building found; label plates are hit-tested in screen
  space first. A pick opens `ObjectInfoWindow` with a town branch (one window per town, key `town:<id>`).
  Road windows show the town name instead of its number.

## R14 — Settings, options and replays

- **Decision**: `ScenarioSettings.Towns` (`TownSettings(NameStyle Mixed, TownSize Normal,
  DebugServicePercent 0)`), validated (0–100). Recorded sessions write a `towns` object only when the
  scenario has a map (map-less sessions and `windows-reference.json` unchanged). Client: new-game screen
  options "Town names" and "Town size", command-line `--town-names`, `--town-size`, `--town-service`;
  headless `run`/`replay` take the same options and print a town summary.

## R15 — Fingerprints and goldens

- **Decision**: `GameSimulation.AppendTo` appends the registry after construction state (only games with
  a map have one). Map-less goldens (`GoldenFingerprintTests`, `ci/golden-fingerprints.json`,
  `ci/sessions/windows-reference.json`) stay unchanged. Map goldens change on purpose because towns found
  buildings at tick 0 and extend streets: `ConstructionReplayTests`, `RoadReplayTests` (and any golden
  that creates a game with a map); `IndustryGoldenFingerprintTests` uses a map without construction and
  must stay unchanged — verified by the test run.

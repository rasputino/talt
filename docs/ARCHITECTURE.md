# Architecture

This document describes how the code is organized today. Update it whenever the structure changes.

## Solution layout

```
Talt.slnx
├── src/
│   ├── Talt.Core/          Game logic. No graphics, no window, no platform code.
│   │   ├── Voxels/         Chunk, ChunkCoord, RegionCoord, VoxelWorld, VoxelMaterial
│   │   ├── Terrain/        ValueNoise, Heightmap
│   │   ├── Construction/   ConstructionState, rules (Data/construction.json), planner, plan, commands
│   │   │                   (terraform, clearArea, undoConstruction), Company, TileObjects, UndoRecord
│   │   ├── Roads/          RoadLayer (tile road pieces), Dir8, RoadRules (+ Data/roads.json), RoadPlanner
│   │   │                   (path, bridge, tunnel, removal, one-way, motorway, connector), RoadSeeder, commands
│   │   ├── Maps/           MapSettings, MapGenerator (+ Stages/), MapData, WorldBuilder (feature 004)
│   │   ├── Content/        Cargo and industry definitions + embedded JSON data (feature 009)
│   │   ├── Industries/     IndustryFleet (SoA state), placement, economy, delivery, read model (009)
│   │   ├── Towns/          TownRegistry (founding, month update, growth, streets, ratings), TownBuildings,
│   │   │                   TownRules, TownNames (+ Data/town-buildings.json, towns.json, town-names.json) (008)
│   │   ├── Stations/       StationRegistry (pieces, stations, warehouse groups, coverage, supply, ratings,
│   │   │                   Load/Unload), StationPlanner, station commands, CargoHold, CargoDelivery,
│   │   │                   StationSeeder, StationRules (+ Data/stations.json) (010)
│   │   ├── Finance/        FinanceSystem (payments, running costs, maintenance, interest, value, bankruptcy,
│   │   │                   inflation), Ledger, PaymentCalculator, RunningCosts, changeLoan command,
│   │   │                   FinanceRules (+ Data/finance.json), MoneyEvent feed (011)
│   │   ├── Meshing/        GreedyMesher, LodVolume, PackedVertex, PackedMesh, FaceDirections,
│   │   │                   MaterialPalette; ChunkMesher/ChunkMesh/VoxelVertex (naive reference)
│   │   ├── Network/        RoadNetwork (incremental road graph), RoadRouter (A* + route cache);
│   │   │                   prototype: TransportNetwork (+ builder), GridPoint, PathFinder (A*),
│   │   │                   GridNetworkGenerator, NetworkTerrainPainter (map-less scenarios only)
│   │   ├── Simulation/     GameSimulation, GameClock/GameDate, SimulationEvents, SimulationRunner,
│   │   │                   VehicleFleet, SyntheticTraffic, PoseInterpolator (render-only)
│   │   ├── Commands/       ISimulationCommand, CommandInbox, Add/RemoveVehicleCommand,
│   │   │                   RecordedSession, SessionRecorder, SessionReplayer, CommandSerializer
│   │   ├── Determinism/    FixedMath, DeterministicRandom (xoshiro256**), StateHasher (XxHash3)
│   │   └── Scenarios/      ScenarioSettings (+ map settings), ScenarioBuilder (shared by client and headless)
│   └── Talt.Client/        Executable: window, input, rendering, interface, runs the simulation.
│       ├── GameWindow.cs   Window lifecycle, screens, input routing, settings, UI pass, title status
│       ├── GameSetup.cs    Builds map + simulation + world + heightmap from NewGameOptions (background task)
│       ├── GameSession.cs  A running game: runner, pose interpolation, vehicle/industry instances, commands
│       ├── PlayerSettings.cs  Persisted settings (language, currency, scale, display, bindings...)
│       ├── DataFiles.cs    Embedded JSON data files with validation errors
│       ├── Ui/             Retained-mode interface: UiContext, widgets, windows, SDF font, icons, theme
│       ├── Screens/        Main menu, new game, in-game HUD and pause menu, settings, key bindings,
│       │                   object information (incl. industries, TownWindow), TownLabels and sample
│       │                   windows; INewGameOptionsSource
│       ├── Input/          KeyBindings (data-driven actions), InputRouter
│       ├── Picking/        Ray, MapPicker and pick layers (terrain, road, vehicle, industry, town)
│       ├── Construction/   ConstructionTool (terrain and road tools), ToolController (preview, drag,
│       │                   confirm), RoadToolOptions, PreviewBoxes, TerrainSync (tile changes → voxels)
│       ├── Localization/   Strings (en/es, plurals), MoneyFormatter and currencies
│       └── Rendering/      WebGpuRenderer, WorldRenderer, UiRenderer, HighlightRenderer, BoxRenderer,
│                           TreeInstances, RoadMesher, RoadRenderer, TownRenderer, TownInstances,
│                           ChunkMeshScheduler, Frustum, LodSelector, RenderStatistics, OrbitCamera,
│                           UnitCube, IndustryInstance, Shaders/*.wgsl
├── tools/
│   ├── Talt.Headless/      Console runner: run and replay simulations without a window
│   └── Talt.ModelGen/      Writes the original .vox models and models.json (feature 018)
├── tests/
│   ├── Talt.Core.Tests/    xUnit tests for Talt.Core (determinism, calendar, runner, commands...)
│   ├── Talt.Client.Tests/  Strings and localization coverage, money, layout, draw list, input
│   │                       routing, windows, tooltips, key bindings, settings, picking, screens,
│   │                       frustum, LOD selection, camera, frame-time tests
│   └── Talt.Headless.Tests/ Command-line parsing tests
└── benchmarks/
    └── Talt.Benchmarks/    BenchmarkDotNet: full tick, vehicle tick, pathfinding, SoA vs Arch ECS,
                            chunk meshing (naive vs greedy, LOD), UI frame cost, picking, industries, towns
```

**Rule:** `Talt.Core` must never reference graphics or windowing libraries. Everything in it must be
testable without a GPU. The client depends on the core, never the other way around.

## Build configuration

- `Directory.Build.props`: shared settings for every project (.NET 10, nullable, warnings as errors,
  recommended analyzers).
- `Directory.Packages.props`: NuGet versions are managed centrally. Projects reference packages
  without a version.

## World model

- The world is made of **voxels**, one byte each (`VoxelMaterial`).
- Voxels are grouped into **chunks** of 32 × 32 × 32, stored as a flat array
  (`index = x + z * 32 + y * 32²`). Y is the vertical axis.
- `VoxelWorld` is a sparse dictionary of chunks. Missing chunks are air.
- `ChunkCoord.FromVoxel` uses floor division, so negative coordinates work.

## Map generation (feature 004)

`Talt.Core.Maps` replaced the prototype `TerrainGenerator`. Design: `specs/004-map-generation/`
(research R1–R13, data-model, contracts). Summary:

- **Settings**: `MapSettings` (width and depth in tiles, multiples of 32, 64 to 16 384 – players 256 and
  up; seed; roughness 0–100; maximum height 8–127; water amount 0–90 %; sea level). Validation errors
  name the setting and its range. `ToText()`/`TryParse` turn a map into one shareable text such as
  `T2-1024x512-s2026-r50-h28-w25-l7`; the leading `T2` is the generator version, bumped whenever the
  same settings would produce a different map. `MapSettingsBuilder` parses the same option names for the
  client and the headless tool.
- **Result**: `MapData` holds five bytes per tile in separate arrays (ground height, terrain type, water
  surface level, flags such as river/lake/cliff/buildable, scenery) plus the reserved `Site`s. Gameplay
  reads it directly; voxels are only for drawing. `Fingerprint()` hashes all of it (determinism tests,
  goldens).
- **Stages** (`MapGenerator.Generate`, progress through `IProgress<MapGenerationProgress>`, cancellable):
  1. *Terrain* – continents, hills (octaves driven by roughness) and ridged mountains from `ValueNoise`
     with domain warping; a monotone histogram remap puts exactly the requested share of tiles under the
     sea and the peak at the maximum height.
  2. *Slopes* – a raster-scan distance transform lowers ground so neighbouring land tiles differ by at
     most one voxel ("one step per tile"), except in rocky high cliff zones (two); sea tiles are never
     touched, so the water share is exact. Tiles still steeper are flagged `Cliff` and not buildable.
  3. *Water* – priority-flood from the sea: lakes (deep depressions at their spill level), levelled
     shallow pits, and a drainage forest where every land tile points to its lowest neighbour. A smooth
     sub-voxel jitter keeps flat ground from draining in straight lines.
  4. *Rivers* – tiles with a large enough catchment become rivers; because every tile drains to the sea
     or a lake, a river cannot dead-end. The water surface never rises downstream and is lowered (or the
     bank raised) so no water floats above a lower bank; the bed is carved one voxel below the water.
  5. *Surface* – water, beach (within 2 tiles of sea or lake and low), rock (cliffs and high rocky
     patches), snow above the snow line (72 % of the land height range, never below sea level + 8), grass;
     coast and buildable flags.
  6. *Scenery* – forest density noise (wetter maps are more wooded) and one tree per tile at most, stored
     as scenery data (kind and variant in one byte). Trees are not drawn yet: the renderer has no
     instanced objects; removable scenery is the design (trees are not terrain voxels).
  7. *Sites* – land is split into connected components; Poisson-disc sampling (Bridson, integer
     arithmetic) places town sites only on the largest landmass and industry sites on components of at
     least 2 000 tiles, on spots whose surrounding square (towns 9 × 9, industries 5 × 5) is buildable and
     nearly flat, spaced by land area (towns about one per 25 000 land tiles, industries one per 12 000;
     minimums 48 town–town and 24 industry–industry). Towns are sampled first; an industry site must lie
     60 to 120 tiles from the nearest town site (`MinTownIndustryDistance`, `MaxTownIndustryDistance`,
     checked by `MapGenerationTuning.Validate`), so every industry belongs to a nearby town that can grow
     to `industryClearancePopulation` (25 000 inhabitants, radius 52, `towns.json`) without reaching it.
     Land with no town in reach (small islands) gets no industry. Industry sites are classified as coast,
     forest, hills or plain. Generator version 2 (the town–industry rule) changed every map.
- **Determinism**: float math is limited to `+ − × ÷`, `MathF.Sqrt`, floor and casts (no `Pow`, `Exp`,
  trigonometry; a test scans the sources); parallel stages split the map into fixed 16-row bands that do
  not depend on core count and sequential stages stay sequential. Tests compare 1, 3 and all threads, and
  golden fingerprints (`MapGoldenTests`) run on every CI platform.
- **World**: `WorldBuilder` creates the chunks (up to four stacked, 127 voxels fit one 128³ region column)
  and fills them in parallel; `Heightmap.FromMapData` builds the camera/vehicle heightmap straight from the
  tile data; `VoxelWorld.GetSurfaceHeight` searches from the top of the highest chunk.
- **Tuning**: thresholds and densities live in `MapGenerationTuning` (changing a default alters maps and
  needs a `GeneratorVersion` bump).

### Screenshots of generated maps (SC-007, T043)

Three 1024×512 maps from the client at generator version `T1` (`-c Release`, whole map in view with
`--zoom 1100`; the grid of roads is the prototype network of the sample simulation, not map data):

| Image | Command line | Shows |
|---|---|---|
| [`004-seed-7.png`](screenshots/004-seed-7.png) | `dotnet run --project src/Talt.Client -c Release -- --map 1024x512 --seed 7 --water 35 --zoom 1100` | sea and bays, beaches, rivers, snowy peaks |
| [`004-seed-11.png`](screenshots/004-seed-11.png) | `... -- --map 1024x512 --seed 11 --water 15 --roughness 80 --max-height 64 --zoom 1100` | rugged mountains with snow, many small lakes |
| [`004-seed-42.png`](screenshots/004-seed-42.png) | `... -- --map 1024x512 --seed 42 --water 45 --roughness 30 --zoom 1100` | large inland sea, gentle hills, rivers |

Project owner's verdict (2026-11-23): approved.

### New game in the client (command line only for now)

`Talt.Client` accepts `--map <N|WxD>`, `--seed`, `--roughness`, `--max-height`, `--water`, `--sea-level`
and `--map-text` (any of them starts a game straight away); the map settings text is printed so the map
can be shared. Progress is printed per stage (`[ 37%] Rivers`). `Talt.Headless map ...` generates without a
window and prints stage timings, counts and the fingerprint (`--build-world` also times the voxel world;
`--threads n` limits workers).

**Follow-up (feature 005's new-game screen exists)**: the screen (`NewGameScreen`, built from
`INewGameOptionsSource` descriptors) still offers only map size, vehicles and seed. Adding roughness,
maximum height, water amount and sea level means new descriptors and a source that fills
`NewGameOptions.Map` (a `MapSettings`); the overview preview of the map before starting can be derived
from `MapData` (terrain, heights, water level) without touching voxels. `GameSetup.Create` already
generates through `MapGenerator` and reports stage progress through its `report` callback. Drawing trees
as instances is deferred to the rendering features.

### Known limits

- The prototype road grid (`GridNetworkGenerator`) is only used by map-less scenarios since feature 007;
  games with a map start with town streets on the reserved town sites (feature 008 grows them).
- Maps with a very large maximum height on a small map are clipped by the slope rule (a 127-voxel peak
  needs at least 127 tiles of run, 64 on cliffs).

## Map generation numbers

Windows, AMD Ryzen/Radeon dev machine, BenchmarkDotNet short job (`MapGenerationBenchmarks`), default
settings, all cores:

| Map | Generate | Build voxel world | Allocated (generate / world) |
|-----|---------:|------------------:|-----------------------------:|
| 256 × 256 | 6 ms | 1 ms | 2.7 / 2.0 MB |
| 1024 × 1024 | 102 ms | 27 ms | 40 / 32 MB |
| 2048 × 2048 | 384 ms | 91 ms | 154 / 129 MB |
| 4096 × 4096 | 2.0 s | 0.32 s | 577 / 515 MB |

Budget: 4096 × 4096 in under 20 s – met with a factor of about 8 (2.3 s in total). Stage shares at 4096
(`Talt.Headless map --map 4096`): terrain 19 %, slopes 4 %, water 45 %, rivers 15 %, surface 5 %,
scenery 2 %, sites 9 %. With `--threads 1` and `--max-height 127` the same map takes 5.0 s, still far
below budget. Client check (`--map 4096 --max-height 127 --stats`): 60 FPS, frame 16.7 ms (worst 19.6 ms),
22 draws, 0.84 M triangles, world memory 176 MB, 0 B allocated per frame.

## Meshing

Feature 003 replaced the naive mesher in the game with `GreedyMesher`
(`specs/003-large-map-rendering/research.md` R1–R5). `ChunkMesher` stays as the reference that the
tests and benchmarks compare against.

### Reference mesher (`ChunkMesher`)

`ChunkMesher` turns a chunk into triangles:

1. Copies the chunk plus a one-voxel border from neighbours into a padded 34³ buffer, so
   neighbour lookups are plain array reads and faces on chunk borders are culled correctly.
2. Emits a quad only where a solid voxel touches air (face culling).
3. Computes per-vertex **ambient occlusion** from the three voxels around each corner and splits
   each quad along its brighter diagonal to avoid artifacts.
4. Applies a deterministic ±5% brightness variation per voxel for the voxel art look.

Vertices are in world space (`VoxelVertex`: position, normal, color = 36 bytes), plus 32-bit indices:
168 bytes per face.

### Greedy mesher (`GreedyMesher`)

- **Same surface, fewer quads.** Faces exist exactly where the reference mesher puts them, with the
  same AO and diagonal rule, except faces pointing down at the bottom of the world (below the world
  is a marker that hides faces without darkening AO). Per face direction and slice, a 2D mask of
  faces keyed by material + four corner AO levels is merged into rectangles, but only where linear
  AO interpolation stays exact (uniform AO: both directions; AO constant along one axis: that axis
  only). Tests check that greedy and naive cover the same unit faces with the same AO, and that the
  naive packed mesh equals the reference mesher face for face.
- **Colour variation in the shader.** The ±5 % per-voxel variation (`MaterialPalette.Variation`) is
  recomputed in `voxel.wgsl` from the voxel under each fragment with the same integer hash, so it
  does not stop faces from merging.
- **Packed vertices.** `PackedVertex` is 8 bytes: position relative to the region origin (8 bits
  per axis), face direction, material, AO level. All quads use the index pattern 0-1-2 / 0-2-3 (the
  mesher rotates the corners to pick the diagonal), so one shared index buffer serves every draw:
  32 bytes per quad instead of 168.
- **Levels of detail.** `LodVolume` downsamples a 128³ region by 2, 4 or 8: a block is solid when at
  least half its voxels are; surface blocks take the material of the real surface, and roads win
  when at least half a block width of columns is road, so the road network stays continuous. Outside
  the region is air, so simplified meshes carry their own side walls ("skirts") that close cracks to
  neighbours drawn at another level.
- **Threading.** Scratch buffers are per thread; meshing only reads the world. Voxel writes inside
  existing chunks while meshing only make a mesh stale (it is rebuilt); creating chunks is not
  allowed while the renderer runs (see `VoxelWorld`).

## Transport network

- `TransportNetwork` is an immutable directed graph. Nodes are junctions or stations with a ground
  position (`GridPoint`, integer units); edges are one-way straight segments whose lengths are
  computed once with an exact integer square root. A two-way road is two edges.
- Adjacency is stored in compressed sparse row (CSR) form: flat arrays, no per-node objects, so
  traversals are cache friendly and allocation free. `TransportNetworkBuilder` assembles it.
- `PathFinder` runs A* with a straight-line heuristic. It reuses its buffers between searches (a
  "visit stamp" avoids clearing them) and is not thread safe: one instance per thread. The heuristic
  of each node is computed once per search and cached (its integer square root was the costliest
  step), and the open set is a small binary heap ordered by (priority, node). Within one search those
  keys are unique, so the expansion order, and therefore every route, is fixed by the algorithm and
  not by the queue implementation.
- `GridNetworkGenerator` builds a synthetic road grid with some roads removed, for tests,
  benchmarks and the prototype. `NetworkTerrainPainter` turns the top voxel under each road into
  road material so the terrain mesher draws it.
- Since feature 007 this immutable network serves only **map-less** scenarios (benchmarks, the CI
  goldens and the windows-reference session, whose fingerprints are unchanged). Games with a map have an
  empty `TransportNetwork` and fleet; their roads are the tile data and incremental graph below.

## Roads (feature 007)

Design: `specs/007-roads/` (clarifications, research R1–R12, data model, contracts).

- **Tile data.** `RoadLayer` (owned by `ConstructionState`, hashed with it) keeps six bytes per tile: link
  mask and exit mask over eight directions (`Dir8`: N, NE, E, SE, S, SW, W, NW — straight and 45° diagonal
  pieces), road type, flags (bridge end, tunnel portal, motorway connector, bridge above / tunnel below
  along X or Z), and for bridge and tunnel ends the long link's direction and span. A one-way link has the
  link bit on both tiles and the exit bit on one. Owners are 006's `TileObjects` (`TileObjectKind.Road`,
  company or town), so terraform and clear rules apply unchanged; the clear tool leaves roads to the removal
  tool. `TileChange` and `TileSnapshot` carry the road piece, so commit and undo restore roads exactly.
- **Rules** (`RoadPlanner`, research R3): straight links climb at most one step; the lower tile is the
  ramp and must be straight; diagonals only between flat tiles at one height whose two cut corners are not
  higher and are not crossed by the other diagonal; bridge and tunnel ends straight. Terraform refuses tiles
  under bridges, above tunnels and corners under diagonal roads.
- **Road tool** (research R4): weighted A* over (tile, arrival direction) states inside the bounding box of
  the two ends plus 48 tiles; costs prefer straight lines, reuse of roads, few turns and ramps; short bridges
  over water are found automatically; when no path exists the first obstacle on the straight line is named
  (too steep, water, industry, motorway...). Buffers are reused: a preview allocates nothing.
- **Structures.** Bridges: straight, ends on land at the same height, deck one step above, anything not
  higher than the ends below (roads keep working underneath), span limited by era (8 / 16 / 32 tiles).
  Tunnels: straight from a hillside to the first tile at the same height, up to 64 tiles. Motorways: two
  one-way carriageways (the line, and its left neighbour for the way back), elevated where the ground cannot
  carry them, continued from existing carriageway ends, joined to other roads only at ends or through exit
  and entry connectors on the outer side. Removal: own pieces; whole bridges and tunnels; town pieces only
  while the town's other pieces stay connected; others skipped. One-way: a straight piece cycles two-way →
  one way → the other way. All costs and limits come from `Roads/Data/roads.json` (validated with field
  names); types: dirt (1850), paved (1920), fast (1960), motorway (1980).
- **Routing graph** (`RoadNetwork`, research R5): junction-contracted. Nodes are road tiles with other than
  two links, a neighbour of another type, or a connector; edges are the chains between them (one per allowed
  direction) with length, speed limit and first direction. Node ids are stable (free list); the out-edges of
  each 64 × 64-tile region are flat CSR arrays owned by the region. After a commit or undo the chains through
  every changed tile and its linked neighbours are walked before and after the change and only the regions
  of their end nodes are rebuilt. `Canonical()` lets tests check that the result equals a full rebuild.
- **Routes** (`RoadRouter`, research R6): A* with an octile heuristic over the contracted graph; ties
  go to the deeper entry, then the node tile, so routes depend only on tile data. A route cache (4 096 entries) is emptied when
  the network's version changes. An exact coarse region graph was rejected (no fewer nodes than the
  junction graph on the reference network and O(portals²) work per region update).
- **Starting roads.** `RoadSeeder` lays a 9 × 9 street grid (three streets each way) on every town site,
  owned by that town, after industries are placed; industry openings skip tiles with objects or bridges.
- **Drawing** (research R10). `RoadMesher` (GPU-free) builds triangles: an octagonal hub plus one arm per
  link, ramps with side wedges, centre or edge markings by type, one-way arrows, bridge decks with railings
  and pillars, tunnel portals, connector acceleration lanes. `RoadRenderer` draws 128 × 128-tile areas: near
  the camera (220 tiles) from detailed 32 × 32-tile block meshes, farther from one simplified mesh (straight
  runs merged); meshes are built within 2 ms per frame, rebuilt only for changed blocks, released after 5 s
  unused, culled by fog and frustum (`roads.wgsl`, lit like the boxes). Voxels are no longer recoloured.
- **Client tools.** Road (R, drag a path), bridge (B, straight line), tunnel (T, click or drag towards the
  hill), road removal (Shift+R, straight or 45° line), one-way (O), motorway (M, straight or 45° line),
  motorway exit (,) and entry (.), road type (Y); a second toolbar column holds them. The road information
  window shows type, speed limit, owner and one-way / bridge / tunnel. Picking uses the road layer. The
  removal tool previews the pieces it takes away in amber (`previewRemove` in theme.json), not in the build
  green, and does not mark neighbours that only lose a link (hand test 2026-11-24).

### Road numbers

BenchmarkDotNet short job, AMD Ryzen 7 160, 2026-11-24 (`RoadNetworkBenchmarks`, `RoadMeshBenchmarks`). The
reference network is a flat 4096 × 4096 map with a two-way road every 16 tiles in both directions (65 536
junctions, about 2 M road tiles); route pairs are at least 1 024 tiles apart.

| Benchmark | Mean | Allocated | Budget |
|---|---|---|---|
| Long route query (A*, no cache) | 1.07 ms | 0 B | < 2 ms (SC-002) |
| Cached route query | 0.001 ms | 0 B | — |
| Incremental update after a 100-tile road (commit + undo record + network), per action | 0.18 ms | 0 B | < 1 ms (SC-003) |
| Full network build, 4096 grid (game creation) | 136 ms | 14 MB once | informational |
| Road tool preview of a 200-tile drag, hilly 1024² map | 0.33 ms | 0 B | < 2 ms (SC-004) |
| Detail mesh of a 32 × 32 block with a road every 8 tiles | 0.17 ms | 0 B | built within 2 ms per frame |
| Simplified mesh of a 128 × 128 area | 0.27 ms | 0 B | built within 2 ms per frame |

The first version used a straight-line heuristic with ties by tile (2.0 ms per long query, right at the
budget) and an unweighted path search (19 ms for the 200-tile preview); the octile heuristic with ties to
the deeper entry, and a ×3/2 weighted path search, gave the numbers above (research R4, R6). The simulation
tick is unchanged: roads only change while commands are processed.

**Hand test (developer, Windows, 2026-11-24,** `--map 1024 --seed 7 --water 35 --start-year 1990 --stats`):
approved ("está superbien"). 60 FPS with 0 B allocated per frame throughout; road meshes 4–13 draws and up to
4 300 triangles near the camera. Requested change: the removal preview was green like building; it is now
amber and marks only the pieces that disappear.

## Simulation loop

`GameSimulation` owns all simulated state and advances it with `Tick()`, the only method that
changes state after creation. Each tick:

1. applies the queued player **commands** in submission order (validate → apply or reject);
2. advances the systems (today, `VehicleFleet`);
3. increments `CurrentTick`;
4. fires **day, month and year events** (in that order) when a boundary was crossed.

- **Time:** 40 ticks per in-game day, 20 ticks per second at normal speed (one day ≈ 2 s).
  `GameClock` derives a Gregorian `GameDate` from the tick counter and the start year (default 1950).
- **Runner:** `SimulationRunner` (in Core, fed with real elapsed time by the client) decides how many
  ticks to run per frame. Standard speeds ×1, ×3 and ×8 multiply ticks per second, never the tick
  size. After a long frame, catch-up is capped at 0.25 s of normal-speed time per frame, so the game
  never freezes. Fast forward runs ticks until a 25 ms budget per frame is spent; it is **locked by
  default**, unlocked through `PlayerSettings`, and toggled with its own key.
- **Threading:** the simulation runs on the main thread; each tick uses worker threads internally
  for vehicle batches. A dedicated simulation thread is deferred until profiling needs it.
- **Smooth motion:** the client snapshots vehicle poses right before the last tick of each frame and
  interpolates previous → current with `PoseInterpolator` (rendering-only floats). Slots whose
  vehicle changed between snapshots are drawn without interpolation.

## Cargo and industries (feature 009)

Industries are the world's freight economy: they produce, accept and process cargo on their own; the
player only transports it. Everything is data-driven (constitution VII).

- **Content** lives in `src/Talt.Core/Content/Data/cargo.json` and `industries.json`, embedded in
  `Talt.Core` and loaded by `GameContent`. Version 1 has 13 cargo types (passengers, mail, coal, wood,
  iron ore, grain, livestock, oil, steel, lumber, fuel, food, goods) and 11 industry kinds (coal and
  iron mines, forest, farm, oil well, power station, sawmill, steel mill, refinery, factory, food
  plant). Validation names the file and the offending id/field and stops the game at startup.
- **Placement**: `IndustryFleet.Create` walks the industry sites reserved by feature 004 and places a
  kind compatible with the site kind and era on about 75 % of them, weighted by the data. A quarter of
  the sites stay free for later openings. Positions and kinds are a pure function of map settings and
  the industry random stream. Each site belongs to the town of its nearest town site
  (`IndustryPlacement.NearestTowns`, ties to the lower index; town ids follow town-site order, feature
  008), and an industry opening on it, at the start or later, takes that town id (`TownIdAt`,
  `IndustryInfo.TownId`, hashed with the other arrays). The client names it with `industry.name`:
  "{town} {kind}" in English, "{kind} de {town}" in Spanish; the town name is never translated.
- **Storage** is structure-of-arrays: scalar arrays per industry plus flat `int[]` tables indexed
  `slot * cargoCount + cargoIndex` for available cargo, pending recipe input, and accepted/shipped
  this and last month. Removal swaps the last industry into the free slot; ids never repeat. No
  per-tick allocation.
- **Schedule**: `GameSimulation` registers `AdvanceDay` and `AdvanceMonth` on the existing day/month
  events. Days accumulate a month's production in whole units (the remainder is added on the month's
  last day); months rotate statistics, apply seeded drift, growth from deliveries, decline after 24
  months without deliveries, seeded closures after about five years and seeded openings on free sites.
  Production beyond a cargo's storage cap is discarded.
- **Processing** is immediate and automatic: accepted cargo with a recipe is converted in whole
  batches (`inputAmount` → `outputAmount`); a partial batch waits as pending input and processing
  waits if the output store is full. Accepted cargo without a recipe is consumed (power station coal).
  `Deliver` and `Take` are the public API that feature 010 will call.
- **Replay and fingerprints**: `ScenarioSettings.Map` is optional and recorded as a `"map"` text;
  replay regenerates the same map and industries. `IndustryFleet.AppendTo` writes nothing when the
  fleet is empty, so map-less scenarios keep their pre-009 fingerprints; with industries it hashes all
  arrays, the forked random stream, free sites, the content fingerprint and the next id.
- **Client**: data-driven placeholder boxes are drawn with an instanced pipeline
  (`industries.wgsl`); instances rebuild only when `IndustryFleet.Version` changes. An
  `IndustryPickLayer` (priority 75) picks industries, and the information window shows production,
  acceptance, recipes, waiting/pending cargo, cargo properties and last month's delivered/shipped
  amounts. Final models are feature 018.

Measured on the development machine (AMD Ryzen 7 160, 8 cores / 16 threads, 4096 × 4096 map, about
1 000 industries, `IndustryBenchmarks`, short job, 2026-11-23):

| Benchmark | Mean | Budget |
|---|---|---|
| Day update, all industries | 20.3 µs | < 1 ms (SC-001) |
| Month update, all industries | 51.7 µs | < 5 ms (SC-002) |
| Initial placement on the 4096 map | 424 µs | informational (1.4 MB allocated once) |

Both updates report no managed allocation per operation.

## Towns (feature 008)

Towns are the source and destination of passengers and mail and accept goods and food. Spec and design:
`specs/008-towns/` (research R1–R15).

- **State**: `TownRegistry` (namespace `Talt.Core.Towns`) is created by `GameSimulation` only for games with a
  map, after industries and `RoadSeeder.SeedTowns`, and attached to `ConstructionState.Towns`. Towns are
  structure-of-arrays with town id = slot = `TileOwner.Town(id).Index` (site order; towns are never removed):
  centre, street pattern (grid/organic), name, population, building count, per-month production and acceptance
  totals, growth points, last month's generated/transported passengers and mail, service per mille, and a
  flat rating table `town × company` (0–1 000, start 500). Buildings live in `TownBuildings` (SoA, stable ids,
  swap-with-last removal, tile → id dictionary for rare queries). Every footprint tile is a tile object
  `TileObjectKind.TownBuilding` owned by the town, so the feature 006/007/009 rules (terraform and road
  blocking, industry openings) treat buildings without new checks.
- **Data** (constitution VII): `Towns/Data/town-buildings.json` (14 original types in 5 categories: footprint
  1–2 tiles, storeys, residents, passengers/mail per month, goods/food acceptance in eighths, era, minimum
  town population, zones, weight, level, procedural model), `towns.json` (growth, starting size distribution
  and Small/Normal/Large scale, ratings) and `town-names.json` (original English- and Spanish-style name
  parts). `TownRules` and `TownNames` validate everything at load and name the file and field.
- **Founding**: each town gets a unique generated name (style from `TownSettings.NameStyle`: mixed, English
  or Spanish), a pattern and a target population (70 % villages 100–500, 25 % towns 1 000–3 000, 5 % cities
  5 000–12 000, scaled 50/100/200 % by the size option), and is grown to it with the same growth code used
  later; network updates are deferred to one `RebuildNetwork`.
- **Growth** (month event, `AdvanceMonth(date, parallel)`): phase 1 runs per town, in parallel when allowed (it
  touches only that town's slots): rotate statistics, apply the debug service share, compute service =
  transported ÷ generated, move ratings toward `500 + service × 400 / 1000` by at most 20, add growth points
  (`(1 + buildings × 30‰) × service`). Without service a town does not grow (clarification). Phase 2 runs
  sequentially in town-id order: each 1 000 points is one action, either a replacement by a higher-level
  building of the same footprint in larger towns, or a new building on a sampled plot (centre-biased; zones
  centre/inner/outer by distance ÷ radius, with taller types weighted up in the centre; plots must be free
  buildable land within one height step, next to a road of the town; grid lines and dead-end continuations
  stay free for streets). When plots run out the town lays a street segment (grid: along its grid lines;
  organic: 3–7 tiles, maybe one turn, no parallel neighbours) with the feature 007 ramp rules, owned by the
  town, possibly connecting to another road. Street changes only mark the road network
  (`RoadNetwork.MarkChanged`); the touched regions are rebuilt once per month (`RebuildChanged`). Each town
  draws from its own stream forked by town id, so parallel and sequential runs are identical.
- **Ratings and construction**: the clear tool demolishes a whole town building (cost
  `demolishCosts.townBuilding` per footprint tile) and road removal takes town pieces, only while the
  company's rating is at least 400 (above "poor"); otherwise `town-refuses`. `ConstructionState.Commit` hands
  the plan to `TownRegistry.ApplyConstruction` first: demolished buildings leave the store and penalties
  apply (building −60, town road −25, tree in a town zone −8, at most −250 per action and town). Actions that
  remove town buildings clear the undo record; undo never restores ratings. Previews show the rating change
  (`ConstructionPlan.RatingChange`). `MayBuildStation` (rating ≥ 200), `RecordTransported` and `Accepts` are
  feature 010's entry points.
- **Settings and replays**: `ScenarioSettings.Towns` (`TownSettings`); recorded sessions write a `towns` object
  only for map games. Client options `--town-names`, `--town-size`, `--town-service` and new-game choosers
  "Town names" and "Town size"; the headless runner takes the same options and prints a town summary.
- **Fingerprint**: `GameSimulation` hashes the registry (settings, rules fingerprint, all arrays, names,
  streams, buildings) after construction state, only when it exists: map-less goldens are unchanged; the map
  goldens of `ConstructionReplayTests` and `RoadReplayTests` changed on purpose.
- **Client**: `TownRenderer` keeps one instance buffer per 128 × 128-tile area (walls first, roofs after),
  rebuilt within 2 ms per frame only when visible and changed (`TownBuildings.TrackChanges` →
  `TryTakeChangedTiles`); fog and frustum culling; areas farther than 320 tiles draw walls only.
  `towns.wgsl` draws window rows per storey from the world position. `TownLabels` projects town centres and
  draws up to 64 non-overlapping name/population plates, largest towns first (texts cached per population).
  `TownPickLayer` walks the ray's ground track over building boxes; a plate or a building opens the town
  window (`TownWindow`: population, buildings, passengers/mail generated and transported, acceptance, rating,
  growth).

### Town numbers

`TownBenchmarks` (2026-11-24, AMD Ryzen 7 160, 8 cores / 16 threads; one invocation per iteration and 10
iterations, so each measured month follows the previous one) on a 4096 × 4096 map with 534 towns and
49 121 buildings at founding:

| Benchmark | Mean | Allocated | Budget |
|---|---|---|---|
| `MonthlyUpdateServed` (every town at 100 % service: growth, streets, network) | 15.5 ms | 74 KB | < 50 ms (SC-002) |
| `MonthlyUpdateUnserved` (statistics and ratings only) | 95 µs | 2.8 KB | informational |
| `CreateGameWithTowns` (industries, streets, founding 534 towns, road network) | 153 ms | 162 MB once | informational |

The served month allocates only when building storage, the tile → building dictionary or network regions
grow; the unserved month's 2.8 KB is `Parallel.For` bookkeeping. Towns run only on month boundaries, so the
tick budget is unchanged. Headless, 1024 × 1024 map, 44 towns, ten years at 60 % service: population
58 296 → 172 969 with a 0.003 ms average tick.

## Stations and cargo handling (feature 010)

Stations collect cargo from their surroundings, keep it waiting and hand it to vehicles; delivered cargo goes to
the consumers around them. Spec and design: `specs/010-stations-cargo/` (research R1–R14).

- **State**: `StationRegistry` (namespace `Talt.Core.Stations`) is created by `GameSimulation` for games with a
  map, after towns, and attached to `ConstructionState.Stations`. Stations (road stations and warehouses share
  one id space) are structure-of-arrays by slot with stable ids and swap-with-last removal: owner, kind,
  bounding box, name parts (town, kind, place, number) or a custom name, handled cargo categories, catchment
  radius and group. Station tiles are in a tile → piece dictionary (type, direction). Groups (research R4) are
  connected components of warehouses and the road stations with a piece inside their influence square
  (warehouses link when their squares overlap); without warehouses every station is its own group (fast path,
  appended on creation). Per group × cargo flat tables hold the served flag, rating (0–1 000), waiting total,
  FIFO packet list, last pickup day and speed and this/last month's received, picked-up, delivered and lost
  figures. Waiting cargo is a pool of aggregated packets (amount, origin station, source town, destination
  — "any" until feature 013 — and pickup day) with a free list; nothing is stored per unit.
- **Placement** (`StationPlanner`, also used by the tool previews): a *bay* is an empty, flat, dry tile beside a
  road (first of N, E, S, W at the same height) and becomes `TileObjectKind.Station` with a one-link road piece
  plus the reverse link on the road, so the routing graph gets a dead-end node vehicles (012) can reach; a
  *drive-through* stop sits on a straight two-link road tile of the company or of a town that allows stations,
  and leaves the road untouched (one unchanged `TileChange`). Pieces touching one company station join it (two
  → `adjacent-two-stations`; bounding box > 12 → `station-too-spread`). Warehouses (2×2, 3×3, 4×4) need flat
  empty land touching a road. Inside a town zone the town's rating must allow stations (`town-refuses`). The
  road under a drive-through stop and the link into a bay are protected (`RoadPlanner` refuses with
  `station-road`; town streets skip station tiles); the clear tool skips stations (`use-station-tool`). Station
  commands (`buildStation`, `buildWarehouse`, `removeStation`; `renameStation` is a plain command) record their
  pieces in the registry through `ConstructionCommand.Committed` and clear the undo record.
- **Coverage** (derived, never hashed; research R5): per station the industries and town buildings with a tile
  in its catchment (union of the pieces' squares, radius 3 bus / 4 truck), acceptance in eighths (industry 8;
  buildings: goods/food from `town-buildings.json`, passengers and mail from their production), the received
  and accepted cargo masks (only handled categories), and a CSR producer table (producer → covering stations).
  It is rebuilt at the start of a day only when construction, industry openings/closings (`IndustryFleet.Version`,
  through a 16-tile station grid) or town building changes (`TownBuildings.StationWatch`, only the stations whose
  catchment holds a changed tile) made it stale.
- **Daily flow** (research R6, R7, R14): `BeginDay` (coverage, debug served marks) → `IndustryFleet.AdvanceDay(date,
  sink)` offers each daily amount to the registry (`IProductionSink`) before the industry's stock → town
  buildings' passengers and mail (spread evenly over the month, exact) → for each producer and cargo the moved
  amount is `(amount × best rating + carry) / 1000` with the remainder carried per producer, split among the two
  best serving groups by rating; the waiting cap (4 000) loses the excess and penalises the rating → ratings move
  toward `base + pickup-days points + last-speed points + waiting points` by at most 10 a day (per group, in
  parallel above 1 024 groups) → neglected cargo (rating < 350, waiting > 200) is lost → the debug service
  (`--station-service`) collects its share every five days. Monthly: statistics rotate. No randomness.
- **Vehicle API** (feature 012) and **payments** (feature 011): `MarkServed`, `Load(station, cargo, max, hold,
  speedKmh)` (oldest first; counts passengers and mail as transported for their town), `Unload(station, hold,
  Normal | Transfer)` (accepted cargo to industries via `IndustryFleet.Deliver` and to towns, never at its origin
  group; one `CargoDelivery` per consumer part to `IDeliveryListener`; the rest stays on board or waits with its
  origin and pickup day). Conservation is tested over 1 000 days of random operations.
- **Fingerprint**: hashed after towns, only once a station was ever built, so every earlier golden (map-less and
  map) is unchanged. Data: `Stations/Data/stations.json` (`StationRules`, validated, fingerprinted).
- **Client**: a third toolbar column (bus stop, truck stop, warehouse with a size option, remove station); the
  preview adds the catchment tiles (`PreviewBoxes.AppendArea`) and "Receives … / Accepts …" to the cursor label;
  placeholder boxes (`StationPlaceholders`) are appended to the industry placeholder instances (no new renderer;
  feature 018 replaces them); `StationPickLayer` (priority 80) and `StationWindow` (cargo table, catchment toggle
  through `GameSession.ToggleCatchment`, rename field).

### Station numbers

`StationBenchmarks` (2026-11-25, AMD Ryzen 7 160, 8 cores / 16 threads, default job) on a 4096 × 4096 map with
5 000 bays seeded beside industries and town roads (`StationSeeder`), every cargo served and the debug service
at 50 %, after a month of play; the grouped game adds 130 small warehouses (5 130 stations in 4 343 groups):

| Benchmark | Mean | Allocated | Budget |
|---|---|---|---|
| `DailyUpdate` (coverage check, industries through the sink, town passengers/mail, ratings, losses) | 0.40 ms | 3.3 KB | < 2 ms (SC-001) |
| `DailyUpdateWithGroups` | 0.33 ms | 3.2 KB | < 2 ms (SC-001) |
| `MonthlyUpdate` (statistics rotation) | 35 µs | 0 B | < 2 ms (SC-001) |
| `CatchmentPreview` (one bus bay, cursor moved to another tile) | 3.9 µs | 0 B | same frame (SC-002) |

The daily allocation is `Parallel.For` bookkeeping of the rating phase (above 1 024 groups); the sequential path
allocates nothing (`StationAllocationTests`). A first version allocated 417 KB a day: `foreach` over the rating
tables' `IReadOnlyList` boxed an enumerator per cell; the tables are arrays now. Stations run only on day and
month boundaries, so the tick budget is unchanged. Headless, 1024 × 1024 map, 200 seeded stations, 60 %
debug service, 4 000 ticks: 24 144 units supplied, 22 392 collected, 1 752 waiting, none lost; 0.05 ms average
tick.

## Economy and finance (feature 011)

Money, loans, recurring costs, delivery income and the ledger. Spec and design: `specs/011-economy-finance/`
(research R1–R16).

- **Money**: whole internal units (1 = one US dollar) in `long`; products that can overflow (payment, inflation,
  value) use one `Int128` intermediate and saturate at ±2⁶² (`Money`). No `float`, `double` or `decimal`.
- **State**: `FinanceSystem` (namespace `Talt.Core.Finance`) is created by `GameSimulation` for games with a map,
  after the stations, and becomes their `DeliveryListener`. `Company` (feature 006) is the account: cash (the old
  `Balance`), `Loan`, a `Ledger`, `Value`, `BankruptMonths`, `IsGameOver` and three accrual remainders. Every change
  of cash goes through `Company.Book(category, amount, cargo)` (construction's `Spend`/`Refund` book
  `construction`); loan movements change cash and loan only. `Ledger` keeps one signed total per slot (six fixed
  categories, then one income slot per cargo) for the last 24 months (ring) and for every game year, so the ledger
  always equals the change of cash, loan movements apart (property-tested over 30 months).
- **Payments** (research R2, R3, R12): `PaymentCalculator.Quote` = `floor(rate × units × distance × timeFactor‰ ×
  paymentLevel / 10¹²)`; `rate` and `freeDays` per cargo in `finance.json`, decay and minimum share from
  `cargo.json`; distance is the Manhattan distance between the origin and destination stations' centres (removed
  stations keep their centre in `StationRegistry`, hashed once one is removed), at least `minDistance`. The debug
  service reports its pickups as `DeliveryConsumer.DebugService`, paid over `debug.serviceDistance` only with
  `--station-service-pays`. Each payment books `income:<cargo>` and writes a `MoneyEvent` to a fixed 512-entry
  feed (not state) that the client drains into floating texts.
- **Costs** (research R4, R5): daily, the running costs registered in `RunningCosts` (feature 012 registers its
  vehicles with a yearly cost and a value) are charged as `(yearly × costLevel + carry) / daysInYear`; at each month
  end, property maintenance (owned road tiles by road type, station pieces by type, warehouses by size, yearly
  values in `finance.json`) and loan interest (`loan × perMille / 12 000`) are charged with their remainders
  carried, so twelve months sum exactly to the yearly figure. Owned road tiles are counted incrementally by
  `ConstructionState` around every tile mutation path (`Commit`, undo, town roads and objects, initial roads) and
  tested against a full scan; station property is counted from the registry (O(stations)). Recurring costs may
  take cash below zero; construction still needs the cash.
- **Month end** (order): maintenance → interest → inflation → company value (cash − loan + property at
  `propertyValuePercent` of its build cost × cost level + vehicle values) → bankruptcy (negative cash with the
  maximum loan: 3 months → warning event, 6 → game over unless sandbox) → ledger month rotation. Year end (after
  the month handlers): ledger year rotation. Handlers run after the stations', so a day's deliveries and debug
  pickups book into the period that just ended.
- **Inflation** (research R7, new-game option, off by default): cost and payment levels in parts per million grow
  each month by `level × basisPoints / 120 000` for `inflation.years`; the cost level scales construction (applied
  once per plan in `ConstructionCommand.Check`, so previews and commands agree), maintenance, running costs, the
  loan step and the maximum loan (rounded down to whole steps); the payment level scales payments.
- **Infinite money** (research R17): `Company.HasInfiniteMoney` / `InfiniteMoneySinceTick`, set by
  `FinanceSettings.InfiniteMoney` at creation or the recorded `enableInfiniteMoney` command, never cleared and hashed.
  `Company.Book` then updates only the ledger (cash frozen, figures still listed), `ConstructionCommand.Check` skips
  the money check, `changeLoan` is refused (`infinite-money`) and bankruptcy never counts.
  `FinanceSystem.UsedInfiniteMoney` is the flag for goals and scores (feature 020). The client asks for confirmation
  in an overlay before sending the command and shows "∞" in the HUD.
- **Commands**: `changeLoan` (`borrow`, `repay`, `borrowMax`, `repayMax`; reasons `max-loan`, `no-loan`,
  `not-enough-cash`). After game over `GameSimulation` refuses every command with `game-over`.
- **Settings**: `ScenarioSettings.Finance` (`FinanceSettings`: sandbox, inflation, debug paid service), written to
  recorded sessions as `settings.finance` only when not default.
- **Fingerprint** (research R11): hashed after the stations, only once something was borrowed, paid, registered
  or bankrupt, the settings are not default, or a ledger holds a non-zero total (an action undone in the same
  month leaves none). Map-less goldens, untouched map games and the industry/town goldens are unchanged; the
  construction, road and station replay goldens changed on purpose (their ledgers are hashed). New golden:
  `FinanceReplayTests`.
- **Data**: `Finance/Data/finance.json` (`FinanceRules`, strict, checked against the cargo, road and station ids,
  fingerprinted). Starting cash stays `construction.json`'s `startingBalance` (500 000).
- **Client**: the HUD coin (or `F2`) opens `FinanceWindow` (table by category for the current and two previous years
  with totals, cash/loan/limits/interest/value line, bankruptcy line, borrow/repay buttons, a `BarChart` of the last
  24 months); negative cash is drawn in the theme's `negative` colour; `MoneyTexts` shows "+amount" above the
  unloading station for 2.5 s (projected like the town plates, at most 64); a bankruptcy warning message; a
  game-over overlay with the final figures. New-game options: sandbox, inflation and infinite money. Headless: `--sandbox`,
  `--inflation`, `--station-service-pays` and a finance summary.

### Finance numbers

`FinanceBenchmarks` (2026-11-25, AMD Ryzen 7 160, 8 cores / 16 threads) on a 4096 × 4096 map with 5 000 bays
seeded beside industries and town roads, the maximum loan, inflation on and 50 000 registered running costs:

| Benchmark | Mean | Allocated | Budget |
|---|---|---|---|
| `DailyUpdate` (running costs of 50 000 registrations: one total per company) | 13 ns | 0 B | < 1 ms (SC-003) |
| `MonthlyUpdate` (maintenance from 5 000 stations and the road counts, interest, inflation, value, bankruptcy, ledger rotation) | 0.14 ms | 0 B | < 1 ms (SC-003) |
| `PayDelivery` (distance, time factor, ledger, money event) | 36 ns | 0 B | — |

`FinanceAllocationTests` checks the same paths with `AllocationAssert`; a first version allocated 120 B a month
end (`foreach` over `IReadOnlyList<Company>` boxed its enumerator). Finance runs only on deliveries and day/month
boundaries, so the tick budget is unchanged. Headless, 512 × 512 map, 40 seeded stations, `--station-service 60
--station-service-pays`, 20 000 ticks: 2 542 deliveries paid, income 2 089 504, expenses −91 480 (construction
and maintenance), the same fingerprint on two runs.

## Determinism

The same settings and the same commands produce the same state at every tick, on every platform.

- **No floating point in simulation state** (`FixedMath`): distances in units (1 tile = 65 536),
  speeds in units per tick, accelerations in units per tick². A reflection test fails if a `float`,
  `double` or vector field appears in simulation types. Floats are allowed only for rendering and
  for one-off conversion of constants.
- **Randomness:** `DeterministicRandom` (xoshiro256**, seeded with SplitMix64). Systems use forked
  streams; the UI never uses the simulation's generator.
- **Commands:** player actions are `ISimulationCommand` records applied at the start of the next
  tick (or at once while paused, feature 006). Commands: `AddVehicleCommand`, `RemoveVehicleCommand`,
  `TerraformCommand`, `ClearAreaCommand`, `UndoConstructionCommand`, and the road commands of feature 007
  (`buildRoad`, `buildBridge`, `buildTunnel`, `removeRoad`, `setOneWay`, `buildMotorway`,
  `buildMotorwayConnector`).
- **Fingerprint:** `GameSimulation.ComputeFingerprint()` hashes the whole state with XxHash3. An
  industry fleet with no industries contributes nothing, so a map-less game keeps the fingerprints it
  had before feature 009; a populated fleet hashes its arrays, random stream and content fingerprint.
  Construction state exists only when the scenario has map settings, and is hashed after the industries.
- **Recorded sessions:** `SessionRecorder` writes settings + commands + checkpoints as JSON
  (`.talt-session.json`); `SessionReplayer` replays them and reports the first diverging tick. Sessions
  with map settings write a `"map"` text; replay regenerates the map and its industries.
- **Golden fingerprints:** `GoldenFingerprintTests` pins the fingerprints of a fixed scenario,
  generated on Windows x64. Linux and macOS must produce exactly the same values. Regenerate only
  when simulation rules change on purpose and say so in the commit message.

## Construction and terraforming (feature 006)

Design: `specs/006-construction-framework/` (research R1–R14, data model, contracts).

- **The simulation owns the map.** `ScenarioSettings.Map` (optional `MapSettings`) gives a game a map;
  `GameSimulation.Create(settings, map)` takes the map the client generated (with progress) or generates
  it (headless replay). `GameSimulation.Construction` (`ConstructionState`) holds the map's tile data,
  a dense object/owner layer (`TileObjects`: kind byte + `TileOwner` ushort per tile; road
  pieces are `Road` objects owned by a company or a town, feature 007), the road layer, the companies (only the player's for now, balance in whole
  US dollars; the full account arrives with feature 011) and the one-level `UndoRecord`. It is hashed into the fingerprint only when a map exists,
  so map-less scenarios (CI goldens, benchmarks) kept their fingerprints. Recorded sessions carry the map
  as its shareable text (`settings.map`, format version unchanged).
- **One planner for preview and command.** `ConstructionPlanner` fills a reusable `ConstructionPlan`
  (changed tiles with before/after, problem tiles with reason codes, total cost). `ConstructionCommand`
  (base of `TerraformCommand`, `ClearAreaCommand`) validates by planning and then `Check` (reason, money);
  `Apply` commits the plan validated in the same tick. The client's `ToolController` plans with its own
  planner and the same `Check`, so the preview's cost and reason always match what applying does. New
  building features subclass `ConstructionCommand` and add a `ConstructionTool`.
- **Terraform rules.** Raise/lower move every area tile one step; level fixes the area at the start
  tile's height. A monotone cascade (raise only raises, lower only lowers; level alternates raise and
  lower passes, then verifies) keeps neighbouring land tiles within one voxel wherever a tile changed;
  untouched cliffs stay. Water tiles never change; land may not end below neighbouring water; tiles under
  any object block (own objects: demolish first; others': owned by someone else); heights 1–`maxHeight`.
  Trees on changed tiles are removed and paid; Rock becomes Grass where dug; Cliff/Buildable flags are
  recomputed around the changes. Terraform actions are all-or-nothing; the clear tool removes trees and
  the company's objects and leaves others' objects standing (shown orange).
- **Costs and limits** come from `construction.json` (starting balance 500 000, 250 per tile per step,
  40 per tree, demolish costs by object kind, undo window 200 ticks, max height 120, 16 384 tiles per
  action), validated at load with the field's name in the error.
- **Undo** stores every touched tile's before/after values; undo is valid within the window if every
  tile still equals its after-values, restores exactly and refunds. Any later action replaces the record.
- **While paused** `GameSession` calls `GameSimulation.ProcessPendingCommands()`: commands apply at once
  without advancing time; they are recorded at the current tick and replay identically.
- **Drawing.** `TerrainSync` drains the changed tiles after commands, rewrites their voxel columns with
  `WorldBuilder.RebuildColumn` (creating the chunk above when needed), refreshes the heightmap and
  notifies the renderer. `BoxRenderer` draws instanced boxes: trees (opaque, `TreeInstances` gathers up
  to 48 000 trees within 160 tiles of the camera target, rebuilt every 16 tiles of movement or when
  scenery changes) and the translucent ghost preview (`PreviewBoxes`: green, red where blocked, orange
  where skipped). A label next to the cursor shows the cost in the display currency and the reason.
- **Picking fix.** The terrain ray cast was limited to the first 32 voxels of height, so hills above
  could not be picked; it now covers the world's highest chunk.

### Construction numbers

BenchmarkDotNet short job, AMD Ryzen 7 160, 2026-11-23 (`ConstructionBenchmarks`, hilly 1024² map,
a 25 × 20 drag = 500 tiles, planning as the preview does on every new cursor tile):

| Benchmark | Time | Allocated |
|---|---|---|
| Level preview (with cascade around the area) | 0.081 ms | 0 B |
| Raise preview | 0.029 ms | 0 B |
| Clear preview | 0.001 ms | 0 B |

SC-001's budget (a 500-tile preview under 1 ms) holds with a factor of about 12; a unit test checks
1 000 repeated previews through `ToolController` allocate nothing (SC-007).

**Hand test (developer, Windows, 2026-11-23,** `--map 1024 --seed 11 --roughness 80 --max-height 64 --stats`):
raise, lower, level, clear, cancel, undo and building while paused all work. From the log: 17 edits on
screen after 23–33 ms (average 31 ms; SC-005 asks < 100 ms), 0 B allocated per frame and 60 FPS in every
one-second sample while building. Found and fixed, each with a regression test: after cancelling, the
preview marks (on roads) stayed on screen, because the controller did not report a preview that
disappeared; and the player asked to see the whole dragged area, so tiles of the rectangle that do not
change are now drawn in a neutral colour; on the retest, the "Not possible: …" notifications were hard to
read straight over bright terrain, so notifications now sit on a panel. Open: about 3 s at 6 FPS shortly after the game started, before
any tool was used, with every render phase under 2 ms (the time went outside the measured phases, as in
feature 003's long frames caused by other processes); not seen again in the session.

Meshing after the concurrent chunk dictionary (`ChunkMeshingBenchmarks`, short job, same day): greedy
full detail 0.62–0.65 ms per chunk, region LOD 1/2/3 0.23/0.11/0.10 ms — the feature 003 numbers
(0.62/0.22/0.12/0.10 ms). To keep it so, `GreedyMesher` now looks the 26 neighbour chunks up once per
chunk instead of once per border voxel (output unchanged; the greedy-vs-naive tests cover it). A same-session
"before" run could not be made: BenchmarkDotNet again failed to build from a temporary worktree.

## Headless runner

`tools/Talt.Headless` references only `Talt.Core`:

```
dotnet run -c Release --project tools/Talt.Headless -- run --map 4096 --vehicles 50000 --ticks 10000
dotnet run -c Release --project tools/Talt.Headless -- run --ticks 10000 --record session.json
dotnet run -c Release --project tools/Talt.Headless -- run --ticks 4000 --map-text T2-512x512-s7-r50-h28-w25-l7
dotnet run -c Release --project tools/Talt.Headless -- replay session.json --checkpoint-every 1000
```

`--map-text` generates the map and places its industries before ticking; without it a run has no
industries (and keeps the pre-009 fingerprints). The summary includes `industries: <count>`.

It prints stable `key: value` lines (`date`, `fingerprint`, `avg-tick-ms`, ...). Exit codes: 0 success,
1 determinism failure, 2 invalid arguments or file, 3 unexpected error.

## Vehicle simulation

`VehicleFleet` simulates vehicles running on **lines**. A line is a closed loop of edges with two
stops: the start of the route and the start of the way back. Vehicles accelerate, cruise, brake so
they stop exactly at the next stop (the remaining distance to each stop is precomputed per line
edge), wait three seconds and continue.

Design choices for performance:

- **Structure of arrays:** one integer array per field (id, line, route index, distance, speed,
  max speed, dwell ticks) instead of one object per vehicle. Vehicles have stable ids; removal moves
  the last vehicle into the free slot.
- **No allocations per tick:** verified by a unit test and by the benchmark's memory diagnoser
  (only `Parallel.For` itself allocates a few kilobytes per tick, independent of vehicle count).
- **Parallel batches:** vehicles are updated in batches of 4096 with `Parallel.For`. Parallel and
  sequential ticks give identical results (unit test).

**Not modeled yet:** vehicles do not interact. They overlap on roads, ignore each other at
junctions and there are no signals or blocks. Interactions are the next big step and the main
threat to parallelism, because neighbouring vehicles depend on each other.

`SyntheticTraffic` creates random lines between junctions and spreads vehicles evenly along them.
Pairs are drawn in batches no larger than the number of lines still missing, so no batch draws a pair
the sequential loop would not have drawn and the random state afterwards is the same. The routes of
a batch are searched in parallel (one `PathFinder` per worker, each route written to its own slot)
and accepted in attempt order, so the fleet is identical with `parallel: false` (unit test).

## Benchmarks

`benchmarks/Talt.Benchmarks` uses BenchmarkDotNet on a 4096 × 4096 tile network
(257 × 257 junctions, one every 16 tiles, ~220 000 one-way edges, 2 000 lines).

```
dotnet run -c Release --project benchmarks/Talt.Benchmarks -- --filter * --job short
```

Results on the development machine (AMD Ryzen 7 160, 8 cores / 16 threads), feature 001:

| Benchmark (short job) | Sequential | Parallel | Before 001 (parallel) |
|---|---|---|---|
| Full `GameSimulation.Tick`, 10 000 vehicles | 0.16 ms | 0.09 ms | — |
| Full `GameSimulation.Tick`, 50 000 vehicles (reference load) | 0.96 ms | 0.19 ms | — |
| Full `GameSimulation.Tick`, 100 000 vehicles | 2.8 ms | 0.33 ms | 0.35 ms (vehicles only) |
| Full `GameSimulation.Tick`, 1 000 000 vehicles | 23 ms | 5.5 ms | 6.0 ms (vehicles only) |
| A* across the whole map (random pairs) | 1.27 ms per search | — | — |

Scenario setup speed-up (same machine, 2026-11-22). `ScenarioSetupBenchmarks` builds
the reference network and its 5 000 lines (10 000 A* routes) and 50 000 vehicles:

| Measurement | Before | After |
|---|---|---|
| `PathFinderBenchmarks` A* across the whole map (in-process job) | 2.49 ms per search | 1.68 ms per search |
| `ScenarioSetupBenchmarks.CreateSyntheticTraffic`, sequential | — | 22.6 s |
| `ScenarioSetupBenchmarks.CreateSyntheticTraffic`, parallel | — | 2.0 s (92 MB allocated) |
| `ScenarioSetupBenchmarks.GenerateNetwork` | — | 52 ms |
| Headless reference run, `setup-ms` | 38.6 s and 56.7 s (two runs) | 2.4–2.7 s |

Fingerprints are unchanged (`C0C656C5AB2D3302`, `C9F573FD7DE5CBE3`; `ci/check-fingerprints.ps1`
passes, replay included). Timings on this laptop vary a lot between runs (the same A* benchmark gave
1.27 ms in feature 001), so compare only numbers measured in the same session. Before and after the
change were measured back to back. The "before" setup benchmark could not run: BenchmarkDotNet
failed to build its boilerplate from a temporary folder, so the headless `setup-ms` is the "before"
reference.

No regression against the previous float prototype (constitution II): the integer simulation is as
fast or faster. The full tick allocates only what `Parallel.For` itself needs (~2–5 KB per tick,
independent of vehicle count).

**Headless reference run:** `Talt.Headless run --map 4096 --vehicles 50000 --ticks 10000` simulates
10 000 ticks in 2.7 s (0.27 ms per tick) with identical fingerprints in parallel and sequential mode.
Scenario setup takes ~2.5 s (it took 26–57 s before the synthetic lines' 10 000 A* routes were
searched in parallel with a faster A*); real games will build lines from player actions, and cached
hierarchical pathfinding (feature 007) will cut this further. Total run time stays under the 60 s target (SC-008).

### Industry economy (feature 009)

`IndustryBenchmarks` (short job, 2026-11-23) generates a 4096 × 4096 map once and places industries on
all its sites (about 1 000), then measures the economy:

| Benchmark | Mean | Allocated | Budget |
|---|---|---|---|
| `AdvanceDay` for every industry | 20.3 µs | 0 B | < 1 ms |
| `AdvanceMonth` for every industry | 51.7 µs | 0 B | < 5 ms |
| `PlaceOnMap` (initial placement) | 424 µs | 1.4 MB once | informational |

After the town–industry rule (2026-11-24, same machine and job): `AdvanceDay` 26.4 µs, `AdvanceMonth`
56.9 µs (both 0 B, within noise), `PlaceOnMap` 1.32 ms (1.5 MB once; the nearest-town search is
industry sites × town sites, about 1 000 × 700 on a 4096 map). `MapGenerationBenchmarks.Generate`:
7.2 ms / 100 ms / 408 ms / 2.1 s for 256 / 1024 / 2048 / 4096, unchanged within noise.

The full simulation tick budget is unaffected: industries only work on day and month boundaries.

### Entity storage decision (research R5)

The same vehicle tick was implemented on the Arch ECS (`ArchComparisonBenchmarks`, single-threaded,
default statistical job):

| Vehicles | Own storage | Arch ECS |
|---|---|---|
| 100 000 | 2.49 ms ± 0.31 | 2.05 ms ± 0.17 |
| 1 000 000 | 19.2 ms ± 2.2 | 18.4 ms ± 2.3 |

- The first comparison (seven separate arrays) showed Arch ~25 % faster. The cause was data layout:
  Arch keeps a vehicle's fields together. Packing the hot fields into one `VehicleState` struct
  closed most of the gap (the reference tick at 50 000 vehicles went from 0.26 to 0.19 ms).
- With 1 000 000 vehicles both are equal within noise; with 100 000 Arch is still ~16 % faster
  single-threaded.
- **Decision: keep our own storage** (flat arrays + packed hot structs) for now, because the game runs
  the tick in parallel (5× faster than sequential, measured, deterministic and allocation-free),
  Arch's parallel queries need its job scheduler and were not measured, and the single-threaded gap
  does not justify a new dependency in `Talt.Core`. **Pattern for later features:** one dense array
  of packed hot-state structs per system, plus separate arrays for cold data.
- Re-evaluate with a parallel Arch benchmark when vehicle interactions arrive (feature 012).

## Rendering

- **Silk.NET** creates the window (GLFW) and provides the WebGPU bindings.
- **WebGPU** through wgpu-native, which uses Vulkan, Metal or DirectX 12 depending on the platform.
  On the development machine (Windows, AMD) it runs on Vulkan.
- `WebGpuRenderer` owns the device, the pipelines, the uniform buffer (camera, sun, ambient light, fog,
  shadow cascades, material palette) and runs the frame; feature 018 changed the frame (see
  "Visual quality" below).
- `voxel.wgsl`: unpacks `PackedVertex`, adds the region origin (per-instance attribute), applies
  palette colour × per-voxel variation × interpolated AO, the shared lighting of `lighting.wgsl`, and
  the animated water of feature 018.

### Visual quality (feature 018)

Specs in `specs/018-visual-quality/` (research R1–R13). Nothing here touches the simulation: goldens
and recorded sessions are unchanged.

- **Frame.** (1) CPU: terrain regions, roads and town areas are culled; the frame's `ModelBatch` is
  filled (vehicles, town buildings, industries, trees, bushes and rocks), culled, levelled and sorted;
  the shadow cascades are fitted. (2) One depth-only pass per cascade into a `Depth32Float` array
  texture: terrain (regions chosen against the cascade's light volume, so off-screen hills shade the
  screen), roads, town boxes, placeholders and models (not in the third cascade, where their shadows are
  a few texels). (3) The world pass into a 4× multisampled target when anti-aliasing is on, resolved into
  the surface. (4) The interface, unchanged.
- **Bind groups and pipelines.** Group 0: `Uniforms` (camera or cascade matrix; the shadow pass reuses
  every vertex shader unchanged with a per-cascade buffer). Group 1 (`FrameResources`, fragment only):
  shadow map array with a comparison sampler, water texture with a linear sampler, model palettes. The
  world layout has groups 0 and 1, the shadow layout group 0 only. Each renderer keeps a `WorldPipeline`
  (pipelines per MSAA sample count, created on first use, plus a depth-only shadow variant with
  slope-scaled bias) built from a managed `PipelineSpec`; renderers receive a `WorldPassContext`.
- **Lighting** (`lighting.wgsl`, inserted by the `// #include` line that `WebGpuShaders.ExpandIncludes`
  expands): sun colour × shadow, sky/ground hemispheric ambient × AO strength, fog, then Narkowicz's
  ACES fit with exposure; the sRGB surface encodes gamma. The clear colour is the tone-mapped sky
  (`Lighting.ToneMap`). The default sun shines over the default camera's left shoulder, so the two
  visible walls differ and shadows fall where they show.
- **Shadows** (`ShadowCascades`, `ShadowRenderer`): 1–3 cascades by preset, practical splits
  (λ = 0.75) up to the shadow distance (capped by the fog), orthographic light projection around each
  slice's bounding sphere, centre snapped to whole texels (no shimmering), 1/4/9 hardware-filtered taps,
  normal-offset lookups, fade over the last tenth.
- **Water**: water top faces get two scrolling value-noise normals (real-time clock, also when paused),
  Fresnel towards the sky, a sun highlight and a colour from depth; at the shore the shallow edge takes
  the wet colour of the nearest land tile and a thin, faint foam line breaks a little further out. Data
  from `WaterTexture` (RGBA8, per tile up to 4096 per side: distance to land by a two-pass chamfer
  transform that also carries the nearest land tile's material, water depth; rebuilt around terraformed
  tiles).
- **Voxel models.** `Talt.Core.Meshing.Models`: `VoxReader`/`VoxWriter` (MagicaVoxel `.vox`, written
  from the format description, contracts/vox-format.md; Z-up file axes become Y-up), `ModelMesher`
  (greedy quads merged by colour and corner AO, ground below the model darkens wall feet, levels 0–3 at
  1, 2, 4 and 8 voxels per cell). Client: `ModelLibrary` reads `Rendering/Data/models.json` and the
  embedded `models/*.vox`, builds one vertex array (12-byte `ModelVertex`: half-voxel position relative
  to the base centre, face, AO, palette index and row) and a palette texture whose alpha marks paint
  (instance colour) and glow (lit windows at night). `ModelBatch` culls (frustum enlarged by each
  object's shadow reach, fog), picks the level by distance (`modelDetail` distances) and counting-sorts
  instances into (variant, level) buckets; `ModelRenderer` draws each bucket with one indexed instanced
  call (base vertex = level start, first instance = bucket start); shadows use one level coarser. 8 model
  voxels per tile.
- **Model keys** (contracts/model-pipeline.md): `vehicle` (variant by vehicle id, paint = vehicle
  colour), `town-building:<id>` (variant by building id, half or quarter turns), `industry:<id>`,
  `tree:broadleaf|conifer|birch`, `bush`, `rock`; keys without an entry keep their box placeholders.
  **Stations and depots (feature 010)** get models by adding `.vox` files and `station:<id>` /
  `depot:<id>` entries, then calling `ModelBatch.Add(entry.Variant(id), position, yaw, tint, variation)`
  from their instance source; no renderer, shader or pipeline change. The first 57 models (vehicles,
  14 building types × 2, 11 industries, trees, bushes, rocks) are original and generated by
  `tools/Talt.ModelGen` (deterministic; regenerate after data changes; editable in MagicaVoxel).
- **Decoration** (`DecorScatter`): at most one bush or rock per free tile from a hash of tile and map
  seed (never on water, beaches, roads, buildings, industries or under trees), ring-gathered around the
  camera, density by preset; presentation only.
- **Day/night** (`TimeOfDay`, off by default): 12 real minutes per cycle, advances only while the game
  runs, keyframed sun/ambient/sky/exposure, the moon at night, windows glow by night factor.
- **Settings** (`GraphicsSettings`, saved as `graphics` in the player settings, version 4): preset
  Low/Medium/High/Custom plus shadows, shadow quality, anti-aliasing, water effects, scenery density,
  model detail, ambient occlusion and day/night; numbers in `Rendering/Data/graphics-presets.json`.
  `--graphics <preset>` for one run, `--measure-effects [s]` for the A/B cost table, `--day-night`,
  `--time-of-day <0-1>`, `--camera x,z`.

### Visual quality numbers (feature 018, Windows, AMD Radeon integrated GPU, Vulkan, 1600 × 900)

Measured with `--measure-effects 3` (vertical sync off, camera still, 3 000 synthetic vehicles; each
configuration turns one effect off from the full preset; "cost" = full preset minus that row, or the
row minus full preset for "day/night on"). Run-to-run noise is about ±0.5 ms.

| Scenario | Full preset avg / p99 (ms) | FPS |
|---|---|---|
| High, 2048 map, camera distance 40 (town) | 7.97 / 9.52 | 126 |
| High, 2048 map, camera distance 350 (default) | 13.93 / 15.24 | 72 |
| High, 2048 map, camera distance 1200 | 10.96 / 12.63 | 91 |
| Medium, 4096 map, camera distance 60 | 6.39 / 7.50 | 156 |
| Medium, 4096 map, camera distance 350 (default) | 14.16 / 15.41 | 71 |
| Medium, 4096 map, camera distance 3000 | 13.38 / 15.04 | 75 |

Both budgets hold at every zoom measured (SC-001, SC-002): High keeps 60 FPS on 2048, Medium on 4096.

Effect costs, Medium on a 4096 map at the default camera distance (350):

| Effect | Cost (ms) |
|---|---|
| Shadows (2 cascades × 2048, 4 taps) | 4.42 |
| Anti-aliasing (MSAA 4×) | 6.52 |
| Water effects | 0.86 |
| Bushes and rocks | 1.43 |
| Ambient occlusion | 0.86 (noise level) |
| Model detail medium vs low | 0.86 |
| All models (vs not drawn) | 8.36 |
| Day/night on | ≈ 0 (−1.02, noise) |

Effect costs, High on a 2048 map at camera distance 70 (town view, before the level-3 and cascade
changes; shadows 3 × 2048 9 taps 5.92, MSAA 2.23, water 0.32, decoration 0.63, AO 0.09, model detail
1.79, models 4.14, day/night 0.62). At the default distance the first High version took 19.6 ms; adding
model level 3 (one cell per tile) and coarser shadow levels brought it to 15.2 ms, and skipping models in
the third cascade and a 420-tile shadow distance to 13.9 ms.

CPU (BenchmarkDotNet, `ModelBenchmarks`, short job): loading and meshing all 57 models 27.5 ms (20 MB
allocated once at start; the client logs about 80 ms including file reads, SC-005 < 1 s); batching
60 000 instances 1.35 ms, 0 B allocated; scattering decoration around the camera on a 1024 map
0.32 ms, 0 B. In game the model batch takes about 2.4 ms per frame at the default zoom on a 2048 map
(26 000 instances); the frame still allocates 0 B.

### World rendering (feature 003)

- **Regions.** `WorldRenderer` groups chunks into regions of 4 × 4 × 4 chunks (128³ voxels): 1 024
  regions on a 4096 map, 16 on a 512 map. Each region has one GPU vertex buffer per level of detail;
  each visible region is one `DrawIndexed` (`firstInstance` = region slot selects its origin).
- **Full-detail buffers** are the concatenation of the region's chunk meshes. When chunks change, a
  new buffer is assembled on the GPU: unchanged chunk ranges are copied buffer-to-buffer from the
  old one and only rebuilt chunks are uploaded, so no mesh copy stays in main memory.
- **Releasing full detail.** Full-detail buffers are the bulk of world memory, so a region's LOD 0
  buffer is released (and its chunk bookkeeping reset) once the region has not chosen LOD 0 for 5 s
  and is farther than 1.5 × the nearer of the farthest LOD 0 distance (`LodSelector.FullDetailDistance`)
  and the full-fog distance (`Lod0Eviction`). LOD 0 is only rebuilt inside both, so hovering near the
  threshold does not thrash. If no simplified level is built yet, one is built first and the release
  waits for it, so a region never drops to nothing; it also waits while a chunk mesh of the region is
  in flight. Simplified levels (LOD 1–3) are small and kept. Edits keep working: a released region
  rebuilds every chunk at its current version when it wants full detail again.
- **Culling** per frame on the CPU: regions beyond the full-fog distance are skipped (they would be
  drawn in sky colour), then a frustum test on the region box and on the mesh bounds.
- **Level of detail** per region from the screen-space error of its nearest point
  (`LodSelector`: the coarsest level whose cells stay under 2 px, 10 % hysteresis). While a level is
  not built yet the nearest built level is drawn (coarser first); a region with nothing built is
  not drawn (the map fills in at start).
- **Background meshing.** `ChunkMeshScheduler` runs dedicated below-normal-priority workers
  (processor count − 2). Each frame the render thread lists missing or stale meshes for regions
  within the fog distance, sorts them by distance (visible first; simplified levels first because
  they are cheap and fill the view), and keeps at most two jobs per worker in flight. Finished meshes
  are uploaded within 4 MB or 2 ms per frame. Nothing is meshed before the window opens.
- **Edits.** `NotifyVoxelsChanged` bumps the version of every chunk within one voxel of a change
  (`ChunkCoord.TouchedBy`) and of their regions' simplified levels; stale meshes are drawn until
  their replacement arrives. The client reports when an edit is on screen (construction edits print
  "Edit visible after … ms"). Chunks created after setup (a column raised into the chunk above, feature
  006) are registered by `NotifyVoxelsChanged`; `VoxelWorld` uses a concurrent dictionary so the main
  thread may create them while mesh workers read.
- **Statistics.** F3 (or `--stats`) shows frame time average/worst, FPS, draw calls, triangles,
  regions per level, world GPU memory (and full-detail buffers released so far), pending mesh jobs
  and managed bytes allocated per frame by the draw path (0 in steady state) in the title bar and the console.
- `vehicles.wgsl`: **instancing**. A single unit cube is drawn once per vehicle in one draw call;
  each instance carries position, heading and color, uploaded every frame.
- Colors are linear. The renderer prefers an sRGB surface format so the GPU converts them.
- GPU resources are released after the window loop has finished, never inside window events.
- **At most two frames in flight.** Each frame is submitted with `wgpuQueueSubmitForIndex`, and before
  submitting a new frame the renderer waits (`wgpuDevicePoll`) for the frame two submissions back.
  Vertical sync alone does not throttle when the window is hidden behind others: during the first
  hand test of feature 003 the client ran at ~480 FPS, queued uploads faster than the integrated GPU
  finished them, and exhausted shared memory until wgpu-native aborted. Vehicle instances are also
  uploaded only in frames that are drawn (never while minimized).

## Camera and controls

`OrbitCamera` orbits a target point on the ground. The target follows the terrain height and stays
inside the map, and the camera tilts up automatically so it never goes inside a hill.

Default controls (feature 005 makes every key rebindable in Settings → Key bindings; the defaults
live in `src/Talt.Client/Input/Data/actions.json`):

| Input | Action |
|---|---|
| Left mouse drag on the map | Rotate |
| Right mouse drag, WASD, arrows | Pan |
| Mouse wheel, `=` / `-` | Zoom (the farthest zoom shows the whole map: up to 2 × the map side) |
| Q / E | Rotate |
| Left click on the map (no drag) | Select the object under the cursor and open its information window |
| Space | Pause / resume |
| 1 / 2 / 3 | Speed ×1 / ×3 / ×8 |
| F | Fast forward on / off (only when unlocked in Settings or with `--unlock-fast-forward`) |
| N | Add a vehicle to a random line |
| Delete | Remove the last vehicle you added |
| Shift+Delete | Close all windows except pinned ones |
| F10 | Settings |
| F3 | Render statistics in the title bar and console (on / off) |
| PageUp / PageDown / L / X | Construction tools: raise, lower, level, clear (feature 006) |
| Backspace | Undo the last construction (within 10 s of game time) |
| R / B / T / Shift+R / O | Road tools: road, bridge, tunnel, remove road, one-way (feature 007) |
| M / , / . / Y | Motorway, motorway exit, motorway entry, next road type (feature 007) |
| Left drag with a tool | Preview and build over the rectangle; release builds, Esc or right button cancels |
| Esc | Close the front window; then clear the selection; then open / close the pause menu |

Options for measurements: `--stats` shows the statistics from the start, `--zoom <distance|max>` sets
the initial camera distance, `--ui-test-windows <n>` opens n sample windows.

The date, money and speed are in the top bar (feature 005); the window title shows the game name, or
the render statistics with `--stats`/F3, including the interface time per frame and the window count.

## User interface (feature 005)

The client draws its own interface on top of the world (research in `specs/005-ui-foundation/research.md`).

- **Framework (`Ui/`)**: a retained widget tree with two-pass layout (measure, arrange) in logical
  pixels multiplied by the interface scale (75–200 %). `UiContext` holds the layers drawn back to front
  (screen, overlay such as the pause menu, windows, popup such as an open drop-down, tooltip), hover,
  mouse capture, keyboard focus and the tooltip timer. Widgets: panel, stack, spacer, label, button,
  icon, check box, drop-down, text input, slider, scroll area, list, tabs, window. Widgets only take
  `TextRef` (a localization key, or a dynamic text composed from localized formats), never literals.
  `WindowManager` keeps z-order, cascades windows of the same kind, focuses an open window with the same
  key (one window per map object), pins, closes all unpinned, keeps title bars on screen, at most 64.
- **Drawing**: `UiContext.Draw` fills a `UiDrawList` (44-byte vertices, four per quad, a draw command
  per clip rectangle) with no allocations in steady frames. `UiRenderer` uploads it and draws it in a
  second render pass (`LoadOp.Load`, premultiplied alpha) with one pipeline and one 1024 × 1024 atlas:
  rounded rectangles are exact signed-distance shapes in the shader, text is an SDF font atlas generated
  at startup from the bundled Noto Sans (OFL) with StbTrueTypeSharp (395 glyphs in ~140 ms: Latin-1,
  Latin Extended-A, typography and currency symbols), icons are original 16 × 16 pixel art from
  `Ui/Data/icons.json`. Theme colours and sizes are in `Ui/Data/theme.json`.
- **Input (`Input/`)**: `InputRouter` gives each event to the owner of the current drag, then the
  interface (front to back), then the focused text input, then the key bindings, then the world (camera
  and picking). A press and release within 4 logical pixels on the map is a click; a drag that started
  on the map never clicks the interface.
- **Picking (`Picking/`)**: CPU ray from the cursor each frame. The terrain is a 2D walk over the tile
  columns the ray crosses (exact for voxel columns at any zoom), roads are tiles whose top voxel is road,
  and vehicles are ray/box tests against the instances drawn this frame, pre-filtered by their distance
  to the ray's ground footprint. `IPickLayer` lets later features add stations or buildings with a
  priority. Hits behind the terrain are ignored. `HighlightRenderer` outlines the hovered and selected
  objects with wire boxes in the world pass.
- **Screens (`Screens/`)**: the window opens on the main menu with no world; a new game is generated on
  a background task while the new-game screen shows "Generating…", then the renderer receives the world
  (`SetWorld`; `ClearWorld` when going back to the menu). Game arguments on the command line skip the
  menu, so the CI smoke test is unchanged.
- **Localization**: `Strings` (en/es tables, English fallback, `key.one`/`key.other` plurals, culture
  numbers and dates) and `MoneyFormatter` (21 display currencies with fixed rates from
  `Localization/Data/currencies.json`; the internal unit is one US dollar; default USD in English, EUR in
  Spanish). The top bar shows the player company's balance from the simulation (feature 006; the
  starting balance is in `Talt.Core/Construction/Data/construction.json`). Tests check both languages have the same keys, every key in the code exists,
  every text of every screen resolves in both languages and fixed-width labels fit.
- **Settings**: `PlayerSettings` reads each property on its own (invalid values fall back alone and are
  reported once), saves after every change through a temporary file, and keeps the feature 001 file.

**New-game screen and map generation (T069, after feature 004):** `DefaultNewGameOptionsSource` is bound
to 004's `MapSettings`: width and depth (256–4096), roughness, maximum height, water and sea level as
sliders, the seed, plus vehicles and start year; the sea level is kept at least 2 below the maximum height.
The screen shows the shareable map code (`MapSettings.ToText`) as the values change. While generating,
`GenerationProgress` receives 004's `MapGenerationProgress` (reported from parallel workers, so it is
locked and only moves forward) and the screen shows the localized stage and a progress bar: map
generation fills 0–80 % and building the voxel world 80–100 % (`GameSetup` scales each step). Before
the first map stage the screen says the roads and traffic are being prepared.

**Hand test (developer, Windows, 2026-11-23):** main menu, settings in both languages (live switch, currency
following the language, settings kept after restarting), new game, top bar and toolbar, hover outlines,
one information window per tile, road and vehicle, pinning and closing all windows, tooltips with
shortcuts, pause menu and rebinding a key all work. Found and fixed during the test, each with a
regression test: the menu title and the Spanish subtitle were cut with an ellipsis; the menu background
was almost black because this driver stores clear colours without the sRGB conversion (menus now draw
their background as an interface rectangle); clicking the map selected nothing, because picking pauses
while a button is held and the click looked at the cursor on release instead of at the press.

### Interface cost (feature 005)

BenchmarkDotNet, short job, AMD Ryzen 7 160, 2026-11-23 (`UiFrameBenchmarks`, `PickingBenchmarks`):

| Benchmark | Time | Allocated |
|---|---|---|
| Interface frame (layout + draw list), in-game HUD, 0 windows | 0.002 ms | 0 B |
| Interface frame, HUD + 20 sample windows (every widget kind) | 0.076 ms | 0 B |
| Terrain pick, grazing ray across a 4096 map | 0.030 ms | 0 B |
| Full pick with 50 000 vehicles, ray straight down | 0.073 ms | 0 B |
| Full pick with 50 000 vehicles, default camera view | 0.076 ms (0.45 ms before the footprint rectangle test) | 0 B |

The vehicle test first rejects vehicles outside the bounding rectangle of the ray's ground footprint
with four comparisons combined with a non-short-circuit `&` (one well-predicted branch; with `||` the
random positions mispredicted and cost 0.2 ms), then measures the distance to the footprint, and only
then tests the box.

**In the client** (`--stats`, 4096 × 4096 map, 50 000 vehicles, normal speed, 1600 × 900, 2026-11-23),
"interface" = picking + layout + draw list + GPU upload and encoding:

| Case | FPS | Worst frame per second | Interface per frame |
|---|---|---|---|
| Cursor over the map, 0 windows | 60 | 17.5–18.7 ms | 0.17–0.20 ms |
| 20 sample windows open (`--ui-test-windows 20`) | 60 | 17.9–19.4 ms | 0.25–0.34 ms |

SC-001 (20 windows add less than 1 ms) and SC-005 (4096 at 60 FPS with the interface, 0 B allocated per
frame in steady state) hold. The main menu costs 0.1 ms per frame. The first in-game frame takes about
9 ms of interface time once (texts and layouts are created).

## Audio (feature 019)

Sound lives only in `Talt.Client/Audio`; `Talt.Core` has no audio reference and the simulation is
unchanged. The client reads `GameSimulation.CommandProcessed`, the terrain-edit result, UI button
activation and the per-frame camera/vehicle snapshot, and turns them into cues. No player action
or state crosses back into the simulation.

- **Data**: `Audio/Data/audio.json` (embedded and validated at startup) defines the five cue
  sounds, the three ambient beds, the music playlist, four bus assignments, gains, priorities, the
  eight-voice budget and the distance/zoom/pan curves. Invalid data logs one clear error and starts
  the game silently instead of stopping it.
- **Native boundary**: `MiniAudioBackend` is the only file that uses `JAJ.Packages.MiniAudioEx`
  (miniaudio 0.11.26). It creates one engine and four `MaSoundGroup` buses (master → music,
  effects, ambient), a fixed pool of voices, and streams the FLAC music tracks. The wrapper is
  MIT/Unlicense and ships native libraries for win-x64/arm64, linux-x64/arm64 and osx-x64/arm64.
  If the engine cannot initialize (no device, container, missing library) `TryCreate` returns null,
  `AudioSystem` uses `NullAudioBackend`, prints one reason and the game keeps running in silence.
  `--no-audio` forces the silent path for CI and manual tests.
- **Mixer**: `AudioMixer` is pure managed and benchmarked. It applies master/music/effects/ambient
  gains and mute, distance attenuation (full volume at 18 tiles, 1.4 rolloff), a zoom gain that
  falls to 20 % at the reference distance, a behind-camera gain and stereo pan from the camera
  basis. It keeps the eight nearest vehicles within 220 tiles, gives the selected and hovered
  vehicle priority and a 4-tile hysteresis, keeps each selected vehicle on the same voice slot even
  when its rank changes, and drops the lowest-priority one-shot when the four one-shot voices are
  busy. Volume and pan are ramped per voice (30 ms in, 80 ms out, 50 ms pan) so a vehicle entering
  or leaving the eight-voice set never clicks. No LINQ, strings or objects are created in the
  update path; the scene is a `readonly ref struct` pointing at the session's existing arrays.
- **Ambience and music**: a 128 × 128 per-game environment grid built once from `MapData` selects
  country, water or town and cross-fades two ambient voices over 1.5 s. Music starts in the main
  menu, survives screen changes, streams one FLAC track at a time and loops a playlist; missing
  tracks are skipped and all-failed music disables itself with one warning.
- **Settings**: `PlayerSettings` version 3 adds master/music/effects/ambient volumes (defaults
  0.8/0.5/0.8/0.65) and mute. The Settings window shows four focusable sliders (mouse and arrow
  keys) with percentages and a mute check box; changes apply and save immediately, and mute never
  overwrites the stored volumes.
- **Assets**: every WAV/FLAC file is original, generated deterministically by
  `tools/audio/generate-audio.cs` (FLAC encoding with EggEncoder, a build-time MIT tool). Loops are
  cross-faded and trimmed at generation time so their end and start are continuous. They are
  listed in `README.md`. The shipped miniaudio build decodes WAV and FLAC but not Vorbis, so music
  and loops use FLAC instead of OGG; the streamed-from-disk design is unchanged.
- **Cost**: `AudioBenchmarks` (50 000 vehicles, 8 voices, short job, Windows, AMD Ryzen 7 160,
  2026-11-23) takes **0.285 ms** per update and allocates **0 B**; the in-client F3 title shows the
  live audio milliseconds and active voice count. The allocation test runs 1 000 updates after
  warm-up and asserts zero managed bytes.
- **Hand test (developer, Windows, 2026-11-23)**: music and clicks in the menu, music continues in
  the game, ambience and engines follow the camera, construction and all four volume controls plus
  mute work. The first version crackled when vehicle ranks changed; each vehicle now keeps its
  voice slot, volume/pan ramp, and sample loops cross-fade correctly. The developer confirmed the
  result is much better and acceptable; a faint background residue was left for a later mixing pass.

## Rendering performance (feature 003)

Windows, AMD Ryzen 7 160 with integrated Radeon GPU over Vulkan, 1600 × 900 window, vertical sync
(60 Hz), 50 000 vehicles at normal speed, Release build, 2026-11-22. Numbers from the client's render
statistics (`--stats`): FPS and worst frame over one second, world GPU memory = vertex, index and
origin buffers of the terrain.

| Map | Zoom (camera distance) | FPS | Worst frame | Draws | Triangles | Regions by LOD 0/1/2/3 | World memory |
|---|---|---|---|---|---|---|---|
| 512 × 512 (`-- 16`) | 350 (default) | 60 | 20 ms | 15 | 268 k | 15/0/0/0 | 4 MB |
| 512 × 512 | 2 000 (max) | 60 | 20 ms | 16 | 78 k | 0/16/0/0 | 1 MB |
| 2048 × 2048 (`-- 64`) | 350 | 60 | 22 ms | 21 | 386 k | 21/0/0/0 | 66 MB |
| 2048 × 2048 | 4 096 (max) | 60 | 21 ms | 256 | 366 k | 0/0/256/0 | 5 MB |
| 4096 × 4096 (`-- 128`) | 350 | 60 | 20 ms | 21 | 379 k | 21/0/0/0 | 96 MB |
| 4096 × 4096 | 1 000 | 60 | 21 ms | 94 | 1.6 M | 92/2/0/0 | 157 MB |
| 4096 × 4096 | 2 500 | 60 | 22 ms | 472 | 1.7 M | 0/313/159/0 | 56 MB |
| 4096 × 4096 | 4 500 | 60 | 21 ms | 906 | 1.3 M | 0/0/897/9 | 22 MB |
| 4096 × 4096 | 8 192 (max, whole map) | 60 | 22 ms | 1 023 | 509 k | 0/0/0/1 023 | 8 MB |

Before feature 003: 1024 map 4.8 M triangles at 36 FPS in fast forward, 2048 map 19.1 M triangles at
14 FPS, 4096 map blank window (6.4 GB of buffers, out of GPU memory).

**Hand test on the 4096 map** (developer, ~8 minutes of panning, rotating and zooming between the
closest and the farthest zoom, with edits and fast forward; console log of the statistics):

- Normal speed: 490 one-second samples, average FPS 56–60 in all of them (60 in 486). In 475 of them
  no frame exceeded 33 ms; 15 had one longer frame (34–56 ms, typically 36–42 ms), also while the
  camera was still and no meshing was pending, so they are not caused by mesh uploads. Up to
  2.75 M triangles and 430 draws at medium zoom; world memory peaked at 292 MB after visiting much of
  the map (all built levels were kept then; full-detail buffers are now released, see below).
  SC-001 is met on average frame rate; the occasional single long frame is a follow-up (see below).
- Edits (PageUp/PageDown): every single-tile edit was on screen after 29–34 ms (two frames; 1 or 4
  chunks rebuilt), well under the 100 ms target (SC-002). 32 × 32 area edits also showed within a few
  frames.
- Startup: the window opens right after terrain generation (~4.7 s) and the view fills in within
  ~3 s, nearest first. Creating the synthetic traffic before that still takes ~26–38 s (feature 001).
- Managed allocations of the draw path: 0 B per frame in steady state.

Meshing benchmark (`ChunkMeshingBenchmarks`, BenchmarkDotNet default job, terrain with roads, per
chunk of 32³ voxels):

| Benchmark | Mean | Allocated |
|---|---|---|
| Reference naive mesher (`ChunkMesher`) | 1.25 ms | 1 933 KB |
| Greedy mesher, full detail | 0.62 ms | 19 KB |
| Region LOD 1 (per chunk of the region) | 0.22 ms | 154 KB |
| Region LOD 2 | 0.12 ms | 20 KB |
| Region LOD 3 | 0.10 ms | 3 KB |

Memory per face: 168 bytes before (36-byte vertices + 32-bit indices) vs 32 bytes per merged quad now
(8-byte vertices, shared index buffer), and far fewer quads after merging.

**Long frames.** While statistics are shown, every frame over 33.4 ms prints where its time went:
`Long frame … ms: update (simulation, ticks), vehicles, acquire, prepare, encode, wait GPU, submit; GC
gen0/gen1/gen2 (pause)`. First diagnosis (2026-11-22), measured while two other Claude sessions were
running builds, tests and 4096 headless runs on the same machine: the render phases (prepare,
encode, wait GPU, submit) stayed under 1 ms almost always; the long frames came from the simulation
tick (normally ~1.6 ms, here 20–290 ms) and from building vehicle instances (normally ~2–4 ms, here
up to 85 ms), both `Parallel.For` work on the shared thread pool that is starved when other
processes load the CPU. No garbage collection happened in any long frame. The hand test above also
ran while another session was benchmarking, so the isolated 34–56 ms frames are most likely the same
CPU contention.

**Confirmed on an idle machine** (same day, no other session running, 4096 map, 50 000 vehicles,
default zoom, 226 one-second samples): 60 FPS in 222 samples and 56–59 in 4; worst frame ≤ 29 ms in
223 samples. Simulation ticks took ~1.5 ms and vehicle instances < 2 ms. The only three long frames
(~210 ms, about 200 s into the run) were spent entirely inside `wgpuSurfaceGetCurrentTexture`
(waiting for a swap-chain image from the driver/compositor) with everything else under 2 ms: a
presentation stall outside the game's code, not reproduced in the other runs. Conclusion: simulation
and drawing stay within the frame budget; long frames seen during testing came from other processes
loading the CPU, plus rare presentation stalls.

**Releasing full-detail buffers** (2026-11-22, 4096 map, 50 000 vehicles, `--stats`, hand test
panning across the map and zooming out to the farthest zoom and back): world memory went back down
after moving away, e.g. 245 → 143 MB, 183 → 164 MB and 245 → 223 MB within a few seconds, with 597
full-detail buffers released in about 30 s of movement; peak 245 MB (292 MB before, with all levels
kept). 60 FPS in 593 of 597 one-second samples, 0 B allocated per frame at rest. The
memory that stays is the simplified levels of every region visited (small) plus full detail around
the camera. `--zoom max` without input is unchanged: 1 023 draws, 8 MB, 60 FPS, 0 B per frame.
`--zoom 1000` without input: 94 draws (92 at LOD 0), settles at 157 MB, 60 FPS, 0 B per frame and
nothing released (every region within reach may still want full detail).

**Follow-ups:** watch for presentation stalls on other machines (the long-frame log names them).

## Current numbers (prototype before feature 003, Windows, AMD Radeon integrated GPU)

| Map | Chunks | Generation | Meshing | Triangles | Vehicles | Tick | FPS |
|---|---|---|---|---|---|---|---|
| 512 × 512 | 256 | ~500 ms | ~200 ms | ~1.25 M | 20 000 | ~0.7 ms | 60 (VSync) |
| 1024 × 1024 | 1 024 | ~900 ms | ~600 ms | ~4.8 M | 50 000 | ~1.6 ms | 46–55 |

In the client the tick is slower than in the benchmark (1.6 ms instead of 0.19 ms at 50 000 vehicles)
because at normal speed ticks are 50 ms apart and worker threads go idle between them. Copying poses
for interpolation and building GPU instances run in parallel batches; the pose buffers are swapped
instead of copied when possible.

Known issues reported while testing the 2048 × 2048 map: panning is a bit jerky (about 16 M
terrain triangles are drawn every frame with no culling or level of detail yet).

## Verification status (feature 001)

- Windows x64: all tests pass; headless run and replay verified; the client's fingerprint at a given
  tick equals the headless one; the client exits cleanly.
- Linux x64 (2026-11-22, Ubuntu 26.04 under WSL 2, .NET SDK 10.0.404): all 151 tests pass,
  golden fingerprints included. Headless fingerprints equal the Windows ones (default scenario at
  5000 ticks `C9F573FD7DE5CBE3`, parallel and sequential; reference load at 10 000 ticks
  `C0C656C5AB2D3302`), and a session recorded on Windows replays on Linux with every checkpoint OK.
- Continuous integration (feature 002, GitHub Actions run of 2026-11-22; images ubuntu-26.04,
  windows-2025 and macos-26 arm64; .NET 10 SDK): on **Windows x64, Linux x64 and macOS arm64** all
  151 tests pass and the headless fingerprints equal the golden values (default scenario at 5000
  ticks `C9F573FD7DE5CBE3`, parallel and sequential; reference load `C0C656C5AB2D3302`), and the
  session recorded on Windows replays with every checkpoint OK. The whole matrix took under 3 minutes.
- ARM64 (same run, hosted `ubuntu-24.04-arm` and `windows-11-arm` runners, which are available to this
  private repository): **Linux arm64 and Windows arm64** also pass all 151 tests with identical
  fingerprints and replay. macOS Intel (x64) is not covered.
- Keyboard controls (speeds, fast forward, pause, add/remove vehicle): checked by hand in the window
  on Windows (2026-11-22, `-- 32 50000 2026 --unlock-fast-forward`), on top of the unit tests of the
  runner and commands.
- Fast-forward rate (SC-003, ≥ ×20 at ≥ 30 FPS with 50 000 vehicles): the client cannot draw the
  4096 × 4096 reference map yet. Its naive terrain mesh (38 million faces, about 6.4 GB of vertex
  and index buffers) runs the integrated reference GPU out of memory and the window stays blank.
  Measured by hand on Windows (2026-11-22, integrated AMD Radeon GPU over Vulkan, 50 000 vehicles):

  | Map | Terrain triangles | Fast forward | FPS | Result |
  |---|---|---|---|---|
  | 1024 × 1024 (`-- 32`) | 4.8 M | ×205 | 36 | passes |
  | 2048 × 2048 (`-- 64`) | 19.1 M | ×48 | 16 | limited by terrain drawing (14 FPS even at ×8) |
  | 4096 × 4096 (`-- 128`) | 76.1 M | — | — | blank window, out of GPU memory |

  The simulation meets SC-003 with margin. In fast forward the runner spends up to 25 ms of each
  frame simulating, so 30 FPS also needs drawing to take under about 8 ms per frame. The 2048 and
  4096 maps fail on drawing, not on simulation; those checks move to feature 003 (large-map
  rendering), whose budget is 2 GB of rendering memory.

  **Re-checked after feature 003** (2026-11-22, hand test, `-- 128 50000 --unlock-fast-forward
  --stats`): on the 4096 map fast forward ran at **×140–×228 effective speed at 35–36 FPS** while the
  camera moved (frames ~28 ms: up to 25 ms of simulation plus drawing). Seconds in which fast forward
  was switched on or off, or many regions were being loaded at once, dipped to ×34–×110 at 26–31 FPS.
  **SC-003 passes on the 4096 reference map.**

## Continuous integration

GitHub Actions (repository `rasputino/talt`, private). Workflows are thin; the checks are PowerShell
scripts in `ci/` that also run locally (`pwsh ci/check-fingerprints.ps1`).

| Workflow | When | What |
|---|---|---|
| `ci.yml` job `build-and-test` | every push and pull request | On `windows-latest`, `ubuntu-latest` (x64), `macos-latest`, `ubuntu-24.04-arm` and `windows-11-arm` (arm64): restore, build Release with warnings as errors, `dotnet test` (TRX files uploaded), then `ci/check-fingerprints.ps1` |
| `ci.yml` job `ci-result` | after the matrix | Single aggregate check; require this one in branch protection |
| `ci.yml` jobs `publish`, `nightly-release` | push to `main` | Self-contained builds of `src/Talt.Client` for win-x64, linux-x64, osx-arm64, each checked for the wgpu-native and GLFW native libraries (`ci/verify-publish.ps1`); uploaded as artifacts and attached to the prerelease `nightly` (replaced on every run, unsigned, testing only) |
| `benchmarks.yml` | manual and nightly | BenchmarkDotNet on `ubuntu-latest`, compared with `benchmarks/baselines/ubuntu-latest.json`; regressions above 10% are flagged in the run summary and as warnings but never fail the run |

- **Determinism gate:** `ci/golden-fingerprints.json` holds the expected fingerprints (default scenario
  at 5000 ticks, sequential and parallel; reference load 4096 map, 50 000 vehicles, 10 000 ticks).
  `ci/sessions/windows-reference.json` was recorded on Windows and is replayed on every platform, so
  every checkpoint must match. Change the golden values only when simulation rules change on purpose.
- **GPU:** runners have no GPU, so the client is only built. Any future GPU test must carry the trait
  `Category=Gpu`; CI excludes it.
- **Baselines:** updated by hand (see `benchmarks/baselines/README.md`), never by the workflow.
- **Client smoke test:** job `client-smoke` publishes the client for the runner's RID, starts the
  published executable (`ci/smoke-client.ps1`, option `--exit-after-fingerprint`), lets it simulate 20
  ticks and requires exit code 0. On Linux it uses a virtual display (xvfb) and the software Vulkan
  driver llvmpipe; on macOS the runner's Metal device. It passed on Linux x64, Linux arm64 and macOS
  arm64 with the same fingerprint as Windows (`1475835573081C3A`, tick 20 of `4 1000 1`; since feature 006
  the client's games include the map in the simulation state and the value is `578FD3BEDBF498BC`), so the
  downloaded builds start there. It is informational (not part of `ci-result`). Windows runners have no
  usable GPU (the renderer crashes creating its pipeline), so Windows is checked by hand.
  Finding: on Ubuntu 26.04, `dotnet run` of the client (not self-contained) cannot find the GLFW
  library under `runtimes/`; the published build, which carries it next to the executable, works.
- **Not covered:** macOS Intel (x64), the Windows client under CI, and any real GPU (all rendering in
  CI is software or virtualised).
- **Merge blocking:** the repository is public (since 2026-11-22), so branch protection on `main`
  requires the `ci-result` check. The owner can still push directly (administrators are not
  enforced), which is how direct merges are done.
- **Benchmark noise:** two runs of the same code on `ubuntu-latest` with the quick job differed by up to
  13% on one benchmark (3 rows flagged out of 12 with no code change), which is why regressions only
  warn and benchmarks do not run on every push. The baseline was recorded with the precise `default`
  job (about 14 minutes); a later `default` run showed no regression over it. Treat a single flagged
  row as a hint and repeat the run.
- **Verified on GitHub (2026-11-22):** the `publish` jobs produce self-contained builds that pass
  `verify-publish.ps1` for win-x64, linux-x64 and osx-arm64 (about 35-40 MB each), and the release
  step replaces the prerelease `nightly` assets.

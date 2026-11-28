# Transit & Logistics Tycoon — Vision

This document describes what TALT is, what it is not, and the principles that guide its development.
It is a living document: decisions marked as *tentative* must be validated before they become final.

## Elevator pitch

A transport management game where you build road, rail, sea and air networks to move passengers
and cargo across a living world. Voxel art style, and a simulation designed from day one to stay
fast on very large maps.

## Pillars

1. **Pure transport.** The player is a transport company, not an industrialist. There is no
   manufacturing, no production chains to manage, no factories to build. The world produces and
   consumes; the player connects it.
2. **Meaningful destinations.** Passengers, mail and goods want to go to a specific place.
   Building networks that serve real demand is the core challenge.
3. **Scales without slowing down.** Large maps must be possible without the performance loss that
   classic transport games suffer. Large maps are an option, not a requirement.
4. **Charming voxel look.** A clean, colorful, readable voxel art style.

## Gameplay

### Scope

- The player builds infrastructure (roads, rails, stations, ports, airports) and runs vehicles
  on routes.
- Industries and towns exist and evolve on their own. The player does not build or operate them.
- Everything (code, graphics, sounds, music, texts) is original.

### Cargo model

| Cargo kind | Examples | Where it can go |
|---|---|---|
| Raw materials | Coal, wood, ore, grain | Any place that accepts it |
| Most processed goods | Steel, lumber, fuel | Any place that accepts it |
| Destination cargo | Passengers, mail, consumer goods | A specific town or destination |

- Undirected cargo is cheap to simulate: it just needs an accepting destination.
- Destination cargo is the interesting (and expensive) part: it is modeled as **aggregated
  flows** (for example "120 passengers/month from A to B"), not as individual agents. This keeps
  the simulation cost independent of the number of passengers.

### Non-goals

- Manufacturing or production management.
- Individual simulation of each passenger.
- Recreating the look, assets or code of any existing game.

## Visual style

- Voxel art, similar in spirit to modern voxel transport games, with its own identity.
- Readability first: the player must understand the network at a glance at any zoom level.
- Level of detail (LOD): full detail near the camera, simplified representations far away.
- Objects are voxel models at 8 voxels per tile (terrain stays one voxel per tile), lit by a sun with
  soft shadows, with tone mapping, ambient occlusion and edge smoothing; water moves and foams at the
  shore; bushes and rocks dress the countryside (feature 018).

## Performance goals

The root cause of slowdowns on large maps in classic transport games is the simulation
(single-threaded, iterating over every tile and vehicle), not the rendering. TALT addresses this
through architecture:

- **Chunked world.** Terrain and entities are grouped into chunks. Only what matters is simulated
  and rendered in full detail.
- **Data-oriented, multithreaded simulation.** An ECS keeps data contiguous in memory and lets
  vehicles and systems be processed in parallel.
- **Hierarchical, cached pathfinding.** Routes are computed first between regions, then locally,
  and reused when nothing has changed.
- **Aggregated demand.** Destination cargo is simulated as flows between places.
- **Allocation-free hot paths.** No garbage generated in the simulation loop.

Initial targets (*tentative*, to be validated with benchmarks):

- Maps of at least 4096 × 4096 tiles.
- Tens of thousands of active vehicles.
- Stable 60 FPS rendering, with the simulation tick independent of the frame rate.

## Technology

No game engine: the game is built directly on top of libraries, for full control over
performance. Target platforms: Windows, Linux and macOS.

| Area | Choice | Status |
|---|---|---|
| Language / runtime | C# on .NET 10 | Decided |
| Window, input, graphics bindings | Silk.NET | Working in prototype |
| Graphics API | WebGPU (wgpu-native: Vulkan, Metal or DirectX 12) | Working in prototype on Windows; Linux and macOS not tested yet |
| ECS | Arch | Tentative |
| Debug UI and tools | ImGui.NET | Tentative |
| Final game UI | — | Open |
| Tests | xUnit | Tentative |
| Benchmarks | BenchmarkDotNet | Tentative |

## Development principles

The game is developed mostly through AI-assisted coding. To keep the project manageable:

- **Document the architecture** in `docs/`, so every session shares the same context.
- **Small, verifiable steps**, each with its own tests.
- **Measure performance from day one**, with a large test map, to catch regressions early.
- **Readable, idiomatic C#**, so every change can be reviewed and debugged by a human.

## Decision log

| Date | Decision | Where |
|---|---|---|
| 2026-11-22 | Simulation state uses integer fixed-point arithmetic (no floats) so results are bit-identical on Windows, Linux and macOS, x64 and ARM64. | specs/001 research R1 |
| 2026-11-22 | 40 simulation ticks per in-game day, 20 per second at normal speed (one day ≈ 2 s). | specs/001 research R2 |
| 2026-11-22 | Speeds ×1, ×3, ×8 always available; unlimited fast forward is unlocked in the settings and has its own control. | specs/001 clarifications |
| 2026-11-22 | Player actions are commands applied at the next tick; sessions can be recorded and replayed. | specs/001 research R7 |
| 2026-11-22 | The interface is our own retained-mode UI drawn with WebGPU, with an SDF font atlas from the bundled Noto Sans (OFL) rasterized by StbTrueTypeSharp; no UI library. | specs/005 research R1, R2 |
| 2026-11-22 | Money is counted in one internal unit (one US dollar) and shown in a player-chosen currency with fixed exchange rates (21 currencies); the default follows the language (USD in English, EUR in Spanish). | specs/005 clarifications |
| 2026-11-22 | Settings apply and are saved as soon as they change; each clicked map object gets its own information window. | specs/005 clarifications |
| 2026-11-22 | Map picking is done on the CPU (ray against tile columns and object boxes) so the answer is ready in the same frame. | specs/005 research R5 |
| 2026-11-23 | Freight economy: 13 cargo types and 11 industry kinds, all in validated data files; raw materials and processed goods are destination-free, passengers/mail/town goods are destination-based. | specs/009 contracts/content-schema.md |
| 2026-11-23 | About 75 % of the reserved industry sites start occupied; industries grow with deliveries, decline after ~24 months of neglect and may close after ~5 years, while new ones open on free sites. Waiting cargo is capped per cargo and production beyond the cap is discarded. | specs/009 clarifications |
| 2026-11-23 | Audio lives only in the client (miniaudio through MiniAudioEx, MIT/Unlicense) and never touches the simulation; music starts in the main menu and continues through the game, with separate master/music/effects/ambient volumes, a mute control and a silent fallback when there is no device. All sounds and music are original. | specs/019 plan, research |
| 2026-11-24 | Roads are tile data with eight directions (straight and 45° diagonal pieces, curves drawn smoothed). Four road types in data: dirt road (1850, 50 km/h), paved road (1920, 80 km/h), fast road (1960, 100 km/h) and motorway (1980, no limit of its own); upgrading pays the cost difference, downgrading is refused. | specs/007 clarifications |
| 2026-11-24 | Motorways are two parallel one-way carriageways (right-hand traffic) joined to other roads only at their ends or through exit/entry ramps, and can be elevated over valleys, water and roads. | specs/007 clarifications |
| 2026-11-24 | Bridges are straight with a maximum span that grows with the era (8, 16, 32 tiles from 1850, 1950, 1985); tunnels are straight, up to 64 tiles. Town streets can be removed only while the town's streets stay connected. | specs/007 clarifications |
| 2026-11-24 | Games with a map start with small street grids on the town sites and no synthetic traffic (real road vehicles arrive with feature 012); map-less scenarios keep the prototype grid for benchmarks and the CI goldens. | specs/007 research R1 |
| 2026-11-24 | Towns grow only when served (share of passengers and mail transported); unserved towns stay as founded. Starting sizes are varied (many villages, some towns, a few cities) with a Small/Normal/Large option; names are generated from original English- and Spanish-style parts (new-game option, default mixed). | specs/008 clarifications |
| 2026-11-24 | Town ratings per company (0–1 000, bands hostile/poor/neutral/good/excellent): demolishing town buildings or roads and cutting trees near towns lower them, service raises them; below "poor" a town refuses demolition of its property, below "hostile" stations in it (feature 010). Demolishing town buildings cannot be undone. | specs/008 clarifications, research R9–R10 |
| 2026-11-24 | Road stations come as bus stops (passengers) and truck stops (mail and freight), each as a bay beside a road or a drive-through stop on a straight road; pieces built next to each other join one station. Catchment radius 3 tiles for bus stops and 4 for truck stops; at most 4 000 units of a cargo wait per station, the excess is lost. Cargo appears only once a vehicle serves it there; the share of production a station gets follows its rating. | specs/010 clarifications |
| 2026-11-24 | Warehouses (small, medium, very large; influence radius 8, 16, 32 tiles) group the company's stations within their radius into one virtual station that shares stock, acceptance and ratings; overlapping warehouses merge their groups. Road stops are named town + type + number, warehouses town + place ("Fallowstead North"); every station can be renamed. | specs/010 clarifications |
| 2026-11-24 | Every industry belongs to a nearby town: it stands 60–120 tiles from the nearest town, far enough that the town can grow to about 25 000 inhabitants without reaching it, and is named after it and its kind ("Fallowstead Steel Mill", "Acería de Fallowstead"). | docs/ARCHITECTURE.md (map generation, sites) |
| 2026-11-25 | Finance: the company starts with 500 000 and no loan; it may borrow up to 500 000 in steps of 10 000 at 5 % a year (paid monthly). Deliveries pay by cargo rate × amount × distance between the stations, less for slow trips; roads, stations and warehouses cost yearly maintenance charged monthly, vehicles a daily running cost. Three month ends bankrupt in a row (negative cash with the maximum loan) warn, six end the game except in sandbox games; inflation (off by default) raises costs 2 % and payments 1.5 % a year for 100 years. All values in `finance.json`. | specs/011 clarifications |
| 2026-11-25 | Optional infinite money: chosen on the new-game screen or turned on during play after a confirmation, and never turned off for that game. Money then never limits the player (cash frozen, costs and income still listed, no loans, no bankruptcy); the game records that it used the mode so later goals and scores can exclude it. | specs/011 research R17 |
| 2026-11-25 | Visual quality: models are MagicaVoxel `.vox` files at 8 voxels per tile, original art generated by a project tool (editable later); quality presets Low/Medium/High (Medium default) with every effect switchable; optional 12-minute day/night cycle with lit windows (presentation only); vehicles pick one of four models from their id until vehicle types exist. | specs/018 clarifications |
| 2026-11-26 | Destination demand: passengers, mail and goods want a destination town. Half of a town's passengers and mail stay in their own town (delivered at another station of the company there), the other half go to other towns in proportion to population / distance (mail: population² / distance²); goods from factories go to goods-accepting towns by population / distance; food and raw cargo keep any destination. Units whose destination the player's lines cannot reach never appear at the station (unserved demand). Routes are computed over the lines of every mode (not the roads), chosen again at every transfer, and paid once on arrival; transfers record a per-leg credit in vehicle profit only. Waiting destination cargo expires after 60 days. | specs/013 clarifications |
| 2026-11-28 | The catalog includes at least twelve powered rail models plus wagons. Catenary and Aurora unlock together in 1960. Replace road vehicles, locomotives and wagons by model on the next depot visit, optionally at end of life. A retired model without a successor stays in service and prompts once. | specs/016 clarifications |
| 2026-11-28 | The traffic heatmap colours road and rail segments by actual vehicle passages in the last complete calendar month. Each train counts once per traversal. Line routes follow traversable roads and railways between stops and indicate disconnected legs. | specs/017 clarifications |
| 2026-11-28 | Company charts cover the latest 24 months including the current month and every game year. Income, expenses and deliveries are period totals. Company value is measured at period end, or to date for the open period. | specs/017 clarifications |
| 2026-11-28 | Line load factor is occupied unit-distance divided by offered capacity-distance over the reporting period. A line has one target departure interval, enforced independently for road and rail at the first stop. | specs/017 clarifications |

## Next steps

1. ~~Technical prototype: open a window and render voxel terrain.~~ Done on Windows (see [ARCHITECTURE.md](ARCHITECTURE.md)).
2. ~~Simulation benchmark: thousands of vehicles moving on a large map.~~ Done: one million
   independent vehicles tick in ~6 ms (see [ARCHITECTURE.md](ARCHITECTURE.md#benchmarks)).
3. Vehicle interactions: following distance, junctions, signals and blocks, keeping the tick parallel.
4. Rendering for large maps: greedy meshing, frustum culling and level of detail.
5. Visual quality: shadows, smaller voxels per tile, scenery (trees, buildings).
6. Test the prototype on Linux and macOS.

## Open questions

- Map generation approach.
- Multiplayer: in scope or not?
- Modding support.
- Final game UI technology.

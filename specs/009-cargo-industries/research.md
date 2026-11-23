# Research: Cargo Types and Industries

Feature 009 fills the world with the freight economy that map generation (004) reserves room for and
that stations (010) will later connect. Decisions below are limited to what this feature needs; money
(011), destination flows (013) and final art (018) stay out.

## R1. Cargo and industry definitions live in `Talt.Core` as embedded JSON

**Decision**: Add `src/Talt.Core/Content/Data/cargo.json` and `industries.json`, embedded in the Core
assembly (`LogicalName = "content/<file>"`), loaded and validated by `Talt.Core.Content.GameContent`.
Use `System.Text.Json` (part of .NET 10, no new package) with case-insensitive properties, comments and
trailing commas allowed. The loader is public so tests can feed malformed text; the default instance is
cached lazily in `GameContent.Default`.

**Rationale**: Constitution VII requires content in versioned data files validated at load; the
simulation needs the data, so it cannot live in the client (`Talt.Core` must not depend on
`Talt.Client`). Embedding avoids file-path and working-directory problems on the three CI platforms
and keeps a single deployable binary. `System.Text.Json` adds no dependency.

**Alternatives considered**: (a) client `DataFiles` — rejected, Core cannot read client resources;
(b) loose files next to the executable — rejected, breaks tools/tests and publish layouts;
(c) hard-coded tables — violates constitution VII.

## R2. Integer-only economy, one unit per cargo

**Decision**: Every cargo type declares its unit (`tonnes`, passengers, heads, bags, crates) and all
amounts are whole `int`s in that unit. Monthly production rates are integers per month; a per-mille
multiplier (1000 = 100 %) expresses each industry's current condition. No fractional cargo, no
floating point anywhere in `Talt.Core`.

**Rationale**: Constitution III and the existing `NoFloatingPointStateTests`; whole units are easy to
show, compare and replay. Per-mille multipliers give 0.1 % resolution without fractions.

**Alternatives considered**: fixed-point tonnes with 1/1000 units — unnecessary precision for
production; floating point — forbidden.

## R3. Industry storage: structure of arrays with per-cargo tables

**Decision**: `IndustryFleet` keeps parallel arrays for scalars (kind, id, x, z, rate, months
neglected, site index) and flat `int[]` tables indexed `slot * cargoCount + cargoIndex` for available
cargo, pending recipe input, accepted/shipped this month and accepted/shipped last month. Removal is
swap-with-last, like `VehicleFleet`; ids stay with the industry. A `Dictionary<int,int>` maps id to
slot for the information window, and a `Dictionary<int,int>` maps tile key to industry id for picking
and construction.

**Rationale**: The update loops touch a few scalar fields and one or two cargo cells per industry, so
SoA is cache-friendly and allocates nothing per tick. All cargo types exist for every kind, and the
largest realistic case (16 000 industries × ~16 cargo types) is a few MB, well inside the memory
budget.

**Alternatives considered**: one class/record per industry — allocation and pointer chasing, rejected;
an ECS — the project already chose packed arrays over Arch for vehicles and industries are simpler.

## R4. Placement: deterministic selection over feature 004's sites

**Decision**: `IndustryFleet.Create(content, map, startYear, DeterministicRandom random,
initialOccupancyPerMille?)` walks
`MapData.IndustrySites` in their deterministic order and, for about 75 % of them, picks a kind whose
placement rules include the site kind, whose era range contains the start year, and whose footprint
fits the reserved free square. Kinds are chosen by weighted random from a forked
`DeterministicRandom` stream (`Fork(0x1D05)`). Sites with no eligible kind stay free. Site index,
occupancy and a free-site list are retained; the map itself is not copied.

**Rationale**: Sites already guarantee buildable, spaced, flat land and encode Coast/Forest/Hills/
Plain. Reusing them keeps map and industry rules in one place, and the explicit seed keeps placement
reproducible and replayable. Leaving a quarter of the sites free makes the "new industries open"
story possible from game start.

**Alternatives considered**: placing industries anywhere on suitable terrain — duplicates feature
004's work and can overlap towns; occupying every site — no room for openings.

## R5. Lifecycle rules are data-driven and seeded

**Decision**: `industries.json` carries an `economy` block (initial occupancy, monthly drift, growth
per month of deliveries, months before decline, decline per month, months and probability for
closing, per-site opening chance, maximum openings per month, rate clamps). Monthly update per open
industry:

1. Rotate monthly statistics; `deliveredThisMonth > 0` resets neglect and adds growth, otherwise
   neglect increases and, after the configured months, the rate declines.
2. Add a small seeded drift (± 15 per mille by default) and clamp the rate.
3. If neglect and rate pass the closing thresholds, roll the seeded closing chance and remove the
   industry (freeing its site) when it succeeds.
4. For every free site, roll the opening chance; open a fitting kind for the current era, at most
   the configured maximum per month.

Daily update only accumulates production (R6); all rule changes happen on the monthly event, as
requested by the feature brief.

**Rationale**: Growth depends on deliveries, which arrive from feature 010; until then neglect makes
the world slowly decline, as clarified. Everything is integer and driven by the simulation's forked
stream, so replay reproduces it exactly.

**Alternatives considered**: fixed lifecycles — less alive; closing decided without randomness —
same result every game and no variety.

## R6. Production, processing and storage

**Decision**: A production entry is `{cargo, perMonth}`. Each day an industry adds
`monthly * rate / 1000 / daysInMonth` whole units of each produced cargo; the division remainder of
the month is added on the month's last day, so a month delivers exactly `monthly * rate / 1000`.
Produced cargo accumulates in the available store up to the kind's per-cargo cap; excess is discarded.
Deliveries enter through `IndustryFleet.Deliver`, used by feature 010:

- Cargo not accepted: refused, nothing changes.
- Cargo accepted without a recipe (power station coal): all of it is consumed and counted as accepted.
- Cargo accepted with a recipe: whole batches are converted immediately (`inputAmount` →
  `outputAmount`); a partial batch stays as pending input. If the output store is full, processing
  waits, leaving the input pending. Recipe output obeys the same cap.

**Rationale**: Matches the clarified rules, keeps processing automatic and immediate, never creates
cargo from nothing, and gives feature 010 a single entry point.

**Alternatives considered**: continuous flow processing — needs fractions; discarding partial batches —
loses cargo and confuses players.

## R7. Simulation integration, replay and fingerprints

**Decision**:

- `GameSimulation.Create(settings, mapData?, content?)` gains optional parameters. If `mapData` is
  null but `settings.Map` is set, the map is generated deterministically first (used by replay).
- `ScenarioSettings` gains an optional `MapSettings? Map`; `RecordedSession` writes it as an optional
  `"map"` text field (format version 1 stays, readers accept its absence) so replays recreate the
  same industries. Sessions without a map (the existing CI reference) create no industries.
- `GameSimulation` registers `Industries.AdvanceDay` and `Industries.AdvanceMonth` on the existing
  day/month events, after construction.
- `IndustryFleet.AppendTo` appends its state **only when it owns at least one industry**. A scenario
  without a map therefore has exactly the previous fingerprint, so existing goldens, the Windows
  reference session and recorded checkpoints keep matching; a scenario with industries hashes all
  industry arrays, its forked random state and a content fingerprint.

**Rationale**: Determinism and replay are non-negotiable, but changing every existing golden for an
empty system would obscure the real rule change and break the CI reference session for no benefit.
Conditional hashing is still complete: equal states hash equally and different industry states differ.

**Alternatives considered**: always hashing an empty marker — forces regenerating every golden;
storing the map itself in the session — huge and unnecessary because the settings reproduce it.

## R8. Client rendering and picking: data-driven box models, one instanced pipeline

**Decision**: Each industry kind declares a small list of boxes (offset, size, colour) as its
placeholder model. The client adds `IndustryInstance` (position, size, colour) and a box shader to the
existing instanced renderer, drawing one box per model box at the industry's ground position.
`GameSession.BuildIndustryInstances` rebuilds the list only when `IndustryFleet.Version` changes
(placement, opening or closing); `GameWindow` gains one call. Picking adds an `IndustryPickLayer`
(priority 75, between roads and vehicles) that tests the ray against the kind's box extent and returns
the industry id; the information window uses the same model bounds for its highlight.

**Rationale**: Boxes read as distinct silhouettes at map zoom, need no art pipeline, follow openings
and closures immediately, and reuse the vehicle instancing pattern already benchmarked. Industries are
not painted into voxels, so opening or closing one never forces a chunk remesh.

**Alternatives considered**: voxel buildings written into `VoxelWorld` — remeshing on every change and
extra coupling with feature 003; sprite overlays — wrong perspective and picking complexity.

## R9. Industry window and localization

**Decision**: `ObjectInfoWindow` gains an industry branch showing the localized kind name, every
production entry per month, accepted cargo and recipes, available and pending cargo, and accepted and
shipped totals for last month. All names, categories and units come from content keys resolved through
the existing `Strings` files; new keys go in both `Strings.en.json` and `Strings.es.json`. A client
test loads the default content and asserts that every referenced key exists in both files.

**Rationale**: Constitution VIII and the existing localization test only compare the two files'
keys; content keys could otherwise be missed.

## R10. Performance budgets and benchmarks

**Decision**: Add `IndustryBenchmarks` with a 4096 × 4096 map generated once in `GlobalSetup` and
1 000 industries placed with full occupancy for the measurement:

- `AdvanceDay` for all industries (SC-001, budget < 1 ms).
- `AdvanceMonth` for all industries (SC-002, budget < 5 ms).
- Initial placement on the 4096 map (informational).
- Full `GameSimulation.Tick` with 1 000 industries at a month boundary (integration cost).

No LINQ, no allocations and no per-industry virtual calls in the loops; record the measured numbers in
`docs/ARCHITECTURE.md`.

**Alternatives considered**: benchmarking only the full tick — hides which part regressed; a 1024
map — does not exercise the reference scale.

## R11. Validation rules and error messages

**Decision**: `GameContent.FromJson` throws `InvalidDataException` whose message starts with the data
file name and ends with the offending id or field. Checks: non-empty unique ids matching
`[a-z0-9-]+`; non-empty name/unit keys; category/destination consistency; at least one vehicle kind;
decay and floor ranges; known cargo in production, acceptance and recipes; positive amounts and
rates; unique entries per cargo; storage capacity for every produced, recipe-input and recipe-output
cargo; footprints and model boxes within 1–5 tiles and heights 1–12; era ranges 1800–9999 with start ≤
end; weights 1–1000; site-kind list non-empty; economy thresholds and clamps consistent. Tests cover
each rule with a minimal JSON document.

**Rationale**: Constitution VII demands clear errors at load; failing at startup beats corrupt state
during play.

## R12. Testing and golden strategy

**Decision**: xUnit tests in `tests/Talt.Core.Tests/Industries` cover loading/validation, placement
(determinism, density, suitability, no overlap, era), daily/monthly maths (exact month totals, caps,
growth, decline, closing, openings), deliveries and recipes, fingerprint sensitivity and parallel
equality. Picking and localization get client tests. Existing `GoldenFingerprintTests` stay unchanged
and are used to prove that a map-less scenario still matches; a new golden test builds a small
generated map with industries and asserts a fixed fingerprint at two ticks. `NoFloatingPointStateTests`
registers `GameContent`, the definition records, `IndustryFleet`, `IndustryInfo` and `ScenarioSettings`
(already registered) and `MapData` if it becomes reachable from simulation state.

**Rationale**: The feature adds a whole simulation system; golden coverage on both the empty and the
populated path catches accidental changes to the existing world and to the new one.

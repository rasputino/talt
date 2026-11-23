# Implementation Plan: Cargo Types and Industries

**Branch**: `009-cargo-industries` | **Date**: 2026-11-23 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/009-cargo-industries/spec.md`

## Summary

Add a data-driven freight economy to `Talt.Core`: cargo and industry definitions in two embedded JSON
files validated at load, industry instances placed on the industry sites that feature 004 already
reserves, daily production accumulation and monthly lifecycle changes driven by the simulation's
scheduled events, automatic processing of delivered cargo, and a small public API that feature 010
will use to load and unload cargo. The simulation gains an optional map (`ScenarioSettings.Map`) so
industries are reproducible from the map settings and recorded sessions; an empty industry fleet
contributes nothing to the state fingerprint, so existing goldens and recorded sessions stay valid.
The client shows industries with data-driven placeholder boxes through the existing instanced
renderer and adds an industry branch to the feature 005 information window and picking. The headless
runner gains `--map-text` so the economy can be run without a GPU. New benchmarks cover day/month
updates for 1 000 industries on a 4096 × 4096 map.

## Technical Context

**Language/Version**: C# (latest) on .NET 10

**Primary Dependencies**: none new. `System.Text.Json` (in-box) for content loading;
`System.IO.Hashing` already present for fingerprints. Placeholder models use the existing WebGPU
instanced pipeline.

**Storage**: Embedded resources `content/cargo.json` and `content/industries.json` inside
`Talt.Core`; recorded sessions gain an optional map text. No save format yet.

**Testing**: xUnit in `tests/Talt.Core.Tests/Industries` (content validation, placement determinism,
production/monthly maths, deliveries, lifecycle, fingerprints); client tests for picking and content
localization; existing golden tests prove map-less scenarios are unchanged; BenchmarkDotNet industry
benchmarks.

**Target Platform**: Windows, Linux x64/arm64, macOS arm64; identical results on all.

**Project Type**: Desktop game (Core library + client executable + headless tool + benchmarks).

**Performance Goals**: day update of 1 000 industries < 1 ms and month update < 5 ms (SC-001/002);
no allocation in the day/month loops; full tick budget unchanged (≤ 4 ms average on the reference
scenario); 4096 × 4096 map generation and frame time unaffected.

**Constraints**: integer-only simulation state; forked `DeterministicRandom`; parallel ticks equal
sequential; `Talt.Core` stays graphics-free; data errors stop startup with a clear message; industries
must never overlap or leave the map; client changes stay minimal and avoid feature 006's tool/HUD
layer.

**Scale/Scope**: about 1 000 industry sites on a 4096² map (feature 004 density), 11 industry kinds,
13 cargo types, up to 16 384² maps; placeholder models of 3–4 boxes; one information window per
selected industry.

## Constitution Check

*Initial gate and post-design re-check: PASS, no violations to justify.*

| Principle | Status | Notes |
|-----------|--------|-------|
| I Core/Client separation | PASS | content, placement, economy and tests in `Talt.Core`; client only renders, picks and formats text |
| II Performance | PASS | budgets measured by `IndustryBenchmarks`; no allocations in day/month loops; no world remeshing |
| III Deterministic | PASS | integer state, forked stream, event-driven, complete state in the fingerprint when industries exist |
| IV Tested increments | PASS | tests per story (load, placement, production, delivery, lifecycle); no GPU/timing |
| V Readable C# | PASS | small records, SoA arrays documented, one concern per file; no unsafe code |
| VI Original content | PASS | original cargo/industry names, data and placeholder shapes; no external assets |
| VII Data-driven | PASS | cargo, kinds, economy thresholds and model boxes in JSON; validation with file-named errors |
| VIII Docs/localization | PASS | English docs, architecture update, all player text through en/es keys |

## Project Structure

### Documentation (this feature)

```text
specs/009-cargo-industries/
├── plan.md
├── research.md            # R1–R12
├── data-model.md          # content records, SoA arrays, lifecycle, client types
├── quickstart.md          # validation and developer checks
├── contracts/
│   ├── content-schema.md  # JSON shape, validation, default tables
│   ├── industry-api.md    # Core API signatures and guarantees
│   └── cli.md             # client, recorded session, headless, benchmarks
├── checklists/requirements.md
└── tasks.md               # /speckit-tasks
```

### Source Code (repository root)

```text
src/Talt.Core/
├── Content/                       # NEW
│   ├── Data/cargo.json
│   ├── Data/industries.json
│   ├── CargoDefinition.cs         # enums, definitions
│   ├── IndustryKindDefinition.cs  # production, recipes, stock, model boxes
│   ├── EconomyRules.cs
│   ├── GameContent.cs             # catalog, validation, fingerprint, embedded loading
│   └── ContentJson.cs             # DTOs + strict mapping
├── Industries/                    # NEW
│   ├── IndustryFleet.cs           # SoA state, day/month updates, fingerprint
│   ├── IndustryPlacement.cs       # sites → initial industries, openings
│   ├── IndustryEconomy.cs         # production, processing, lifecycle helpers
│   ├── IndustryInfo.cs            # read model for the window
│   └── IndustryDeliveryResult.cs
├── Simulation/GameSimulation.cs   # content, industries, Create overload, events, hash
├── Scenarios/ScenarioSettings.cs  # optional MapSettings
├── Commands/RecordedSession.cs    # optional map text
├── Commands/SessionReplayer.cs    # map recreation through Create
└── Talt.Core.csproj               # embed Content/Data/*.json

src/Talt.Client/
├── GameSetup.cs                   # map first, then simulation with the map
├── GameSession.cs                 # industry instances (cached by Version)
├── GameWindow.cs                  # one line: renderer.UpdateIndustries(...)
├── Picking/Picking.cs             # PickKind.Industry, id, IndustryPickLayer
├── Screens/InGameScreen.cs        # pick context and selection refresh
├── Screens/Windows.cs             # industry information window
├── Rendering/WebGpuRenderer.cs    # industry instance buffer + pipeline
├── Rendering/Shaders/industries.wgsl
├── Localization/Strings.en.json
└── Localization/Strings.es.json

tools/Talt.Headless/               # run --map-text, industry count in summary
benchmarks/Talt.Benchmarks/IndustryBenchmarks.cs
tests/Talt.Core.Tests/Industries/  # new test files
tests/Talt.Client.Tests/Picking/IndustryPickTests.cs
tests/Talt.Client.Tests/Localization/ContentLocalizationTests.cs
docs/ARCHITECTURE.md               # industry system, data, fingerprints, numbers
docs/VISION.md                     # cargo list and lifecycle decision
```

**Structure Decision**: two new namespaces inside the existing `Talt.Core` project (`Content` and
`Industries`); no new projects. Client edits are deliberately confined to display and picking:
`GameWindow.cs` receives one rendering call, and the tool/HUD layer owned by feature 006 is not
touched. Feature 019 (audio) has no overlapping files.

## Implementation phases

1. **Content** — definitions, JSON DTOs, loader, validation, default data, content fingerprint,
   localization keys. Tests first for every validation rule.
2. **Placement and state** — `IndustryFleet` SoA, `IndustryPlacement` from `MapData.Sites`,
   occupancy lookup, id/slot maps, empty fleet, fingerprint (conditional), registration in
   `NoFloatingPointStateTests`.
3. **Economy** — daily production split across days, storage caps, deliveries and recipes,
   monthly statistics, growth/decline/closing/openings, seeded stream. Unit tests per story.
4. **Simulation integration** — `GameSimulation` content/industries/events/Create overload,
   `ScenarioSettings.Map`, recorded session map text, replay path; verify existing goldens unchanged.
5. **Client** — map-before-simulation order, industry instances and shader, picking layer, information
   window, strings, client tests.
6. **Tools and performance** — headless `--map-text` and summary, `IndustryBenchmarks`, measurements
   recorded in `ARCHITECTURE.md`.
7. **Verification** — full tests, fingerprint gate, benchmark budget, developer in-window check,
   documentation, analyze fixes.

## Dependencies and licensing

No new package. `System.Text.Json` and `System.IO.Hashing` ship with .NET (MIT licence), compatible
with a closed commercial game. All cargo/industry names and placeholder models are original.

## Risks and mitigations

| Risk | Mitigation |
|------|------------|
| Merge conflict with feature 006 in `GameWindow.cs`/HUD | only one call added to `GameWindow`; no tool or HUD code touched; industry UI goes through the existing info window |
| Existing goldens/reference session break | empty fleet appends nothing; a dedicated test asserts the old values; the populated path gets its own golden |
| Replay of old sessions fails after `ScenarioSettings` changes | `map` is optional; old JSON parses exactly as before; tested |
| Data changes break determinism silently | content fingerprint is part of the state when industries exist; validation tests pin the default data |
| Monthly update cost on large maps | openings iterate only free sites and are capped per month; benchmark at the reference scale |
| Player cannot understand a window with many cargo rows | one line per produced item/recipe, units from data, "waiting" vs "last month" separated |

## Complexity Tracking

No constitution violations. Deliberate complexities, justified in research: a per-cargo SoA table set
(R3) for cache-friendly and allocation-free updates, and an optional map in `ScenarioSettings` (R7)
so recorded sessions replay the industries without storing a whole map.

# Tasks: Cargo Types and Industries

**Input**: Design documents from `specs/009-cargo-industries/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/

**Tests**: Included, as required by the constitution (all Core logic is tested with or before the
code). Each story writes its tests first.

**Organization**: Tasks are grouped by user story so each can be implemented and verified on its own.
Paths are relative to the repository root.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on an incomplete task)
- **[Story]**: US1–US6 from spec.md
- Every task names the files it touches

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: New namespaces, embedded data files and player-facing keys.

- [X] T001 Create `src/Talt.Core/Content/` and `src/Talt.Core/Industries/` folders and embed `Content/Data/*.json` as `content/<filename>` resources in `src/Talt.Core/Talt.Core.csproj`
- [X] T002 [P] Add the `industry.*`, `cargo.*`, `cargo.category.*`, `unit.*` and `info.industry.*` keys to `src/Talt.Client/Localization/Strings.en.json` and `Strings.es.json` (same key set in both)

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Content, placement and simulation wiring that every story uses.

**⚠️ CRITICAL**: No user story can start until this phase is complete.

- [X] T003 [P] Add `CargoCategory`, `VehicleKind`, `CargoDefinition`, `ProductionEntry`, `RecipeDefinition`, `StockDefinition` and `ModelBox` in `src/Talt.Core/Content/` following `data-model.md` (`CargoDefinition.Id`, `CargoDefinition.NameKey`, `CargoDefinition.Category`, `CargoDefinition.UnitKey`, `CargoDefinition.DestinationRequired`, `CargoDefinition.ValueDecayPerDayPerMille`, `CargoDefinition.MinimumValuePercent`, `CargoDefinition.Color`, `CargoDefinition.VehicleKinds`, `ProductionEntry.CargoId`, `ProductionEntry.PerMonth`, `RecipeDefinition.InputCargoId`, `RecipeDefinition.InputAmount`, `RecipeDefinition.OutputCargoId`, `RecipeDefinition.OutputAmount`, `StockDefinition.CargoId`, `StockDefinition.Capacity`, `ModelBox.X/Y/Z/Width/Depth/Height/Color`)
- [X] T004 [P] Add `EconomyRules` and `IndustryKindDefinition` in `src/Talt.Core/Content/` following `data-model.md` (`IndustryKindDefinition.Id`, `IndustryKindDefinition.NameKey`, `IndustryKindDefinition.FootprintWidth`, `IndustryKindDefinition.FootprintDepth`, `IndustryKindDefinition.Production`, `IndustryKindDefinition.Accepts`, `IndustryKindDefinition.Recipes`, `IndustryKindDefinition.Stock`, `IndustryKindDefinition.Model`, `IndustryKindDefinition.SiteKinds` as `IReadOnlyList<SiteKind>` without `Town`, `IndustryKindDefinition.EraStart`, `IndustryKindDefinition.EraEnd`, `IndustryKindDefinition.Weight`, and every `EconomyRules` field including `EconomyRules.InitialOccupancyPercent`, `EconomyRules.DriftPerMille`, `EconomyRules.GrowthPerMille`, `EconomyRules.DeclineAfterMonths`, `EconomyRules.DeclinePerMille`, `EconomyRules.CloseAfterMonths`, `EconomyRules.CloseChancePerMille`, `EconomyRules.CloseAtRatePerMille`, `EconomyRules.OpeningChancePerMillePerSite`, `EconomyRules.MaxOpeningsPerMonth`, `EconomyRules.MinRatePerMille`, `EconomyRules.MaxRatePerMille`)
- [X] T005 [P] Write the default `src/Talt.Core/Content/Data/cargo.json` with the 13 cargo types of `contracts/content-schema.md`
- [X] T006 [P] Write the default `src/Talt.Core/Content/Data/industries.json` with the economy block and 11 industry kinds and box models of `contracts/content-schema.md`
- [X] T007 Implement `GameContent` (embedded loading, id indexes, `Default`, `FromJson`, `Fingerprint`) and full validation with file-named `InvalidDataException`s in `src/Talt.Core/Content/GameContent.cs`; add `tests/Talt.Core.Tests/Industries/GameContentTests.cs` covering every rule of `contracts/content-schema.md` and one malformed JSON per rule
- [X] T008 Add optional `MapSettings? Map` to `src/Talt.Core/Scenarios/ScenarioSettings.cs` with delegated validation (`data-model.md`, `ScenarioSettings.Map`) and tests for equality/validation in `tests/Talt.Core.Tests/Scenarios/ScenarioSettingsTests.cs`
- [X] T009 Implement the `IndustryFleet` SoA skeleton and `IndustryInfo` in `src/Talt.Core/Industries/IndustryFleet.cs` and `IndustryInfo.cs` (scalar arrays `_kind`, `_id`, `_x`, `_z`, `_ratePerMille`, `_monthsNeglected`, `_siteIndex`, `_deliveredThisMonth`; per-cargo tables `_available`, `_pendingInput`, `_acceptedThisMonth`, `_acceptedLastMonth`, `_shippedThisMonth`, `_shippedLastMonth`; maps `_tileOwner`, `_slotById`; free-site list, `CreateEmpty`, accessors, `Version`, conditional `AppendTo`), all per `data-model.md` `IndustryFleet`; register the new types in `tests/Talt.Core.Tests/Determinism/NoFloatingPointStateTests.cs` and add tests for empty state and fingerprint-unchanged behaviour
- [X] T010 Implement initial placement in `src/Talt.Core/Industries/IndustryPlacement.cs` (weighted kind by site kind/era/footprint, `InitialOccupancyPercent`, free-site list, no overlap, map bounds) and `tests/Talt.Core.Tests/Industries/IndustryPlacementTests.cs` over several seeds and a non-square map (`data-model.md`, `IndustryKindDefinition.FootprintWidth/FootprintDepth`, `SiteKind`)
- [X] T011 Wire content and industries into `src/Talt.Core/Simulation/GameSimulation.cs`: `Content`, `Industries`, `Create(settings, map?, content?)` with map generation from `settings.Map` and forked `0x1D05` stream, and hash industries after vehicles; add `tests/Talt.Core.Tests/Industries/IndustrySimulationTests.cs` (empty and populated creation, deterministic content)
- [X] T012 Add the optional `"map"` text to `src/Talt.Core/Commands/RecordedSession.cs` and make `src/Talt.Core/Commands/SessionReplayer.cs` recreate it; add round-trip and old-session tests in `tests/Talt.Core.Tests/Commands/RecordedSessionTests.cs`

**Checkpoint**: content loads, industries are placed on a map, the simulation owns them and old sessions still work. Stories can now proceed.

---

## Phase 3: User Story 1 — See industries on the map (Priority: P1) 🎯 MVP

**Goal**: Industries are visible as distinct placeholder buildings on a generated map.

**Independent Test**: Generate a map with several seeds and confirm that industries appear on the right sites, with kind-specific shapes, and that a map-less scenario is unchanged.

### Tests for User Story 1

- [X] T013 [P] [US1] Add `tests/Talt.Client.Tests/Rendering/IndustryInstanceTests.cs` verifying one instance per model box, placement at the industry's ground height, and rebuilding only when `IndustryFleet.Version` changes
- [X] T014 [P] [US1] Add headless tests in `tests/Talt.Headless.Tests/HeadlessArgumentsTests.cs` for `run --map-text` (valid, invalid, and no-map default)

### Implementation for User Story 1

- [X] T015 [P] [US1] Add `IndustryInstance` and `src/Talt.Client/Rendering/Shaders/industries.wgsl` (instanced unit cube with per-instance size and colour)
- [X] T016 [US1] Extend `src/Talt.Client/Rendering/WebGpuRenderer.cs` with the industry instance buffer, pipeline and render-pass draw, mirroring `UpdateVehicles`
- [X] T017 [US1] Add `GameSession.IndustryInstances` built from `Industries` model boxes and cached by `Version` in `src/Talt.Client/GameSession.cs`; reorder `src/Talt.Client/GameSetup.cs` to generate the map before `GameSimulation.Create(settings, map)`; set `MapSettings` in `NewGameOptions.ToScenario()` so `ScenarioSettings.Map` carries into recordings (`data-model.md`, `ScenarioSettings.Map`)
- [X] T018 [US1] Add the single `_renderer.UpdateIndustries(_session.IndustryInstances);` call in `src/Talt.Client/GameWindow.cs` (coordinate this one line with feature 006; no HUD or tool code)
- [X] T019 [P] [US1] Add `run --map-text` parsing, validation and the `industries:` summary line to `tools/Talt.Headless/HeadlessArguments.cs` and `tools/Talt.Headless/Program.cs`

**Checkpoint**: run the client and the headless command; industries are visible and reproducible.

---

## Phase 4: User Story 2 — Inspect an industry (Priority: P1)

**Goal**: Clicking an industry opens its information window.

**Independent Test**: Open windows for a producer and a processor; check production, acceptance, recipe, available and last-month rows; close and reopen after a month.

### Tests for User Story 2

- [X] T020 [P] [US2] Add `tests/Talt.Client.Tests/Picking/IndustryPickTests.cs` (ray hits the model box, priority below vehicles, id/key/`SameObject`, misses when the ray is behind terrain)
- [X] T021 [P] [US2] Add client tests for the industry window texts in `tests/Talt.Client.Tests/Screens/IndustryWindowTests.cs` (producer, processor, gone industry)

### Implementation for User Story 2

- [X] T022 [P] [US2] Add `PickKind.Industry`, `PickResult.IndustryId`, the `industry:<id>` key and `SameObject` handling in `src/Talt.Client/Picking/Picking.cs`
- [X] T023 [US2] Add `IndustryPickLayer` (priority 75, kind footprint/model height bounds), extend `PickContext` and register the layer in `src/Talt.Client/Screens/InGameScreen.cs`
- [X] T024 [US2] Add the industry branch to `ObjectInfoWindow` in `src/Talt.Client/Screens/Windows.cs`: localized kind name, production per month, accepted cargo, recipes, available and pending cargo, accepted/shipped last month, and a gone message; reuse the existing highlight bounds

**Checkpoint**: pick and inspect industries in the client; windows track the same object.

---

## Phase 5: User Story 3 — Deliver raw materials anywhere accepted (Priority: P1)

**Goal**: A delivery is accepted only by industries that list the cargo, and destination-free cargo needs no chosen destination.

**Independent Test**: Deliver the same cargo to every accepting kind and to a non-accepting one; check counters and state.

### Tests for User Story 3

- [X] T025 [P] [US3] Add `tests/Talt.Core.Tests/Industries/IndustryDeliveryTests.cs` for accepted/refused deliveries, unknown ids/cargo, non-positive amounts, and accepted counters

### Implementation for User Story 3

- [X] T026 [US3] Implement `IndustryFleet.Deliver` acceptance/refusal and `IndustryDeliveryResult` in `src/Talt.Core/Industries/IndustryFleet.cs` and `IndustryDeliveryResult.cs` per `contracts/industry-api.md` (`data-model.md`, `IndustryDeliveryResult.Outcome`, `IndustryDeliveryResult.Accepted`, `IndustryDeliveryResult.Refused`, `IndustryDeliveryResult.Consumed`, `IndustryFleet._acceptedThisMonth`)
- [X] T027 [US3] Consume accepted cargo without a recipe (power station coal) and record it in the accepted counters (`data-model.md`, `IndustryKindDefinition.Accepts`)
- [X] T028 [P] [US3] Expose `TryGetIndustryAt`/`IsOccupied` for feature 006 and add occupancy tests for every footprint tile in `tests/Talt.Core.Tests/Industries/IndustryOccupancyTests.cs`

**Checkpoint**: deliveries are accepted exactly where the data says, and construction can query occupancy.

---

## Phase 6: User Story 4 — Processing industries turn cargo into other cargo (Priority: P2)

**Goal**: Recipe inputs become outputs immediately, without creating or losing cargo.

**Independent Test**: Deliver batches to a sawmill, a steel mill and a food plant; check outputs, pending input, full output stores and unknown cargo.

### Tests for User Story 4

- [X] T029 [P] [US4] Add `tests/Talt.Core.Tests/Industries/IndustryProcessingTests.cs` for whole batches, partial batches, multiple recipes, output-full postponement, caps and input ≠ output

### Implementation for User Story 4

- [X] T030 [US4] Implement recipe conversion in `src/Talt.Core/Industries/IndustryEconomy.cs` (called from `IndustryFleet.Deliver`) using `_pendingInput`, respecting input/output stock capacities (`data-model.md`, `IndustryKindDefinition.Production`, `IndustryKindDefinition.Recipes`, `IndustryFleet._pendingInput`, `IndustryKindDefinition.Stock`, `IndustryDeliveryResult.ConvertedCargoId`, `IndustryDeliveryResult.ConvertedAmount`)
- [X] T031 [US4] Implement the output-full rule: keep input pending and process later instead of destroying it; add the regression test to `IndustryProcessingTests.cs`
- [X] T032 [P] [US4] Implement `IndustryFleet.Take` for feature 010 (never takes more than available, increments shipped) and its tests in `IndustryProcessingTests.cs`

**Checkpoint**: processing chains work and never invent or lose cargo.

---

## Phase 7: User Story 5 — Industries change over time (Priority: P2)

**Goal**: Production accrues daily, statistics rotate monthly, and industries grow, decline, close and open deterministically.

**Independent Test**: Run years with and without deliveries; check exact monthly totals, growth, decline, closures, openings and identical histories for the same seed.

### Tests for User Story 5

- [X] T033 [P] [US5] Add `tests/Talt.Core.Tests/Industries/IndustryProductionTests.cs` for exact month totals split over days, leap years, rate multipliers, caps and zero rates
- [X] T034 [P] [US5] Add `tests/Talt.Core.Tests/Industries/IndustryLifecycleTests.cs` for statistics rotation, growth with deliveries, decline after 24 months, seeded closures after 5 years, openings on free sites, era filtering, site reuse and determinism

### Implementation for User Story 5

- [X] T035 [US5] Implement daily production accumulation in `IndustryFleet.AdvanceDay`/`src/Talt.Core/Industries/IndustryEconomy.cs` (whole units, remainder on the last day, caps, no allocations) per research R6 (`data-model.md`, `IndustryFleet._available`, `IndustryKindDefinition.Production`, `IndustryFleet.StockCapacityAt`, `EconomyRules.MaxRatePerMille`)
- [X] T036 [US5] Implement monthly statistics rotation, seeded drift, growth/decline and rate clamping in `IndustryFleet.AdvanceMonth`/`IndustryEconomy.cs` (`data-model.md`, `IndustryFleet._acceptedThisMonth`, `IndustryFleet._acceptedLastMonth`, `IndustryFleet._shippedThisMonth`, `IndustryFleet._shippedLastMonth`, `EconomyRules.DriftPerMille`, `EconomyRules.GrowthPerMille`, `EconomyRules.DeclineAfterMonths`, `EconomyRules.DeclinePerMille`, `EconomyRules.MinRatePerMille`)
- [X] T037 [US5] Implement seeded closing and openings with site release/reuse and `Version` bumps in `IndustryEconomy.cs`/`IndustryPlacement.cs` (`data-model.md`, `EconomyRules.CloseAfterMonths`, `EconomyRules.CloseChancePerMille`, `EconomyRules.CloseAtRatePerMille`, `EconomyRules.OpeningChancePerMillePerSite`, `EconomyRules.MaxOpeningsPerMonth`, `IndustryKindDefinition.EraStart`, `IndustryKindDefinition.EraEnd`)
- [X] T038 [US5] Register `AdvanceDay`/`AdvanceMonth` on `GameSimulation.Events` in deterministic order and add `tests/Talt.Core.Tests/Industries/IndustryDeterminismTests.cs` (parallel = sequential, replay, populated golden fingerprint at two ticks, empty golden unchanged)

**Checkpoint**: industries produce and change on schedule; replay matches.

---

## Phase 8: User Story 6 — Cargo types have properties (Priority: P3)

**Goal**: The player can read each cargo's category, unit, value decay and allowed vehicles.

**Independent Test**: Read a cargo's information and compare it with `cargo.json`.

### Tests for User Story 6

- [X] T039 [P] [US6] Add `tests/Talt.Client.Tests/Localization/ContentLocalizationTests.cs` checking every `nameKey`/`unitKey`/category key from `GameContent.Default` exists in both `Strings.en.json` and `Strings.es.json`

### Implementation for User Story 6

- [X] T040 [US6] Add the cargo-properties section to the industry window (category, unit, value decay per day, minimum value and allowed vehicle kinds) using localized keys in `src/Talt.Client/Screens/Windows.cs`
- [X] T041 [P] [US6] Add Core tests asserting the default cargo properties match `contracts/content-schema.md` (decay, floor, destination flags, vehicle kinds) in `GameContentTests.cs`

**Checkpoint**: cargo properties are visible and localized.

---

## Phase 9: Polish & Cross-Cutting Concerns

**Purpose**: Performance, docs and end-to-end verification.

- [X] T042 [P] Add `benchmarks/Talt.Benchmarks/IndustryBenchmarks.cs`: 4096 × 4096 map in `GlobalSetup`, 1 000 industries, day update, month update, placement and a full tick at a month boundary, with `[MemoryDiagnoser]`
- [X] T043 Run the industry benchmarks and record the measured numbers and budgets in `docs/ARCHITECTURE.md`; compare the full tick with the feature 001 baseline
- [X] T044 [P] Add the industry/cargo section to `docs/ARCHITECTURE.md` (data files, SoA, events, fingerprint conditional, client rendering/picking, performance numbers)
- [X] T045 [P] Record the cargo list, lifecycle and 75 % occupancy decisions in `docs/VISION.md` (decision log)
- [X] T046 [P] Update `README.md` and `tools/Talt.Headless` usage with `--map-text` and the industry output
- [X] T047 Run `dotnet test --filter "Category!=Gpu"`, `dotnet test tests/Talt.Core.Tests --filter "FullyQualifiedName~GoldenFingerprint"` and `pwsh ci/check-fingerprints.ps1`; if golden values change on purpose, update both `tests/Talt.Core.Tests/Determinism/GoldenFingerprintTests.cs` and `ci/golden-fingerprints.json` and say so in the commit message; fix any failure
- [X] T048 Verify the quickstart end-to-end, launch the client for the developer and ask what the title bar/screen shows (synthetic input cannot reach GLFW)
- [X] T049 Mark completed tasks in this file, apply any `/speckit-analyze` fixes, and ask the developer before committing

---

## Dependencies & Execution Order

### Phase dependencies

- **Setup (Phase 1)**: no dependencies.
- **Foundational (Phase 2)**: depends on Setup; blocks every user story.
- **User stories (Phases 3–8)**: all depend on Foundational.
  - US1 (visible) goes first and provides the client path.
  - US2 (inspect) depends on US1 for picking/rendering bounds.
  - US3 (delivery) is independent of US1/US2 and can start after Foundational.
  - US4 (processing) depends on US3 (`Deliver`).
  - US5 (lifecycle) depends on Foundational; integrates with US4 for delivered input.
  - US6 (cargo properties) depends on US2 for the window.
- **Polish (Phase 9)**: depends on all stories.

### Story order

`US1 → US2 → US6` (client path) and `US3 → US4 → US5` (economy path) can run in parallel by two
developers after Foundational; US5 should be merged before the final benchmarks.

### Parallel opportunities

- T002, T005, T006, T008 can run in parallel once T003/T004 exist.
- T013/T014 and T015 can run in parallel at the start of US1.
- T020/T021 and T022 can run in parallel at the start of US2.
- T025/T028 can run in parallel with T026.
- T029/T032 can run in parallel with T030.
- T033/T034 can run in parallel with T035.
- T039/T041 and T040 can run in parallel.
- T042, T044, T045, T046 are independent.

## Implementation Strategy

### MVP first

Setup → Foundational → US1 gives a generated world with visible, reproducible industries (the
feature's minimum value). Validate with the headless `run --map-text` and the client.

### Incremental delivery

1. Foundational + US1: industries exist and are visible.
2. US2 + US6: the player can understand them.
3. US3 + US4: deliveries and processing work for feature 010.
4. US5: the world evolves on schedule; determinism and goldens verified.
5. Polish: benchmarks, docs, full verification.

### Notes

- Write each story's tests first and see them fail before implementing.
- No task may add floating point to `Talt.Core`; run `NoFloatingPointStateTests` after adding state.
- Do not touch feature 006's tool/HUD files; the only `GameWindow.cs` edit is T018.
- Do not commit, push or merge without the developer's explicit approval.

# Tasks: Visual Quality

**Input**: Design documents from `specs/018-visual-quality/` (plan.md, spec.md, research.md,
data-model.md, contracts/, quickstart.md)

**Tests**: required by the constitution (IV) for all pure logic; GPU-only tests carry
`Category=Gpu`.

**Format**: `- [ ] T### [P?] [Story?] Description with file path`

## Phase 1: Setup

- [X] T001 Take "before" screenshots and FPS on `main` (2048 seed 7 zoom 60/600, 4096 default) with the client built from `main` into a scratch folder; store them under the shared notes folder for the side-by-side comparison (SC-003)
- [X] T002 Create folders `src/Talt.Core/Meshing/Models/`, `src/Talt.Client/Assets/Models/`, `src/Talt.Client/Rendering/Data/`, `tests/Talt.Core.Tests/Meshing/Models/`; embed `Assets/Models/*.vox` as `models/<file>` in `src/Talt.Client/Talt.Client.csproj`
- [X] T003 Create console project `tools/Talt.ModelGen/Talt.ModelGen.csproj` (references Talt.Core) and add it to `Talt.slnx`

## Phase 2: Foundational (blocks all stories)

- [X] T004 Add `// #include "file.wgsl"` expansion to `WebGpuShaders.Load` in `src/Talt.Client/Rendering/UiRenderer.cs` (one level, no cycles) with a unit test in `tests/Talt.Client.Tests/Rendering/ShaderIncludeTests.cs`
- [X] T005 Write `src/Talt.Client/Rendering/Shaders/lighting.wgsl`: shared `Uniforms` (view_proj, sun dir/colour, sky/ground ambient, camera+fog, sky colour, cascade matrices/splits, time, exposure, AO strength, flags, palette) and functions `shade(albedo, normal, world_pos, ao)`, `apply_fog`, `tone_map` (research R2)
- [X] T006 Extend the `Uniforms` struct in `src/Talt.Client/Rendering/WebGpuRenderer.cs` to match `lighting.wgsl`; compute the tone-mapped clear colour on the CPU (`ToneMap` helper in `src/Talt.Client/Rendering/Lighting.cs`, tested in `tests/Talt.Client.Tests/Rendering/LightingTests.cs`)
- [X] T007 Switch `voxel.wgsl`, `roads.wgsl`, `towns.wgsl`, `boxes.wgsl`, `vehicles.wgsl`, `industries.wgsl`, `highlight.wgsl` in `src/Talt.Client/Rendering/Shaders/` to the include (no visual change other than tone mapping)
- [X] T008 Create group 1 frame resources (`src/Talt.Client/Rendering/FrameResources.cs`: shadow array texture or 1×1 dummy, comparison sampler, water texture or dummy, linear sampler, model palette texture or dummy) and the two pipeline layouts (main: group 0+1, shadow: group 0) in `WebGpuRenderer.cs`; pass both layouts to all world renderers
- [X] T009 Add `src/Talt.Client/Rendering/PipelineVariants.cs` (pipelines per sample count, created on first use) and use it in `WebGpuRenderer`, `BoxRenderer`, `HighlightRenderer`, `RoadRenderer`, `TownRenderer`
- [X] T010 Add `GraphicsSettings` (data-model.md GraphicsSettings.*; presets loader from `src/Talt.Client/Rendering/Data/graphics-presets.json`, validation, Custom detection) in `src/Talt.Client/GraphicsSettings.cs` with tests in `tests/Talt.Client.Tests/Rendering/GraphicsSettingsTests.cs`
- [X] T011 Persist `graphics` in `src/Talt.Client/PlayerSettings.cs` (version 4, per-property fallback per contracts/graphics-settings.md) with tests in `tests/Talt.Client.Tests/PlayerSettingsTests.cs`
- [X] T012 Add `--graphics low|medium|high` to `src/Talt.Client/Program.cs` and pass the active `GraphicsSettings` to `WebGpuRenderer` through a `SetGraphics(GraphicsSettings)` method applied each frame
- [X] T013 Add MSAA 4×: multisampled colour and depth targets with resolve into the surface in `WebGpuRenderer.Draw`/`Resize`, chosen by `GraphicsSettings.AntiAliasing`

**Checkpoint**: game renders as before with tone mapping; AA switchable; settings saved.

## Phase 3: User Story 1 — Soft shadows (P1)

**Goal**: terrain, roads, buildings, trees, vehicles cast filtered cascaded shadows.
**Independent test**: shadows on/off at close, medium, far zoom on a town and a hill.

- [X] T014 [P] [US1] Implement `ShadowCascades` (split distances, bounding-sphere fit, light view/projection, texel snapping, light frustum) in `src/Talt.Client/Rendering/ShadowCascades.cs` with tests in `tests/Talt.Client.Tests/Rendering/ShadowCascadesTests.cs` (stability under camera translation smaller than a texel, coverage of the slice)
- [X] T015 [US1] Implement `ShadowRenderer` (shadow array texture per quality, per-cascade uniform buffers and bind groups, per-cascade depth passes) in `src/Talt.Client/Rendering/ShadowRenderer.cs`
- [X] T016 [US1] Add `PrepareShadows(Frustum lightFrustum, int cascade)` and `EncodeShadow(pass, cascade)` to `src/Talt.Client/Rendering/WorldRenderer.cs` (regions selected by light frustum, drawable LOD) and a depth-only voxel pipeline in `WebGpuRenderer.cs`
- [X] T017 [P] [US1] Add depth-only shadow pipelines and `EncodeShadow` to `RoadRenderer.cs`, `TownRenderer.cs`, `BoxRenderer.cs` (opaque only) and the vehicle/industry pipelines in `WebGpuRenderer.cs`
- [X] T018 [US1] Shadow lookup with normal offset, PCF taps per quality and distance fade in `lighting.wgsl`; cascade data in uniforms
- [X] T019 [US1] Show cascades × size and shadow draws in F3 / `--stats` (`src/Talt.Client/Rendering/RenderStatistics.cs`, title/overlay in `GameWindow.cs`)

**Checkpoint**: US1 verifiable with box placeholders; run the client and check (constitution workflow).

## Phase 4: User Story 2 — Finer detail: model pipeline (P1)

**Goal**: objects drawn from 1/8-tile voxel models with AO and detail levels.
**Independent test**: close zoom on vehicles and trees shows voxel models; far zoom clean.

- [X] T020 [P] [US2] `VoxModel` (data-model.md VoxModel.SizeX/SizeY/SizeZ/Voxels/Palette) in `src/Talt.Core/Meshing/Models/VoxModel.cs`
- [X] T021 [P] [US2] `VoxReader` and `VoxFormatException` per contracts/vox-format.md in `src/Talt.Core/Meshing/Models/VoxReader.cs`; tests (valid file, unknown chunks skipped, bad magic/version, truncated chunk, voxel outside size, missing RGBA, axis conversion) in `tests/Talt.Core.Tests/Meshing/Models/VoxReaderTests.cs`
- [X] T022 [P] [US2] `VoxWriter` in `src/Talt.Core/Meshing/Models/VoxWriter.cs` with round-trip tests in `tests/Talt.Core.Tests/Meshing/Models/VoxWriterTests.cs`
- [X] T023 [US2] `ModelVertex` (data-model.md ModelMesh/ModelVertex.X2/Y2/Z2/FaceAo/Color) and `ModelMesh` in `src/Talt.Core/Meshing/Models/ModelMesh.cs`; `ModelMesher` (greedy, AO, diagonal flip, LOD 1–2 downsampling) in `src/Talt.Core/Meshing/Models/ModelMesher.cs`; tests (single voxel = 6 quads, merged slab, AO corners, LOD keeps silhouette) in `tests/Talt.Core.Tests/Meshing/Models/ModelMesherTests.cs`
- [X] T024 [US2] `ModelLibrary` (models.json per contracts/model-pipeline.md and data-model.md Model assignment.key/variants/file/paint/glow/yaw; validation errors; mesh building; one vertex/index buffer; palette texture rows with role alpha) in `src/Talt.Client/Rendering/ModelLibrary.cs` with validation tests in `tests/Talt.Client.Tests/Rendering/ModelLibraryTests.cs`
- [X] T025 [US2] `ModelBatch` (data-model.md ModelInstance.PositionYaw/TintVariation, ModelBatch; LOD by distance × model detail, frustum culling with shadow reach (bounding radius + height × 0.7), counting sort into buckets, no per-frame allocation) in `src/Talt.Client/Rendering/ModelBatch.cs` with tests in `tests/Talt.Client.Tests/Rendering/ModelBatchTests.cs`
- [X] T026 [US2] `models.wgsl` (rotate by yaw, palette lookup with paint/glow roles, AO, shared lighting) and `ModelRenderer` (main and shadow pipelines, upload, draws per bucket) in `src/Talt.Client/Rendering/Shaders/models.wgsl` and `src/Talt.Client/Rendering/ModelRenderer.cs`; wire into `WebGpuRenderer` world and shadow passes
- [X] T027 [US2] Model generator foundation in `tools/Talt.ModelGen/` (`Program.cs`, `VoxelCanvas` helpers: boxes, gradients, deterministic noise, palette builder) writing to `src/Talt.Client/Assets/Models/`
- [X] T028 [US2] Generate vehicle models (car, van, bus, truck; paint index) and tree models (broadleaf, conifer, birch × 2–3 variants) in `tools/Talt.ModelGen/`; register keys in `src/Talt.Client/Rendering/Data/models.json`
- [X] T029 [US2] Vehicles as models: `GameSession.BuildInstances` appends model instances (variant from id, yaw from direction, tint from colour) when `vehicle` has a model, else boxes (`src/Talt.Client/GameSession.cs`, `GameWindow.cs`)
- [X] T030 [US2] Trees as models: `TreeInstances` emits model instances per tree kind (variant from scenery variant) with boxes as fallback (`src/Talt.Client/Rendering/TreeInstances.cs`)
- [X] T031 [US2] Softer terrain AO curve and AO strength uniform in `voxel.wgsl` and `models.wgsl`
- [X] T032 [US2] Test that every key in the shipped `models.json` loads and meshes, and loading all models takes < 1 s, in `tests/Talt.Client.Tests/Rendering/ShippedModelsTests.cs` (SC-005)

**Checkpoint**: run the client; vehicles and trees are models.

## Phase 5: User Story 3 — Landscape and water (P1)

**Goal**: animated water with depth and shoreline foam; bushes and rocks.
**Independent test**: river, lake shore, countryside on a new map.

- [X] T033 [P] [US3] `WaterTexture` builder (data-model.md WaterTexture: distance to land, depth, stride, rectangle rebuild) in `src/Talt.Client/Rendering/WaterTexture.cs` with tests in `tests/Talt.Client.Tests/Rendering/WaterTextureTests.cs`
- [X] T034 [US3] Upload the water texture to group 1, rebuild rectangles on terrain edits (`WebGpuRenderer.NotifyVoxelsChanged` path) and add a real-time clock uniform
- [X] T035 [US3] Water shading in `voxel.wgsl` (animated normals, Fresnel to sky, sun specular, depth tint, foam band) gated by `WaterEffects`
- [X] T036 [P] [US3] `DecorScatter` (data-model.md Decoration; eligibility, density by preset and zoom, ring gathering, rebuild triggers) in `src/Talt.Client/Rendering/DecorScatter.cs` rebuilt when `GameSession.SceneryVersion` or the construction objects version changes, with tests (determinism, no decoration on water/roads/buildings/trees, density 0 = none) in `tests/Talt.Client.Tests/Rendering/DecorScatterTests.cs`
- [X] T037 [US3] Generate bush and rock models (3 each) in `tools/Talt.ModelGen/`; add `bush`/`rock` keys; feed decoration instances into `ModelBatch` from `InGameScreen`/`GameWindow`

**Checkpoint**: run the client; water and decoration visible.

## Phase 6: User Story 4 — Final models for buildings and industries (P2)

**Goal**: every town building type and industry kind has an original model; stations/depots can be added by data.
**Independent test**: town and industries drawn as models; replacing a model file changes the look.

- [X] T038 [US4] Generate town building models from `src/Talt.Core/Towns/Data/town-buildings.json` (walls, windows with glow index, doors, pitched/flat roofs, chimneys, rooftop details; 2 variants per type) in `tools/Talt.ModelGen/`
- [X] T039 [US4] Generate industry models from `src/Talt.Core/Content/Data/industries.json` (footprint-sized, detail per kind) in `tools/Talt.ModelGen/`
- [X] T040 [US4] Town buildings as models: `TownRenderer` appends one model instance per building in visible areas (variant from id, yaw from footprint orientation), box path kept for types without a model (`src/Talt.Client/Rendering/TownRenderer.cs`)
- [X] T041 [US4] Industries as models: one model instance per industry in `GameSession`, boxes kept as fallback (`src/Talt.Client/GameSession.cs`)
- [X] T042 [US4] Document in `docs/ARCHITECTURE.md` and `contracts/model-pipeline.md` how stations and depots add models (keys `station:<id>`, `depot:<id>`), and leave a note in the shared notes folder for feature 010

**Checkpoint**: run the client; towns and industries are models.

## Phase 7: User Story 5 — Quality presets (P2)

**Goal**: Low/Medium/High/Custom in settings, applied immediately and saved.
**Independent test**: switch presets in a running game; restart keeps choice.

- [X] T043 [US5] Graphics section in `SettingsWindow` (`src/Talt.Client/Screens/Windows.cs`): preset dropdown and one control per effect; Custom shown automatically; `host.ApplySettings()` reaches the renderer
- [X] T044 [P] [US5] Localization keys for the Graphics section, preset names and effect values in `src/Talt.Client/Localization/Strings.en.json` and `Strings.es.json`
- [X] T045 [US5] Apply every effect at run time (shadow texture recreate on quality change, AA variant, decoration density, model detail distances, AO strength, water flag) and verify the switch completes within a second (SC-007)

**Checkpoint**: run the client; presets switch live.

## Phase 8: User Story 6 — Day and night (P3)

**Goal**: optional 12-minute cycle with lit windows.
**Independent test**: enable, watch dusk and night; disable returns to daylight.

- [X] T046 [P] [US6] `TimeOfDay` (data-model.md TimeOfDay.Phase/CycleSeconds, keyframes, LightState, pause rule) in `src/Talt.Client/Rendering/TimeOfDay.cs` with tests in `tests/Talt.Client.Tests/Rendering/TimeOfDayTests.cs`
- [X] T047 [US6] Feed `LightState` into uniforms and the sky colour; glow palette entries and town-shader windows lit by night factor and per-window hash in `models.wgsl`/`towns.wgsl`; `--day-night` and `--time-of-day` options in `Program.cs`
- [X] T048 [US6] Day/night checkbox in settings (covered by T043/T044 keys)

**Checkpoint**: run the client with `--day-night --time-of-day 0.88`.

## Phase 9: Polish & cross-cutting

- [X] T049 `--measure-effects [seconds]` A/B measurement mode in `src/Talt.Client/GameWindow.cs` (+ `EffectCostProbe.cs`), vertical sync off, prints table, restores settings
- [X] T050 [P] Benchmarks: model load+mesh, `ModelBatch` with 60 000 instances, `DecorScatter` rebuild in `benchmarks/Talt.Benchmarks/ModelBenchmarks.cs`
- [X] T051 [P] `Category=Gpu` test compiling every WGSL shader through wgpu in `tests/Talt.Client.Tests/Rendering/ShaderCompileTests.cs`
- [X] T052 Run all tests and `pwsh ci/check-fingerprints.ps1`; confirm goldens unchanged (SC-006)
- [X] T053 Measure budgets: High on 2048 and Medium on 4096 at default/close/far zoom with `--stats`, and `--measure-effects` for both; tune presets until within budget (SC-001, SC-002)
- [X] T054 Record renderer passes, model pipeline, effect costs and benchmark numbers in `docs/ARCHITECTURE.md`; art direction decisions in `docs/VISION.md`; new options in `README.md`
- [ ] T055 Take "after" screenshots matching T001, present side by side to the developer for approval (SC-003); launch the client for the developer's hand test (camera movement, settings switch, FPS)
- [ ] T056 Ask the developer about committing

## Dependencies

- Phase 1 → Phase 2 → stories. US1 (shadows) and US2 (models) depend only on Phase 2; US3 depends on
  Phase 2 (decoration also on US2's `ModelBatch`); US4 depends on US2; US5 depends on all effects
  existing (works incrementally); US6 depends on Phase 2 and US2 (glow).
- Polish after the stories.

## Parallel opportunities

- T014 with T020–T023 (different projects/files); T033 and T036 with each other; T044 with T043;
  T046 any time after T005; T050/T051 at the end.

## Implementation strategy

MVP = Phase 2 + US1 (shadows and tone mapping already change the look most), then US2 + US3 (detail,
water, landscape), then US4 models for buildings/industries, then presets, day/night, measurement and
documentation.

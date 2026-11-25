# Research: Visual Quality (feature 018)

All decisions below were taken for the reference machine (Ryzen 7 160 with integrated Radeon GPU,
Vulkan through wgpu-native) and for the existing renderer of features 003–009: one world render pass
into the sRGB surface, one shared uniform bind group (`Uniforms` in `voxel.wgsl`), terrain region
meshes with 8-byte packed vertices and baked AO, and instanced unit cubes for vehicles, industries,
town buildings and trees. No new dependency is needed.

## R1 — Shadows: cascaded shadow maps

- **Decision**: cascaded shadow maps (CSM) for the sun. One `Depth32Float` 2-D array texture with one
  layer per cascade (1–3 cascades, 1024–2048 texels per side, by preset). Each cascade covers a slice
  of the camera's view distance (practical split scheme, λ = 0.75) up to a *shadow distance* that is
  a preset value capped by the fog distance. Each cascade's light matrix is an orthographic
  projection fitted to the **bounding sphere** of its frustum slice and its origin is **snapped to
  whole shadow texels**, so shadows do not shimmer when the camera moves or rotates (FR-002).
  Shadows fade out over the last 10 % of the shadow distance instead of ending in a line.
- **Casters**: every opaque world renderer gets a depth-only *shadow pipeline* built from its own
  shader module (vertex stage only, no fragment stage, slope-scaled depth bias, no culling). The
  shadow pass reuses each renderer's vertex shader unchanged: it is bound with a per-cascade
  uniform buffer whose `view_proj` is the cascade's light matrix. Terrain draws regions selected
  against each cascade's light frustum (not the camera frustum), so off-screen hills still shade the
  visible ground; models and roads use the camera-culled lists with a margin (R6).
- **Filtering**: hardware comparison sampling (`textureSampleCompare`, 2 × 2 bilinear PCF per tap):
  4 taps (medium) or 9 taps (high) on a rotated grid. Receivers offset their lookup along the normal
  by about one texel to avoid acne on axis-aligned voxel faces.
- **Rationale**: CSM is the standard technique for large outdoor scenes with a single sun; it keeps
  close shadows crisp at every zoom. A single shadow map would be either blurry at close zoom or too
  short at far zoom.
- **Alternatives**: one shadow map (rejected: quality across the zoom range); ray-marched heightmap
  shadows for terrain only (rejected: buildings, trees and vehicles must cast too); shadow volumes
  (rejected: fill-rate heavy on an integrated GPU).

## R2 — Linear lighting and tone mapping

- **Decision**: all shaders share one WGSL include, `lighting.wgsl`, inserted by a minimal
  `// #include "file.wgsl"` preprocessor in `WebGpuShaders.Load`. It holds the uniform block, the
  sun (direction, colour, intensity), a hemispheric sky/ground ambient term, ambient occlusion
  strength, shadow lookup, fog and a tone-mapping curve (Narkowicz ACES fit with an exposure value).
  Lighting and fog are computed in linear colour; tone mapping is the last step; the sRGB surface
  format performs the gamma encoding (already chosen by `ChooseSurfaceFormat`). The clear colour is
  the tone-mapped sky colour computed on the CPU by the same curve, so the far fog meets the sky
  seamlessly.
- **Rationale**: one shared lighting function keeps terrain, roads, models and boxes consistent
  (FR-004) and removes seven duplicated copies of the uniform block; no extra render target or
  full-screen pass is needed for tone mapping.
- **Alternatives**: HDR render target plus post pass (rejected for now: extra bandwidth on the
  integrated GPU and nothing else needs it); Reinhard (rejected: washes out saturated voxel colours).

## R3 — Ambient occlusion: improved baked AO, no SSAO

- **Decision**: keep and improve baked per-vertex AO: terrain keeps the exact AO from the greedy
  mesher (feature 003) with a softer curve and a strength uniform; models get the same per-vertex AO
  from the model mesher (R6). AO is applied to the ambient term only, so sunlit faces are not
  darkened twice. The "ambient occlusion" setting scales the strength (0, 0.6, 1).
- **Rationale**: voxel AO baked from the grid is exact for this geometry and free at draw time.
- **Alternatives**: SSAO (rejected: needs a normal/depth pre-pass and a full-screen blur; costly at
  1080p on the reference GPU for little gain over exact baked AO).

## R4 — Anti-aliasing: MSAA 4×

- **Decision**: multisample anti-aliasing with 4 samples (WebGPU guarantees sample counts 1 and 4).
  The world pass renders into a multisampled colour texture and depth texture and resolves into the
  surface; the interface pass is unchanged. Changing AA at run time needs pipelines with another
  sample count: each world renderer keeps its pipelines in a small `PipelineVariants` cache keyed
  by sample count, created on first use, so switching takes one frame.
- **Rationale**: voxel art is all long straight geometric edges, which MSAA handles exactly; text and
  the UI stay crisp because they are drawn after the resolve.
- **Alternatives**: FXAA (rejected: blurs the crisp voxel look and needs an off-screen target and a
  post pass); 2× and 8× (not guaranteed by WebGPU).

## R5 — Water

- **Decision**: water stays voxel terrain (material `Water`); `voxel.wgsl` shades water fragments with
  (a) a normal perturbed by two scrolling value-noise layers driven by a real-time clock uniform
  (keeps moving while paused, FR-006), (b) a Fresnel blend towards the sky colour and a sun specular
  highlight, (c) a colour that darkens with water depth, and (d) a foam band where the distance to
  land is below ~0.6 tiles, broken up by noise and slowly pulsing. Distance to land and depth come
  from a client-side *water texture* (`Rg8Unorm`, one texel per tile up to 4096 per side, one texel
  per 2 or 4 tiles above) built from `MapData` when the game starts and rebuilt per rectangle when
  terraforming changes tiles. Sampled with linear filtering, the shoreline is smooth instead of
  following the tile staircase.
- **Rationale**: no change to the simulation or the terrain mesh; one texture lookup per water pixel.
- **Alternatives**: screen-space depth for foam (rejected: needs a depth copy and a second pass);
  separate translucent water mesh (rejected: meshing change and sorting problems).

## R6 — Voxel model pipeline

- **Decision**:
  - **Format**: MagicaVoxel `.vox` (RIFF-like chunks: `VOX ` magic + version, `MAIN` with children
    `PACK`, `SIZE`, `XYZI`, `RGBA`; extended scene chunks `nTRN`, `nGRP`, `nSHP`, `MATL`, `LAYR`,
    `rOBJ`, `rCAM`, `NOTE`, `IMAP` skipped by size). Reader `VoxReader` written from the published
    format description in `Talt.Core.Meshing.Models`, pure and GPU-free, with strict size checks
    (axes 1–256, voxel count ≤ size product, chunk lengths within the file). Files without an `RGBA`
    chunk are rejected (the editor's default palette is not shipped). Coordinates are converted from
    the editor's right-handed Z-up axes to the game's Y-up axes: `game = (x, z, sizeY − 1 − y)`.
    `VoxWriter` writes the same subset (used by the model generator and by round-trip tests).
  - **Meshing**: `ModelMesher` builds a greedy mesh per face direction; quads merge only when colour
    and all four corner AO values match (the feature 003 rule); per-vertex AO uses the same
    side/corner rule as terrain, and the quad diagonal is flipped when AO is anisotropic. Detail
    levels 0, 1 and 2 are built by downsampling 2× and 4× (a cell is filled when any of its voxels is
    filled; the colour is the most common one), so far objects keep their silhouette without holes.
  - **Scale**: 8 model voxels per tile (clarification); 1 model voxel = 0.125 world units.
  - **Vertex**: 12 bytes: `Sint16x4` (x, y, z in half-voxel units relative to the model's pivot —
    centre of the base — and face | AO << 3) + `Uint32` (palette index | palette row << 8).
  - **GPU**: all meshes of all models and detail levels live in one vertex buffer and one index
    buffer; the palettes live in one `Rgba8UnormSrgb` texture of 256 × (model count) texels whose
    alpha encodes the palette entry's role: opaque (255), *paint* (128, multiplied by the instance
    tint, used for vehicle bodies) or *glow* (0, windows that light up at night).
  - **Drawing**: `ModelRenderer` gathers per frame the instances appended by the object sources
    (vehicles, buildings, industries, trees, decorative scenery; later stations and depots) into one
    list, picks the detail level per instance from its distance and the model-detail setting,
    culls against the camera frustum enlarged by the instance's shadow reach (height × shadow
    length), sorts by (mesh, level) with a counting sort, uploads once and records one indexed,
    instanced draw per non-empty bucket. Shadow passes reuse the same buckets.
  - **Instance**: 32 bytes: position xyz + yaw (radians), tint rgb + variation (0–1).
- **Rationale**: one draw per visible (model, level) pair keeps draw calls in the low hundreds even
  on a 4096 map; meshes built at load (SC-005) keep the frame path free of meshing; the pipeline is
  generic, so stations and depots need only data (FR-014).
- **Alternatives**: per-area static instance buffers like feature 008's town areas (rejected: one draw
  per model per area explodes at far zoom); a third-party `.vox` library (rejected: small format,
  licence review overhead, and the guide asks for our own parser); GPU-driven culling with compute
  (rejected for now: complexity; CPU bucketing measured first).

## R7 — Model assignment data

- **Decision**: `src/Talt.Client/Rendering/Data/models.json` maps *model keys* to one or more
  variants: `vehicle`, `town-building:<id>`, `industry:<id>`, `tree:<kind>`, `bush`, `rock`, and later
  `station:<id>`, `depot:<id>`. Each variant names a `.vox` file (embedded as `models/<name>.vox`)
  and optionally the palette indices used as paint and glow, a yaw offset and a pivot override. The
  file is validated at load: unknown keys are allowed (future objects), missing files, unreadable
  models, bad indices and duplicate keys stop the start with the file name and reason. Building
  types and industry kinds without an entry use the existing box placeholder.
- **Rationale**: data-driven (constitution VII), and the only thing feature 010 needs to add.

## R8 — Original models generated by a tool

- **Decision**: a new console tool `tools/Talt.ModelGen` (references `Talt.Core` only) generates the
  first set of original `.vox` files from code: town building types from their data (footprint,
  storeys, wall/roof colours, roof style, window style), industry kinds from their data (footprint,
  box colours, plus details such as chimneys, tanks, piles), four vehicles (car, van, bus, truck),
  three tree kinds with two or three variants each, and a few bushes and rocks. Output is
  deterministic (seeded), committed under `src/Talt.Client/Assets/Models/`, and editable in
  MagicaVoxel. A test checks every model key in `models.json` resolves and meshes.
- **Rationale**: clarification answer (models generated by the project, original art).

## R9 — Decorative bushes and rocks

- **Decision**: `DecorScatter` (client) derives decoration from a hash of (tile x, tile z, map seed):
  at most one bush or rock per tile, with sub-tile offset, yaw and variant. Allowed only on
  buildable grass tiles (bushes) or grass/rock tiles (rocks) with no tree, road, building or
  industry, and never on water or beaches. Density = preset density × zoom factor (fewer when far).
  Gathered ring by ring around the camera target (as `TreeInstances` does) and rebuilt only when the
  target moves 16 tiles, the zoom band changes or the scenery/objects version changes.
- **Rationale**: not simulation state (FR-007), identical every time, cheap to rebuild.

## R10 — Day/night cycle

- **Decision**: `TimeOfDay` (client) is a presentation clock: one cycle is 12 real minutes, it
  advances with frame time only while the game is not paused, and starts at mid-morning. It yields
  sun direction (east–west arc tilted 35°), sun colour and intensity, ambient colours, a night
  factor and an exposure from a small table of keyframes interpolated smoothly; at night a cool
  "moonlight" keeps the scene readable (FR-018). Windows glow by `night factor × lit(window)`,
  where about 60 % of windows are lit, chosen from a hash of the instance variation and the window
  voxel. Off by default; when off, the fixed daylight of the current game is used.
- **Rationale**: game days last 2 s, far too short for a visual cycle (clarification).

## R11 — Graphics settings and presets

- **Decision**: `GraphicsSettings` (client) with `Preset` (Low, Medium, High, Custom) and the effect
  values: `Shadows` (off/on), `ShadowQuality` (low: 1 cascade × 1024; medium: 2 × 2048; high:
  3 × 2048, plus shadow distance), `AntiAliasing` (off / MSAA 4×), `WaterEffects` (off/on),
  `SceneryDensity` (0, 0.35, 0.7, 1), `ModelDetail` (distance multiplier for detail levels),
  `AmbientOcclusion` (0, 0.6, 1), `DayNight` (off/on, not part of presets). The preset table lives in
  `Rendering/Data/graphics-presets.json`, validated at load. Saved as a nested `graphics` object in
  the player settings (version 4); unknown or invalid values fall back per property to Medium. The
  settings screen gets a Graphics section: preset choice plus one control per effect; changing an
  effect after picking a preset shows Custom.
- **Rationale**: FR-015–FR-017; data-driven values; same per-property resilience as feature 005.

## R12 — Measuring effect costs

- **Decision**: a client option `--measure-effects [seconds]` (requires a game: map options or
  defaults) runs an automatic A/B sequence with vertical sync off: the current preset, then the same
  preset with each effect turned off on its own, a few seconds each after a warm-up, holding the
  camera still; it prints average and 99th-percentile frame times per configuration and the
  difference, then restores the player's settings and exits. `--graphics low|medium|high` picks the
  preset for one run without saving. F3 / `--stats` add the current graphics settings, shadow draws
  and triangles, model draws and instances, and the CPU time of shadow preparation. Results are
  recorded in `docs/ARCHITECTURE.md`. BenchmarkDotNet adds model loading/meshing, instance bucketing
  and decoration scattering benchmarks.
- **Rationale**: synthetic input does not reach the window, so measurement must be automatic; A/B
  differences on the real game measure each effect's real cost on the reference GPU. GPU timestamp
  queries are optional in WebGPU and were not needed.

## R13 — Object sources move to models

- **Decision**: existing sources keep their culling and change their output: vehicles
  (`GameSession.BuildInstances`) append model instances with a stable variant from the vehicle id,
  yaw from the travel direction and paint tint from the current colour; town buildings
  (`TownRenderer`) append one model instance per building (variant from the building id) in visible
  areas; industries one instance per industry; trees (`TreeInstances`) one instance per tree.
  Objects whose key has no model keep the existing box path. The old box shaders remain for
  placeholders and the construction preview.
- **Rationale**: smallest change that keeps culling, LOD bands and placeholders working, and keeps
  merges with feature 010 small.

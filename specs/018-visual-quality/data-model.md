# Data Model: Visual Quality (feature 018)

Nothing here is simulation state. All types are client presentation data except the pure model
format types in `Talt.Core.Meshing.Models`, which hold no game state.

## VoxModel (Core, `Talt.Core.Meshing.Models`)

| Field | Type | Notes |
|---|---|---|
| SizeX, SizeY, SizeZ | int | 1–256 each, in **game axes** (Y up) |
| Voxels | byte[] | palette index per cell, 0 = empty; index `x + SizeX * (y + SizeY * z)` |
| Palette | uint[256] | RGBA (R in the low byte); entry *i* is colour index *i* (index 0 unused) |

Validation: sizes in range, every voxel inside the size, at least one voxel, palette present.
Conversion from the file: `game(x, y, z) = (vox.x, vox.z, vox.sizeY − 1 − vox.y)`; file colour index
*c* (1–255) uses palette entry `RGBA[c − 1]` (format rule), stored at `Palette[c]`.

## ModelMesh / ModelVertex (Core)

- `ModelVertex` (12 bytes): `short X2, Y2, Z2` — position in half-voxel units relative to the pivot
  (centre of the base); `ushort FaceAo` — face index 0–5 | AO 0–3 << 3; `uint Color` — palette index
  (8 bits) | palette row (24 bits, filled by the client).
- `ModelMesh`: per detail level 0–2, a vertex list (4 per quad) and the level's voxel size (1, 2, 4).
- `ModelMesher.Build(VoxModel)` → `ModelMesh` (deterministic; quads merged when colour and AO corners
  match).

## Model assignment (client, `Rendering/Data/models.json`)

| Field | Type | Notes |
|---|---|---|
| key | string | `vehicle`, `town-building:<id>`, `industry:<id>`, `tree:<kind>`, `bush`, `rock`, `station:<id>`, `depot:<id>` |
| variants[] | list | ≥ 1 |
| variants[].file | string | `.vox` file name in `Assets/Models` (embedded as `models/<file>`) |
| variants[].paint | int[] | palette indices tinted by the instance colour (optional) |
| variants[].glow | int[] | palette indices that light up at night (optional) |
| variants[].yaw | number | yaw offset in degrees, multiple of 90 (optional) |

## ModelLibrary (client)

- Loaded once per process: model keys → variant list → `ModelMeshHandle` (first index and index count
  per level, radius and height for culling) and palette row.
- `TryGet(key, out ModelEntry)`; unknown keys → placeholder path.

## ModelInstance (client, 32 bytes, matches `models.wgsl`)

| Field | Type | Notes |
|---|---|---|
| PositionYaw | Vector4 | pivot world position (xyz), yaw radians (w) |
| TintVariation | Vector4 | paint tint linear RGB, variation 0–1 (window lighting, colour jitter) |

## ModelBatch (client)

Per frame: `Begin(camera, frustum, settings)`, `Add(handle, position, yaw, tint, variation)`, `End()` →
buckets `(mesh, level) → (first instance, count)`; capacity grows by doubling, never shrinks; no
allocations per frame after warm-up.

## GraphicsSettings (client, saved in player settings as `graphics`)

| Field | Type | Values | Default |
|---|---|---|---|
| Preset | enum | Low, Medium, High, Custom | Medium |
| Shadows | bool | | from preset |
| ShadowQuality | enum | Low, Medium, High | from preset |
| AntiAliasing | enum | Off, Msaa4 | from preset |
| WaterEffects | bool | | from preset |
| SceneryDensity | enum | Off, Low, Medium, High | from preset |
| ModelDetail | enum | Low, Medium, High | from preset |
| AmbientOcclusion | enum | Off, Soft, Full | from preset |
| DayNight | bool | not in presets | false |

Rules: choosing a preset copies its values; changing any value except `DayNight` sets `Custom` unless
the values equal a preset. Each value is read on its own; invalid → default.

## Graphics presets (client, `Rendering/Data/graphics-presets.json`)

Per preset: the effect values above, plus the numeric parameters behind each quality enum: shadow
cascades (1–3), shadow map size (512–4096, power of two), shadow distance (tiles), PCF taps (1, 4,
9), decoration density (0–1), model detail distances (tiles for level 0→1 and 1→2). Validated at
load (ranges, all three presets present).

## WaterTexture (client)

`Rgba8Unorm`, (W / s) × (D / s) texels, s = 1 for sides ≤ 4096, else 2 or 4. R = distance to the
nearest land tile, 0–4 tiles mapped to 0–1; G = water depth (water level − ground), 0–8 voxels mapped
to 0–1; B = surface material of the nearest land tile (the shallow edge takes the shore's colour). Rebuilt for a rectangle (+4 tiles margin) after terrain edits.

## Decoration (client)

`DecorScatter.At(x, z, seed)` → none | bush(variant) | rock(variant) with sub-tile offset and yaw;
eligibility from terrain type, flags, scenery and tile objects. Not stored.

## TimeOfDay (client)

| Field | Type | Notes |
|---|---|---|
| Phase | double 0–1 | 0 = midnight; starts at 0.35 (mid-morning) |
| CycleSeconds | const | 720 |

Outputs `LightState`: sun direction, sun colour × intensity, sky ambient, ground ambient, sky colour,
exposure, night factor 0–1. Advances only when the runner is not paused.

# Data Model: Map Generation and New Game

All types live in `Talt.Core.Maps` unless noted. Indexing: `index = x + z * Width` (same orientation
as `Heightmap`: X east, Z south, Y up).

## MapSettings (record)

| Field | Type | Range / default | Notes |
|-------|------|-----------------|-------|
| Width | int | 64–16 384, multiple of 32; player minimum 256 (checked by the client); default 1024 | tiles |
| Depth | int | same | tiles; independent of Width |
| Seed | long | any | negative and zero valid |
| Roughness | int | 0–100, default 50 | detail octaves and relief variation |
| MaxHeight | int | 8–127, default 28 | highest voxel surface |
| WaterAmount | int | 0–90 (%), default 25 | share of tiles under the sea |
| SeaLevel | int | 1 to MaxHeight − 2, default 7 | sea surface height (voxels) |

Validation: `Validate()` throws `ArgumentException` naming the setting and the allowed range (also
used by `TryParse`). `ToText()` / `TryParse` per research R11. `GeneratorVersion` constant (starts
at 1). Equality is value equality, so settings are usable as dictionary keys in tests.

## MapGenerationTuning (record, data-driven)

Defaults for thresholds and densities named in research (cliff step 2, rock line, snow fraction,
beach width, river threshold factor, forest density, minimum site distances, site densities).
`MapGenerator.Generate(settings, tuning?, …)` uses `MapGenerationTuning.Default`. Not part of the
shareable text: changing tuning means bumping `GeneratorVersion`.

## MapData (sealed class, immutable after generation)

Per-tile arrays of length `Width × Depth`:

| Array | Type | Meaning |
|-------|------|---------|
| `Height` | byte | ground surface height in voxels (count of solid voxels in the column); river beds are carved ground |
| `Terrain` | byte (`TerrainType`) | Water, Beach, Grass, Rock, Snow |
| `WaterLevel` | byte | 0 = no water; otherwise the water surface height (exclusive top); sea and lakes level, rivers = bed + 1 |
| `Flags` | byte (`TileFlags`) | River, Lake, Sea, Buildable, Cliff, Coast (land within 2 tiles of water) |
| `Scenery` | byte | 0 = none; bits 0–2 tree kind (1–7), bits 3–7 variant/growth |

Plus `Settings`, `Sites` (`IReadOnlyList<Site>`), `Rivers` summary (count, springs) for tests and
statistics, and:

- `Fingerprint()` — 64-bit hash (existing `StateHasher` FNV-style) over settings, all arrays and
  sites, in index order. Used by determinism tests, goldens and the headless `map` command.
- Accessors: `HeightAt(x, z)`, `TerrainAt`, `IsBuildable`, `WaterLevelAt`, `SurfaceHeightAt` (max of
  height and water level), bounds-checked; `Overview(int maxSide)` helper deferred (US5).

Invariants (all asserted by tests on many seeds and sizes):

1. `Height ≥ 1` everywhere; `Height ≤ MaxHeight`.
2. Buildable tiles: all 4 neighbours differ by ≤ 1; a tile with a neighbour step > 1 is `Cliff`
   (or water) and not buildable.
3. Water: sea/lake tiles of one body share `WaterLevel`; every 4-neighbour of a water tile is water
   or land with `Height ≥ WaterLevel`. River neighbours along the flow never rise (downstream ≤).
4. Rivers: following parent pointers from any river tile reaches Sea or Lake tiles; bed monotone.
5. Snow only at/above the snow line; Beach only within 2 tiles of water; trees only on Grass, not
   Cliff, not Buildable-false by steepness.
6. Sites are `Buildable` with the free radius also buildable and inside the map.

Memory: 5 bytes per tile (80 MB at 4096²) plus transient generator buffers released after
generation (≈ 250 MB peak at 4096²: parent, order, accumulation, filled level).

## Site / SiteKind

```text
SiteKind: Town, IndustryCoast, IndustryForest, IndustryPlain, IndustryHills
Site: X, Z (tile of the centre), Kind, FreeRadius (tiles, ≥ 3 for towns), Score (0–255)
```

Sites are sorted by (Kind, Z, X) so the list is deterministic. Features 008 and 009 read `Sites`;
`MapData.TownSites` and `MapData.IndustrySites` are views.

## MapGenerationProgress

`readonly record struct MapGenerationProgress(MapGenerationStage Stage, float Fraction)` where the
fraction is overall 0–1 and monotonic, and `MapGenerationStage` is an enum
(`Terrain, Slopes, Water, Rivers, Surface, Scenery, Sites, World`) whose localized names the client
looks up (`stage.map.<name>` keys). Reports are throttled to changes ≥ 1% and never from inside hot
loops.

## Generation state machine

`Terrain → Slopes → Water → Rivers → Surface → Scenery → Sites → (WorldBuilder) World`; weights
for the progress fraction are fixed in the tuning (measured shares). Cancellation is checked between
bands and stages and throws `OperationCanceledException`; no partial result escapes.

## Relationship to existing types

- `WorldBuilder.Build(MapData) → VoxelWorld`, columns from `Height`/`Terrain`/`WaterLevel`.
- `Heightmap.FromMapData(MapData)`.
- `TerrainSettings`/`TerrainGenerator` removed; `NetworkTerrainPainter` unchanged (paints the top
  voxel under roads).

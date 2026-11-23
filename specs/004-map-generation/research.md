# Research: Map Generation and New Game

Decisions for the open questions of the plan. Each: decision, rationale, alternatives.

## R1 — Noise and cross-platform determinism

**Decision**: keep the existing `ValueNoise` (hash lattice + smoothstep) as the basis and add
fractal, ridged and domain-warped helpers on top of it. No simplex implementation. All generator
arithmetic uses only `+ - * /`, `MathF.Sqrt`, `MathF.Floor` and integer operations; `Pow`, `Exp`,
`Sin` and friends are forbidden in `Talt.Core.Maps` (they may differ between platform math
libraries). .NET does not fuse multiply-add on its own, so IEEE-754 single precision gives the same
bits on x64 and arm64. A golden fingerprint test running on all five CI platforms is the proof.

**Rationale**: value noise is already tested and deterministic; its grid artifacts are hidden by
fBm octaves, domain warping and the later slope smoothing. Avoids a new algorithm with patent and
licensing questions (OpenSimplex is public domain, but not needed).

**Alternatives**: OpenSimplex/Perlin (nicer isotropy, more code, same risk surface); integer
fixed-point noise (exact by construction but slower to write and tune).

## R2 — Height shaping, water share and sea level

**Decision**: height = continent mask (very low frequency noise) + hills (fBm, octaves and
persistence driven by *roughness*) + ridged mountains weighted by the continent mask, normalised
to [0, 1] in fixed point (16 bits). A **monotone piecewise-linear remap** then places the water
share exactly: the value at quantile `WaterAmount` (computed from an integer histogram of 65 536
bins, deterministic) maps to the sea surface `SeaLevel`; values below it map into [1, SeaLevel-1],
values above into [SeaLevel, MaxHeight]. So `WaterAmount` sets how much of the map is under the sea
(within ±0.5%), `SeaLevel` sets where the sea sits vertically (how much relief is above it),
`MaxHeight` the top, `Roughness` the amount of detail and relief variation.

**Rationale**: makes SC-006 (monotone response of water share and relief) true by construction and
testable; all four settings stay independent; integer remap is deterministic.

**Alternatives**: threshold noise at a fixed value (water share unpredictable); simulate erosion
(slow, hard to bound).

## R3 — Slope rule: one voxel per tile, deliberate cliffs

**Decision**: enforce "neighbouring tiles differ by at most 1 voxel" with a two-pass **chamfer
limiter** (min-plus transform with 4-neighbour steps; one forward and one backward raster pass give
the exact fixed point) that lowers over-steep ground, instead of iterating blur filters. The step
allowed per tile is 1 normally and 2 inside the cliff zones (mountain tiles whose rock-noise value
is high, and above a rock line). Tiles whose neighbour step is still > 1 are marked `Cliff` and not
buildable. Because heights are integers ≤ 127 and the passes are sequential raster scans, the result
is deterministic; each pass is a cheap O(n) (≈ 0.1–0.2 s at 4096²). A flat-ish smoothing pass
before the limiter (3 × 3 box on the hill component, per row band) avoids visible terracing.

**Rationale**: gives a hard guarantee (SC-005 and the invariant test) rather than a probabilistic
one; keeps mountains dramatic where cliffs are allowed. A 127-voxel mountain needs ≥ 64 tiles of
run in cliff zones and ≥ 127 elsewhere, which is natural on large maps; on small maps the generator
scales relief by the shorter map side (peak height ≤ `min(MaxHeight, shorterSide/2)`), documented.

**Alternatives**: repeated Gaussian smoothing (no guarantee); terracing to flat steps (looks
artificial); raising valleys (changes the water share).

## R4 — Lakes and drainage: priority flood

**Decision**: run the **priority-flood** depression-filling algorithm (Barnes et al.; public
description) seeded from the sea tiles (all tiles below the sea surface), with a bucket queue
indexed by integer height (≤ 128 buckets, O(n)). It yields (a) a filled level per tile: depressions
deeper than a threshold become **lakes** at the filled level, shallower ones are levelled into the
terrain; and (b) a **parent pointer** per tile (the neighbour that discovered it, which is its
lowest neighbour), forming a drainage forest rooted in the sea; the pop order is a topological order
(downstream first). If no tile is below the sea surface (extreme sea level) the lowest tile is
turned into a pond and used as the single root. Because every land tile drains to a root, every
river ends in the sea or a lake by construction.

**Rationale**: the guarantee "rivers always connect to water; no dead ends" (FR-009, SC-003) comes
from the data structure, not from luck; linear time; sequential cost ≈ 1–2 s at 4096².

**Alternatives**: steepest-descent tracing with random walks out of pits (can dead-end), A* per
river (slow, 10⁵ paths), hydraulic erosion simulation (cost, unbounded).

## R5 — Rivers

**Decision**: flow accumulation over the drainage forest (reverse pop order, integer counts). A tile
with accumulation ≥ `RiverThreshold` (scales with map area and water amount) and not under a lake
is a river tile; springs are tiles at the head of such chains on ground above the lakes. The river
bed is carved downstream-first: `bed[c] = max(bed[parent], min(h[c] - 1, …))` so the bed never rises
downstream; water level of a river tile = bed + 1. A **bank repair pass** (reverse pop order:
upstream to downstream) lowers a bed to the lowest 4-neighbour *land* height and propagates the
minimum to the parent, so no water surface ends up higher than an adjacent land tile (no floating
water, FR-008); lowering is monotone and ends at the sea level, so it terminates. Rivers are one tile
wide (two where accumulation is very high, decided by tuning). The slope limiter runs again after
carving on non-water tiles only if a banks step exceeds 1 (rare), and tests assert the invariants.

**Alternatives**: carve first and re-flood (double cost); width by accumulation (kept as a tuning
option, off by default).

## R6 — Beaches, rock, snow, buildability

**Decision**: classification pass, per tile, parallel by rows:
- `Water` (sea, lake, river) from the water level;
- `Beach`: land with height ≤ SeaLevel + 1 and within 2 tiles of sea or lake water and gentle slope;
- `Rock`: cliff tiles and steep tiles (step ≥ 2) above the rock line;
- `Snow`: height ≥ snow line, `snowLine = SeaLevel + 0.72 × (MaxHeight − SeaLevel)` computed in
  integers, with a ±2 voxel noise wobble; never below SeaLevel + 8 (no snow when there is no
  mountain);
- `Grass` otherwise (forest is scenery on grass).
`Buildable` flag = land (not water), step to all 4 neighbours ≤ 1, not Rock/Snow-steep.
Voxel materials: Water, Sand (beach), Grass+Dirt, Stone (rock), Snow — all existing materials, so no
palette or shader change.

## R7 — Forests and trees (scenery data)

**Decision**: a forest density map (low-frequency noise, one octave of detail) thresholded with the
water amount (wetter maps, more forest) decides tree density per tile on `Grass` tiles of slope ≤ 1;
a tile holds at most one tree, chosen by `hash(x, z, seed) < density` with kind (3 bits: broadleaf,
conifer, pine, palm, dead…) by height band and region noise, and growth stage (5 bits). Trees are
stored in the per-tile `Scenery` byte (kind, variant), not in voxels, and are not rendered in this
feature (the renderer has no instance pass yet; drawing them is a later rendering task, noted in
`docs/ARCHITECTURE.md`). Spec edge: no trees on water, beach, snow, rock, steep.

**Alternatives**: Poisson-disc trees (cost, unneeded at per-tile precision); trees as voxels (rejected
by the clarification guidance: must be removable scenery).

## R8 — World building and heights above 32 voxels

**Decision**: `WorldBuilder` creates chunks sequentially (the world's dictionary is not thread-safe),
then fills them in parallel per chunk with a new internal bulk `Chunk` writer (no per-voxel
bounds-checked calls), column by column from `MapData`. `VoxelWorld.GetSurfaceHeight` gains a world
top (`VoxelWorld.TopChunkY`) default so heights above 31 work; `Heightmap.FromMapData` builds the
height map directly from tile data (height or water surface, whichever is higher), skipping voxel
walking. The renderer already works on 128³ regions with a Y coordinate; verification task: render a
map with max height 127 and confirm meshing, LOD and culling behave (feature 003 tests exist for
multi-region Y); fixes, if any, are kept minimal and flagged to feature 005's owner.

**Memory risk**: a chunk is 32 KB; a 4096² map with average height 40 holds ~ 16 000 × 2 chunks
(≈ 1 GB). Mitigation if measured too high: share one immutable "solid stone" chunk for fully
buried chunks (copy on write). Measured and recorded in the plan's quickstart; not built unless
needed (YAGNI).

## R9 — Parallelism without nondeterminism

**Decision**: parallel stages (noise, classification, scenery, world fill, histogram) use
`Parallel.For` over **fixed row bands** of 16 rows (independent of core count), each a pure function
of its input tiles, and merge results in band order; sequential stages (chamfer, priority flood,
accumulation, sites, components) stay sequential. A test generates with `MaxDegreeOfParallelism` 1
and the machine default and compares fingerprints (constitution III).

## R10 — Sites (towns and industries)

**Decision**: label land components (iterative BFS, 4-neighbour, `Buildable`-agnostic land); towns
only on the largest component (this makes SC-004 hold by construction); industry sites on any
component of ≥ `MinIndustryComponentArea` tiles. **Poisson-disc sampling** (Bridson, public
description) on a background grid with a `DeterministicRandom` seeded from the map seed and the
site kind: candidates are accepted if the surrounding window is flat (all tiles `Buildable`, height
range ≤ 2 for towns, ≤ 3 for industries) and the minimum distance to previous sites holds (towns
48, industry–industry 24, industry–town 16 tiles by default, scaled in `MapGenerationTuning`). Site
kinds for industries: `Coast` (within 6 tiles of sea), `Forest` (tree density high), `Plain`
(flat open), `Hills` (height above mid level, gentle). Target counts: 1 town per 40 000 land tiles
and 1.5 industries per town, capped by what fits; small or watery maps yield fewer sites. Each site
stores position, kind, the guaranteed free square (radius) and a suitability score.

**Alternatives**: grid jitter (visible regularity), best-candidate (slower), sites anywhere
(violates connectivity requirement).

## R11 — Settings text and versioning

**Decision**: `MapSettings.ToText()` and `TryParse` use `T<version>-<W>x<D>-s<seed>-r<roughness>-h<maxHeight>-w<water>-l<seaLevel>`
(e.g. `T1-1024x512-s2026-r50-h28-w25-l7`). `GeneratorVersion` is a constant bumped whenever a change
alters output for the same input; the text carries it and the generator refuses (with a clear
message) a version it does not implement. Seed is a signed 64-bit integer; negative, zero and
maximum values are valid; a missing seed on the command line picks a random one (printed).

## R12 — Client integration without touching feature 005's files

**Decision**: `Program.cs` gets named options `--map <WxD|N>`, `--seed`, `--roughness`,
`--max-height`, `--water`, `--sea-level`, `--map-text <text>`; the existing positional
`[chunks-per-side] [vehicles] [seed]` keeps working (maps to a square size). Console output:
`[ 37%] Rivers` lines (stage names localized through `Strings`) every 5% at most, and a final line
with the settings text. The scenario's square road network uses `min(width, depth)` rounded down to
a multiple of 32 (non-square maps get roads in a square part; roads and towns are replaced by later
features). `PrototypeScene`/`GameWindow` keep their current constructor; nothing else changes.

## R13 — Golden fingerprints

The simulation scenario fingerprints in feature 001 depend on the network, vehicles and seed, not on
voxels, so they are expected to be unchanged; if a test shows otherwise the goldens are regenerated
deliberately and the commit message says so (user rule). New goldens are added for the map content
fingerprint of fixed settings on small sizes (256², 512 × 256) and checked on all CI platforms.

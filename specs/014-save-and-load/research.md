# Research: Save and load (feature 014)

Each entry: decision, rationale, alternatives considered. Numbers marked "estimate" are replaced by
measurements in `docs/ARCHITECTURE.md` during implementation.

## R1. What a save contains: a verbatim image of the persistent state

**Decision**: every system writes, field by field, **all instance state that is not a reference, an
immutable configuration value or a scratch buffer**, exactly as it is in memory: arrays with their full
length (not trimmed to the live count), free lists, pool indices, version counters, caches,
in-progress job state and random streams. On load the system is constructed empty, wired exactly as
`GameSimulation.Create` wires it, and its fields are overwritten from the save. Nothing that affects
future ticks is "rebuilt" unless the rebuild is proven identical (R2).

**Rationale**: FR-003 asks for identical continuation, not only an identical fingerprint at the moment
of loading. A survey of `Talt.Core` (about 700 private fields) found state that is **not hashed but
changes the future**:

- version counters compared against stamps (`CargoRouter._startedVersion` against the stations' and
  line sources' versions: a mismatch starts a route job; `RoadVehicleSystem._stationsVersion`,
  `_depotsVersion`, `_componentVersion`, `_depotIndexVersion`; `StationRegistry._industryVersion`);
- caches whose hits change behaviour because search work is budgeted per tick
  (`RoadVehicleSystem._routeCache`, `_searchBudget`, `_staleBudget`; `RoadRouter._cache` with its
  eviction queue; `TrainSystem._searchBudget`);
- multi-tick work in progress (`CargoRouter._buildStage` 1–3 with partially built `RoutingProblem`s,
  a solved but not yet applied `_nextPool` until `_pendingApplyTick`);
- pools with free lists whose order decides which slot the next packet takes
  (`StationRegistry` packet pools `_pkNext/_pkFree`, `_dpNext/_dpFree`);
- the road graph's node numbering, which depends on the history of incremental edits and can decide
  ties in path searches.

Rebuilding any of these would make a loaded game diverge from the original some ticks later. A
verbatim image is also the simplest rule to apply and to review: "if it is state, it is saved".

**Alternatives considered**:

- *Replay the recorded commands from the start* (the session format of feature 001): exact, but load
  time grows with play time and map generation alone may take up to 20 s on 4096² (budget 5 s).
- *Save only hashed state and rebuild the rest*: smaller, but every rebuilt structure would need a
  proof that it equals the history-dependent original; the list above shows several that do not.
- *Reflection or source-generated serializers*: rejected by the plan prompt for hot data; reflection
  is slow on large arrays and hides what is saved; a source generator adds build complexity for ~35
  types (constitution V).

## R2. Derived state that may be rebuilt

**Decision**: only these are rebuilt on load, each with a test that the rebuilt structure equals the
saved game's structure field by field (not only by fingerprint):

- the client's voxel world, meshes and heightmap (render state, built from `MapData` as for a new game);
- `GameContent` / rules lookups and every `readonly` reference wiring;
- per-tick scratch buffers that are cleared or fully overwritten before every read (listed in the field
  manifest, R12, with the reason).

Everything else, including `RoadNetwork`, coverage grids and lane maps, is saved verbatim. When a
rebuild later proves identical and measurably saves time or space, it can move to this list with its
test.

**Rationale**: the default of R1 is safe; the exceptions are limited to state that is outside the
simulation or provably write-before-read.

## R3. Dictionaries and hash sets

**Decision**: dictionaries and sets are written as their entries **in enumeration order** and read
back by inserting in that order into a new instance. A test per dictionary that the simulation
enumerates (`StationRegistry._pendingSupply`, `RoadVehicleSystem._requests`, `_holders`,
`StationRegistry._removedCentre`) checks that enumeration order after load equals the original's,
including after further inserts following removals.

**Rationale**: .NET `Dictionary` enumeration order is insertion order only while nothing was removed;
removals leave holes that later inserts fill. Re-inserting in enumeration order reproduces the current
order, but not the hole positions. The survey found that the enumerated dictionaries are either
cleared within the day or tick (`_pendingSupply` daily, `_requests` per tick: no holes survive a save
point) or only read through sorted keys (`_removedCentre`, `_carry`, `_nameCounters`). Where a
dictionary is enumerated and keeps holes across ticks, the fix is to make the code enumerate in a
defined order (sorted keys) **only if** that does not change results; otherwise its exact layout is
saved. The tests make the assumption checkable.

**Alternatives considered**: writing the dictionary's internal buckets and entries via reflection
(fragile across runtime versions, and platform-identical layout is not guaranteed).

## R4. File layout

**Decision**: one file per save, little-endian, in this order:

1. **Fixed header** (64 bytes): magic `TALTSAVE`, container format version, flags, offsets and lengths
   of the details block, thumbnail and section table, and a checksum of the header itself.
2. **Details block**: UTF-8 JSON (name, kind, real save time, in-game date, company name, money, map
   size, game version, content identity summary), with its checksum. Small, read by the load screen.
3. **Thumbnail**: 256 × 256 RGB pixels, compressed, with its checksum.
4. **Sections**: one per subsystem, each made of independently compressed blocks of at most 4 MiB of
   uncompressed data.
5. **Section table** at the end of the file: per section its four-character id, section version,
   uncompressed length and per block the offset, compressed length, uncompressed length, codec and
   checksum; the table has its own checksum and the header points to it.

The load screen reads only the header, the details and the thumbnail (FR-016). The table at the end
lets the writer stream sections without knowing their sizes beforehand. Exact byte layout:
[contracts/save-format.md](contracts/save-format.md).

**Alternatives considered**: a zip archive (`System.IO.Compression.ZipArchive`): built-in and
inspectable, but per-entry Deflate only, no per-block parallelism, CRC32 only, and its central
directory parsing is more code paths to harden against corruption; a single compressed stream (no
cheap metadata reads, no parallel decompression).

## R5. Compression codec

**Decision**: per-block codec field; the default codec is **Deflate (`System.IO.Compression`,
`CompressionLevel.Fastest`)**, with blocks compressed and decompressed in parallel on the thread pool.
Brotli (quality 1, also built in) and "stored" are supported codecs so the default can change without
a format change. The first implementation task measures both on the reference save and records the
choice in `docs/ARCHITECTURE.md`; Deflate is kept unless Brotli is at least as fast to decompress and
saves at least 20 % space.

**Rationale**: no new dependency (constitution: built-in .NET only). Most of the bytes are tile layers
(road, rail, objects: mostly zeros) and entity arrays, which compress extremely well even at the
fastest level; the budget is time (save ≤ 3 s, load ≤ 5 s), not size. Deflate's fastest level and
decoder are fast and well understood; Brotli's higher qualities are far too slow for 300+ MB, and its
quality 1 is competitive, hence the measurement. 4 MiB blocks keep 16 threads busy and bound the
memory of each worker.

**Alternatives considered**: Zstandard (best speed/ratio, but not in the .NET 10 base library: a
native dependency to justify, package and ship on six platform targets); LZ4 (third-party package); no
compression (a 4096² save would be about 400 MB on disk).

The implemented benchmark compared codecs on the actual mixed-mode reference snapshot (320 MB). Deflate Fastest wrote it in 125.48 ms and Brotli quality 1 in 129.90 ms. The shipped default remains Deflate. Full results are in the architecture document.

## R6. Checksums and integrity

**Decision**: XxHash3 (64-bit, `System.IO.Hashing`, already referenced by Core for fingerprints) over
the header, the details, the thumbnail, the section table and **every compressed block**; the
uncompressed length of each block is checked after decompression, and each section reader must
consume exactly its section's bytes. All checks of the whole file run **before** any state is
constructed (FR-019); a load either fully succeeds or leaves the running game untouched.

**Rationale**: XxHash3 is fast enough to checksum the whole file in a few tens of milliseconds;
detecting damage, not tampering, is the goal (spec assumption), so a cryptographic hash is not needed.
Checking compressed blocks means a damaged block is reported before the decompressor sees it.

**Alternatives considered**: CRC32 (weaker, no faster in .NET); SHA-256 (slower, no benefit for damage).

## R7. Snapshot at a tick boundary, write in the background

**Decision**: saving has two phases.

1. **Capture** (main thread, between ticks): every section writer copies its state into a
   `SaveBuffer`, a list of pooled 4 MiB segments. Large arrays (tile layers, entity arrays) are copied
   with block copies, in parallel across sections where the sections are independent. The thumbnail is
   sampled from the map in the same phase. Capture never allocates once warm (pooled segments, no
   LINQ, strings encoded into spans), checked with `AllocationAssert`.
2. **Write** (background task): compress the segments block by block in parallel, checksum, write to
   `<name>.talt.tmp`, flush to disk, then atomically replace `<name>.talt` (`File.Move` with
   overwrite on the same folder). Segments are returned to the pool afterwards; the pool is trimmed
   after each save so a one-off 400 MB peak does not stay resident.

Only one write runs at a time; an autosave due while a write is still running is skipped (and tried
again at the next interval), a manual save or quick save waits for it (spec edge cases).

**Rationale**: the 100 ms stall budget (SC-002) only allows copying; in the reference scenario the
state is about 340 MB of tile layers (5 map bytes + 6 road + 6 rail + 3 object bytes per tile ×
16.8 M tiles) plus entity arrays, which copy in roughly 30–60 ms with parallel block copies (estimate,
measured by the benchmark). Compression and disk I/O are the slow part and run off the main thread
while the game keeps playing.

**Alternatives considered**: copy-on-write or dirty-chunk tracking of tile layers (much smaller stall,
but touches every writer of tile data across five features; deferred until the measurement shows the
copy exceeds the budget); pausing the simulation while writing (violates SC-002); forking the process
(not portable).

## R8. Load path

**Decision**: read and verify the whole file (R6), decompress all blocks in parallel into pooled
segments, then restore sections in dependency order on the calling thread (the client calls it from a
background task with a progress report, FR-025): simulation core and settings → content identity
check → map and construction → roads/rail/objects → towns → industries → stations → finance → road
vehicles → trains → demand and router → later features' sections. Afterwards the client builds its
voxel world from the restored map exactly as after map generation.

**Rationale**: restore order mirrors the wiring order of `GameSimulation`'s constructor, so every
system finds its dependencies constructed. Load budget (5 s): decompression of ~400 MB in parallel
and block copies are well under it; the voxel world build of a 4096² map (feature 003/004 numbers)
dominates and is already within the new-game budget.

## R9. Constructing an empty, wired simulation

**Decision**: `GameSimulation` gets a second private construction path, used only by the loader, that
creates every system in the same order and with the same event registrations as a new game but skips
generation work (no industry placement, no town founding, no street seeding, no initial forks
consumed beyond what the constructor does anyway, since random streams are overwritten from the save).
Systems that today can only be created by generating content (`TownRegistry.Found`,
`IndustryFleet.Create`) get an internal "for restore" factory with the same wiring. `MapData` gets an
internal constructor from saved arrays and sites.

**Rationale**: the event registration order inside the constructor decides the order of day, month
and year handlers; duplicating it in a loader would invite drift. Sharing the constructor with a flag
keeps one source of truth.

## R10. Content identity and remapping (FR-018, FR-022)

**Decision**: a `CNTN` section stores, for each content table that state refers to by index, the
ordered list of stable ids: cargo types, industry kinds, town building types, station piece types,
warehouse types, road types, rail types, signal types (if indexed), road vehicle models and rolling
stock models; plus the fingerprints of the content and of every rules file. On load:

- every saved id must exist in the installed data, otherwise the save is refused with a message that
  names the missing ids;
- a saved-index → installed-index table is built per content table; when every table is the identity
  (the normal case) sections are read without remapping; otherwise each reader remaps its content
  indices through the table (tile layers with a 256-entry lookup, in parallel);
- when any fingerprint differs, the load succeeds with a "may play differently" warning in the result.

**Rationale**: feature 016 will reorder and extend the vehicle catalog; stable ids make old saves
survive it. Identity is the common case and costs nothing.

## R11. Versioning and migrations (FR-017, FR-020, FR-021)

**Decision**: two version levels.

- **Container format version** (header): changes only when the file layout itself changes. Version 1
  is written by this feature. A newer container version is refused.
- **Section version** (per section): each section reader declares its current version and a chain of
  migration steps `vN → vN+1` that operate on the decoded section data (reading the old layout and
  producing the new state, typically filling a new field with its default). A section with a newer
  version than the reader knows is refused as "made by a newer version". A known section missing from
  the save is initialised to its defaults (the system as constructed for a new game, with no history).
  An unknown section id is refused as "newer", never ignored (FR-021).

Every section version bump adds a fixture save written by the previous version to
`tests/Talt.Core.Tests/Saves/Fixtures/` and a test that it loads, continues and re-saves at the current
version (SC-005). The first fixture of version 1 is committed by this feature, so the first bump by
016 or 017 has its predecessor ready.

**Rationale**: per-section versions keep features independent (016 and 017 are being specified in
parallel), and refusing unknown sections avoids silently losing a newer feature's state.

## R12. Save field manifest (keeps future features honest)

**Decision**: `tests/Talt.Core.Tests/Saves/SaveFieldManifest.txt` lists every instance field of every
saved type with its classification: `saved`, `derived` (rebuilt on load, with the test that proves it),
`scratch` (write-before-read buffer), `ref` (wiring to another system or immutable rules/content). A
reflection test (like `NoFloatingPointStateTests`) fails when a field of a registered type is missing
from the manifest or a listed field no longer exists, so adding state forces its author to decide how
it is saved.

**Rationale**: the verbatim rule only works if new fields are not forgotten; the fingerprint catches
hashed fields, the re-save comparison (R13) catches most of the rest, the manifest catches them at
the moment the field is added.

## R13. Proof of identical continuation

**Decision**: the core test pattern for every scenario fixture:

1. play a game for some ticks (rich fixture: map with towns and industries, stations, roads, road
   vehicles on lines, rail with signals and trains, destination cargo in transit, a loan, a removed
   station, undo history);
2. save at several tick offsets, chosen to hit a mid-day tick, a day boundary, a month boundary, every
   `CargoRouter` build stage and a pending route apply;
3. load each save and check the fingerprint equals the original's;
4. **save the loaded game again and compare the section payloads byte for byte** with the first save
   (this covers non-hashed state);
5. run both the original and the loaded game N further ticks (10 000 in the long test) with the same
   scripted commands, comparing fingerprints at checkpoints and section payloads at the end;
6. sequential and parallel ticking give the same results after loading.

**Rationale**: steps 4 and 5 are what make "continues identically" checkable without trusting the
fingerprint's coverage.

## R14. Thumbnail

**Decision**: a 256 × 256 top-down overview sampled from `MapData` and the tile layers on the main
thread during capture: terrain colour by type and height shading, water, roads, rail, town buildings
and industry tiles, scaled with the larger map side mapped to 256 pixels (non-square maps letterboxed).
Colours come from a small data table in Core content (constitution VII). Stored as compressed RGB.
The client turns it into a texture for the load screen; Core does it without a GPU, so headless saves
have it too (spec clarification).

**Rationale**: sampling 65 536 tiles costs well under a millisecond; no image codec dependency is
needed because the file stores raw compressed pixels.

## R15. Where saves live and how names map to files

**Decision**: `<ApplicationData>/TALT/saves/` (next to `settings.json`, which already uses
`Environment.SpecialFolder.ApplicationData`). File names are the player's name made safe for every
platform (characters outside letters, digits, space, `-`, `_`, `.` replaced; reserved Windows names
avoided; at most 80 characters), with `.talt`. The original name is stored in the details block, and
clashes are compared case-insensitively on every platform so a save does not appear twice on
case-insensitive file systems. Autosaves are `autosave-1.talt` … `autosave-N.talt` rotated by their
details' save time; the quick save is `quicksave.talt`.

## R16. Autosave scheduling

**Decision**: a client `AutosaveScheduler` accumulates real elapsed time only while the runner is not
paused (and not in menus that pause it), triggers a capture at the next frame after the interval is
reached, and resets the accumulator after a successful capture. Settings (`AutosaveMinutes`: 0 = off,
5, 10, 15, 30; `AutosaveKeep`: default 3) live in `PlayerSettings`. Its per-frame update allocates
nothing (`AllocationAssert`).

**Rationale**: the clarification chose real unpaused minutes so the pace does not depend on game
speed; the scheduler lives in the client because it measures real time, which Core must never read.

## R17. Pending commands, recorder and the client's state

**Decision**: commands queued but not processed are saved in the simulation core section using the
existing `CommandSerializer` JSON per command (rare, small), together with the inbox's next submission
id. A session recorder is not attached to loaded games (spec assumption). The client stores its camera
in a client-owned `CAMR` section that Core carries as an opaque blob; Core's API accepts and returns
"extra sections" for that purpose.

## R18. Headless tool

**Decision**: `Talt.Headless run … --save <file>` writes a save after the run, and a new
`Talt.Headless load <file> --ticks N --checkpoint-every K` loads a save, prints the fingerprint at load
and at each checkpoint and optionally `--save` again. This is how cross-platform loading is checked:
CI loads a Windows-written fixture save on every platform and compares the printed fingerprints with
committed values (added to `ci/check-fingerprints.ps1`).

## R19. Performance budgets for this feature

| Metric | Budget | Source |
|---|---|---|
| Save, reference scenario, total (capture + write) | ≤ 3 s | SC-001 |
| Load, reference scenario (read, verify, decompress, restore; client world build reported separately) | ≤ 5 s | SC-001 |
| Main-thread stall of a save (capture) | ≤ 100 ms | SC-002 |
| Load screen with 200 saves | ≤ 0.5 s | SC-006 |
| Capture allocations once warm | 0 bytes | constitution II |
| Autosave scheduler per frame | 0 bytes | constitution II |
| Tick after load | unchanged tick numbers and 0 allocations | constitution II |

The benchmark `SaveLoadBenchmarks` builds the reference game (feature 012/013's 50 000-bus game plus
feature 015's seeded trains) and measures capture, write, verify + decompress, restore and file size;
the results go into `docs/ARCHITECTURE.md` (new "Save and load (feature 014)" section).

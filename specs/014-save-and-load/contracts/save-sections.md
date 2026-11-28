# Contract: save sections and how later features add state

This is the save contract that features 016 (vehicle catalog and eras, spec FR-006) and 017 (line
management and statistics, spec FR-007) asked for. It also applies to every later feature that adds
simulation state.

## Section registry

| Id | Owner (feature) | Version in 014 | Contents |
|---|---|---|---|
| `CORE` | Simulation (001) | 1 | settings, tick, clock, root random, pending commands, prototype fleet and network |
| `CNTN` | Saves (014) | 1 | content ids per table, rules fingerprints |
| `MAP_` | Maps (004) | 1 | map tiles and sites |
| `CNST` | Construction (006) | 1 | companies, ledgers, objects and undo |
| `ROAD` | Roads, Network (007) | 1 | road layer, road graph, road router cache |
| `RAIL` | Rails (015) | 2 | rail layer, signals, blocks, rail depots |
| `TOWN` | Towns (008) | 1 | towns and buildings |
| `INDY` | Industries (009) | 1 | industries |
| `STNS` | Stations (010, 013) | 1 | stations, groups, cargo, destination buckets |
| `FINA` | Finance (011) | 1 | company accounts, loans, ledgers, running costs |
| `RVEH` | Vehicles (012) | 1 | road vehicles, depots, lines and orders |
| `TRAI` | Rails (015) | 2 | trains and their reservations |
| `DMND` | Demand (013) | 1 | destination tables, cargo router |
| `CATL` | Catalog (016) | 1 | company road and rail replacement policies, rail-part renewal notice flags; date-derived availability and introductions |
| `LSTA` | **reserved for 017** | — | monthly/yearly histories (company, line, station, town), in-progress period counters, line target intervals and dispatch timing |
| `CAMR` | Client | 1 | camera (opaque to Core) |

Ids are never reused. Ids beginning with `X` are reserved for experiments and are never written by a
release.

## Rule 1: where new state goes

- **State of a new subsystem** (016's catalog clock and renewal rules; 017's statistics store) goes in
  **its own section**, registered in `SaveSections` after the sections it depends on. A save without
  that section (written before the feature) loads with the new-game defaults: no announcements seen
  yet, renewal off, empty histories. This is the cheapest path and needs no migration.
- **New fields of an existing system** (for example 017's per-line target interval stored in
  `LineRegistry`, or 016's per-vehicle model reference if it changes form) bump **that system's
  section version** and add a migration step from the previous version that fills the default.
- If a feature can choose, prefer its own section: 017's line intervals and dispatch timing MAY live
  in `LSTA` keyed by line id instead of changing `RVEH`, provided the restore order lets `LSTA` find the
  lines (it is read after `RVEH` and `TRAI`).

## Rule 2: what must be saved

Everything that affects future ticks, verbatim (research R1): including in-progress period counters,
partially elapsed intervals ("a mid-period save must permit the same next departure and next period
close as uninterrupted play", 017 FR-007), version counters, caches whose use is budgeted, and random
streams. Every new instance field of a saved type MUST be classified in
`tests/Talt.Core.Tests/Saves/SaveFieldManifest.txt` (`saved`, `derived`, `scratch`, `ref`); the manifest
test fails otherwise.

## Rule 3: content references

State that refers to content (vehicle models, cargo types, industry kinds, road and rail types) stores
the **installed index** in memory as today, and the save stores the content table's id list in `CNTN`.
A feature that adds a content table (016's era-dependent infrastructure options, if indexed) adds its
id list to `CNTN` (bumping `CNTN`'s version) and reads indices through `reader.Content`. Reordering or
extending a catalog must not change what a saved vehicle is (016 FR-006): never write raw indices
without a `CNTN` table behind them.

## Rule 4: dates and calendar identity

Periods and announcements are saved with their calendar identity (year and month numbers, or day
numbers from `GameClock`), never as "n periods ago" relative to the save, so a save loaded later in
real time and a game started in a different year both see the same history (017's "reporting period
… has a calendar identity").

## Rule 5: tests required with every section change

1. Round trip: a fixture game using the new state saves, loads, has the same fingerprint, re-saves to
   the same section bytes, and continues identically (research R13); add the new system to the rich
   fixture builder `SaveTestGames.Rich`.
2. Missing section: a save of the previous release (a committed fixture) loads with the defaults.
3. Version bump: a committed fixture written by the previous section version loads, migrates and
   continues; commit a fixture written by the new version for the next feature.
4. Fingerprint goldens are not changed by save support itself (SC-007).

## Rule 6: whichever feature merges second integrates

014, 016 and 017 are developed in parallel from the same `main`. If 016 or 017 merges **before** 014,
014's implementation adds their state to the sections above before it merges. If 014 merges **first**,
016 and 017 add `CATL` / `LSTA` (or section version bumps) as part of their own implementation,
following this contract. Either way the merged result must pass the field manifest test and the round
trip tests with the new state present in `SaveTestGames.Rich`.

# Contract: Command line, sessions and tools

## Client (`src/Talt.Client`)

No new player-facing option is needed: every map started from the command line or the new-game screen
now comes with industries. Existing map options (`--map`, `--seed`, `--roughness`, `--max-height`,
`--water`, `--sea-level`, `--map-text`) define the map and therefore the industries.

Examples:

```text
dotnet run --project src/Talt.Client -c Release -- --map 512x512 --seed 7 --water 35
dotnet run --project src/Talt.Client -c Release -- --map-text T1-1024x512-s2026-r50-h28-w25-l7
```

Expected: the world appears with industries on the reserved industry sites, and clicking one opens the
industry window. The recorded session (when `--record` is used) stores the map settings so a replay
recreates the same industries.

Internal order change: `GameSetup.Create` generates the map first and then creates the simulation with
that map (previously the simulation was created first and did not see the map). The progress bar
weights stay the same (map 0–80 %, voxel world 80–100 %); simulation creation is the fast step between
them and is not reported.

## Recorded session (`.talt-session.json`)

Format version stays `1`. The `settings` object gains one optional field:

```json
{
  "formatVersion": 1,
  "settings": {
    "seed": 2026,
    "startYear": 1950,
    "mapSizeTiles": 512,
    "roadSpacing": 16,
    "roadRemovalChancePerMille": 150,
    "lineCount": 2000,
    "vehicleCount": 20000,
    "map": "T1-512x512-s2026-r50-h28-w25-l7"
  }
}
```

- Writers emit `"map"` when `ScenarioSettings.Map` is set.
- Readers accept its absence (old sessions) and reject invalid text or a generator version mismatch
  with a clear error.
- Replay regenerates the map from the text and therefore the same industries. This is slower than a
  map-less replay, which is expected for a verification tool.
- Sessions without the field have no industries and keep their existing fingerprints.

## Headless runner (`tools/Talt.Headless`)

`run` gains `--map-text <text>`:

```text
Talt.Headless run [--seed <int>] [--map <tiles>] [--vehicles <int>] [--lines <int>]
                  [--start-year <int>] [--map-text <T1-...>] --ticks <int> [--sequential]
                  [--record <session.json>]
Talt.Headless replay <session.json> [--ticks <int>] [--checkpoint-every <int>] [--sequential]
```

- `--map-text` parses with `MapSettings.TryParse` (error + exit code 2 when invalid) and sets
  `ScenarioSettings.Map`; the game then generates the map and places industries before ticking.
- Without `--map-text`, `run` creates no map and no industries; the state fingerprint is exactly the
  pre-009 one (used by the existing CI reference session).
- The summary prints `industries: <open count>` after `vehicles:`.
- `replay` uses the session's map text automatically; if it has none, it stays map-less.

Example:

```text
dotnet run --project tools/Talt.Headless -c Release -- run --ticks 4000 --map-text T1-512x512-s7-r50-h28-w25-l7
```

Expected: the map is generated once, `industries:` is non-zero, and 4 000 ticks (100 in-game days)
run; the fingerprint is stable across platforms and repetitions.

## Benchmarks

```text
dotnet run --project benchmarks/Talt.Benchmarks -c Release -- --filter "*Industry*" --job short
```

`IndustryBenchmarks` has a 4096 × 4096 map in `GlobalSetup`, 1 000 placed industries, and measures
day update, month update, placement and a full tick at a month boundary. `[MemoryDiagnoser]` must
report no per-operation allocation.

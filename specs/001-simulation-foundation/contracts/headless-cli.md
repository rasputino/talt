# Contract: Headless Runner CLI

**Project**: `tools/Talt.Headless` (console, references `Talt.Core` only; no window or GPU).

## `run`

Builds a scenario and runs it for a number of ticks.

```
Talt.Headless run [--seed <int>] [--map <tiles>] [--vehicles <int>] [--lines <int>]
                  [--start-year <int>] --ticks <int> [--sequential] [--record <session.json>]
```

Output (stdout, one `key: value` per line, stable keys for scripts):

```
ticks: 10000
date: 1950-09-14
fingerprint: 0C11E2A9F07B3D45
setup-ms: 812
simulate-ms: 3410
avg-tick-ms: 0.341
```

## `replay`

Replays a recorded session and verifies outcomes and checkpoints.

```
Talt.Headless replay <session.json> [--ticks <int>] [--checkpoint-every <int>] [--sequential]
```

Output: same keys as `run`, plus one `checkpoint <tick>: <fingerprint> OK|MISMATCH` line per
checkpoint.

## Exit codes

| Code | Meaning |
|---|---|
| 0 | Success (and all checkpoints/outcomes matched for `replay`) |
| 1 | Determinism failure (outcome or fingerprint mismatch); first failing tick on stderr |
| 2 | Invalid arguments or unreadable session file |
| 3 | Unexpected error (stack trace on stderr) |

# Contract: options and recorded sessions

## Client (`Talt.Client`)

| Option | Values | Effect |
|--------|--------|--------|
| `--town-names <style>` | `mixed` (default), `english`, `spanish` | name style of a new map game |
| `--town-size <size>` | `small`, `normal` (default), `large` | initial size scale |
| `--town-service <percent>` | 0–100 (default 0) | debug: share of passengers and mail counted as transported every month |

New-game screen: "Town names" and "Town size" choosers (localized), stored with the other new-game
options. The debug service option is command line only.

## Headless (`tools/Talt.Headless`)

`run` and `replay` accept the same three options when a map is used (`replay` takes them from the session); `run` prints, after the run, the town
count, total population, total buildings and the five largest towns (name, population).

## Recorded session (`.talt-session.json`)

When `settings.map` is present, `settings.towns` is written:

```json
"towns": { "names": "mixed", "size": "normal", "servicePercent": 0 }
```

Missing `towns` reads as the defaults (sessions recorded before feature 008 still load; their map
games replay with towns, so their fingerprints differ from before — documented in the commit).
Map-less sessions never write it.

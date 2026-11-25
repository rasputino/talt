# Contract: command-line options

| Option | Client | Headless (`run`) | Meaning |
|--------|--------|------------------|---------|
| `--station-service <0-100>` | yes | yes | debug: every station's handled cargo is served and that percentage of waiting cargo is picked up every `debug.intervalDays`; part of the scenario settings (recorded, replayed) |

| `--seed-stations <n>` | yes | yes | debug: before the first tick, submits `buildStation` commands for up to n bays beside town roads and next to industries (found by `StationSeeder`, deterministic), so they are recorded and replayed like player actions |

Headless `run` prints, when the game has stations: `stations: N`, `station-groups: N`,
`waiting: <total>`, and the five stations with most waiting cargo.

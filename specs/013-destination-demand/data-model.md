# Data model: Destination demand model

All fields are integers (fixed point where noted); all arrays are dense and reused. "Hashed" means appended to the
fingerprint once the router has applied its first route set (`CargoRouter.EverApplied`).

## DemandRules (`demand.json`, immutable)

| Field | Meaning |
|---|---|
| `cargo[]` | per destination cargo id: `ownTownPerMille`, `populationExponent` a (0–2), `distanceExponent` k (0–3), `source` (`buildings` or `industries`) |
| `maxDestinations` | N other towns per source (1–64, default 32) |
| `rangeTiles` | towns farther than this are ignored (default 1 500) |
| `expiryDays` | waiting destination cargo expires after this (default 60) |
| `routing.updateIntervalDays`, `routing.applyDelayDays` | job cadence (default 4 and 1) |
| `routing.transferPenaltyTicks`, `routing.dwellTicks`, `routing.congestionPenaltyPerMille`, `routing.zeroCapacityPenaltyTicks`, `routing.maxRouteDays`, `routing.pruneMarginTiles`, `routing.iterations` (1–2) | assignment costs |
| `overlay.pairs` | pairs drawn by the client (presentation, not hashed) |

`Fingerprint` (ulong) of the parsed rules is hashed.

## DestinationTables (hashed)

- Per source s (town index, or goods industry slot) and destination cargo c: `ownShare` (ppm), `count`,
  `dest[N]` (town index), `share[N]` (ppm), cumulative `cum[N+1]`; `counter` (long, apportionment counter C).
- Per (s, c, i): monthly statistics `movedThis/Last`, `unservedThis/Last` (units) — i = −1 for own town stored at
  index N.
- `cursor` (next source to refresh), `month` slice bookkeeping.
- Validation: shares sum to 1 000 000 − ownShare when count > 0; destinations distinct, ≠ s for towns.

## LinkGraphSnapshot (not hashed: derived, immutable, owned by the job)

- Nodes: group keys of the company (sorted), node index by binary search; node centre (x, z).
- Edges (CSR by source node): target node, travel ticks, capacity per month (cargo-specific: one snapshot per
  company and cargo).
- Serving: per node bit/list of towns it serves for the cargo.
- Flow estimate: per node, list of (source town, units per month).
- Destination set: towns to route, each with relevance radius.

## RouteTable (hashed)

Per (company, cargo): sorted arrays `groupKey[]`, `town[]`, `hop1[]`, `hop2[]` (group keys, −1 none), `share2[]` (per
mille), `carry[]`. Swapped atomically at the apply tick; carries of entries that keep the same (group, town, hop1, hop2)
are copied from the old table so splits stay exact.

## CargoRouter schedule (hashed)

`nextStartTick`, `pendingApplyTick` (−1 none), `appliedVersion`, `graphVersionAtStart`, `everApplied`.

## Destination packet (StationRegistry, hashed)

| Field | Meaning |
|---|---|
| bucket key | (group slot, cargo, next hop group key or −1) |
| amount | units |
| origin | station id of first pickup group (010 meaning) |
| sourceTown | town that generated it |
| destination | destination town |
| pickupDay | first loaded day, −1 not yet |
| sinceDay | day it started waiting at this group |
| credit | transfer credit carried (money units, long) |

Totals (hashed): `TotalDirectedGenerated`, `TotalUnserved`, `TotalExpired`; existing `TotalSupplied/Lost/Delivered`
also count directed cargo.

## HeldCargo (changed)

Adds `Credit` (long). Merging requires equal (cargo, origin, sourceTown, destination, pickupDay); credits add.
Splitting a part (partial unload/load) splits credit proportionally (floor, remainder kept on the remaining part).

## State transitions of a destination unit

```
generated → (no route) unserved
          → waiting(group, hop) → on board(vehicle) → delivered (destination served, not origin group)
                                                    → waiting(other group, hop') (transfer)
                      → lost (cap, neglect, expiry, station removed)
```

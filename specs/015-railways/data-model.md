# Data model: Railways (015)

## RailLayer (per tile, hashed once rail existed)

| Field | Type | Notes |
|---|---|---|
| links | byte | Dir8 mask; a link implies the neighbour's (or long-link far end's) opposite link |
| type | byte | track type index + 1 (0 = none) |
| flags | byte | `BridgeEnd`, `TunnelPortal`, `OverBridgeX/Z`, `UnderTunnelX/Z` |
| span, longDir | byte | long link of a bridge/tunnel end (like roads) |
| signal | byte | bits 0–1 kind (0 none, 1 block, 2 path, 3 one-way path); bit 2 face towards the lower link direction `a` (a < 4), bit 3 face towards `a + 4` |

Invariants: links matched; signals only on tiles whose links are exactly one opposite pair and that are not long-link
ends; a level crossing tile has a straight rail pair perpendicular to a straight road pair.

## RailPiece

`readonly record struct RailPiece(byte Links, byte Type, RailFeatures Flags, byte Span, byte LongDir, byte Signal)`
with `Pack()` for undo hashing.

## Blocks (derived)

`block[tile]` (int, −1 none), `blockCount`; rebuilt by flood fill over affected components after a change.

## RailDepot

id, owner, tile, direction (the link towards its track). Kept in `RailDepotRegistry` (hashed with the train system).

## Rolling stock model (rail.json)

id, nameKey, kind (locomotive | wagon), category (wagons: passengers, mail, bulk, tanker, box; locomotives: none or
passengers/mail), capacity, powerKw, tractiveEffortKn, massTonnes, lengthMilliTiles, speedKmh, power (steam, diesel,
electric), price, runningCostPerYear, lifeYears, reliabilityPerMille, firstYear, lastYear, loadPerStep, color.

## Train

| Field | Notes |
|---|---|
| id, owner | stable id (never reused) |
| parts | list of (model, cargo, CargoHold, purchase day, running-cost handle) in order, locomotives first |
| line, stopIndex | shared `LineRegistry`; −1 = none |
| depot | depot it is in, or −1 |
| steps | deque of (fromTile, direction, length) from tail to end of authority |
| headStep, headOffset | head position on the deque (units) |
| speed | units per tick |
| state flags | stopped, in depot, loading, reversing, going to depot, broken down, waiting at signal, no route |
| target | stop station or depot id, stop tile and stop offset (where the head must stop) |
| servedMask | wagons already served at this stop (pull-forward) |
| route | planned steps beyond the authority (reused buffer) |
| loadTimer, waitTicks | dwell and stuck counters |
| ageDays, reliability, daysSinceService, serviceInterval, profits, value | like road vehicles |

State transitions: InDepot → (start with line) Leaving → Running ⇄ WaitingAtSignal → Stopping → Loading
(→ PullingForward → Loading)* → Running … → GoingToDepot → InDepot. Breakdown overlays Running.

## Ownership (state)

`owner[tile]` (int, train id, 0 none): exactly the tiles of every train's deque; not hashed separately (derived from
the trains' deques, which are hashed).

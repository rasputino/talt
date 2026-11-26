# Contract: commands (recorded sessions)

Every command has `companyId`. Reason codes are added to `ConstructionReasons` and localized as
`construction.reason.<code>`.

| type | fields | refused with |
|---|---|---|
| `buildDepot` | x, z | construction reasons of a bay (no-road, not-flat, water, object-in-the-way, town-refuses, not-enough-money) |
| `removeDepot` | x, z | `no-depot`, `owned-by-other`, `vehicle-in-the-way` |
| `buyVehicle` | depotId, model (id), cargo (id or ""), | `no-depot`, `unknown-model`, `model-not-available`, `cargo-not-allowed`, `not-enough-money` |
| `sellVehicle` | vehicleId | `no-vehicle`, `owned-by-other`, `not-in-depot` |
| `cloneVehicle` | vehicleId | as `buyVehicle` |
| `startVehicle` | vehicleId, start (bool) | `no-vehicle`, `owned-by-other` |
| `sendToDepot` | vehicleId | `no-vehicle`, `no-depot-reachable` (toggles off when already ordered) |
| `setVehicleCargo` | vehicleId, cargo | `not-in-depot`, `hold-not-empty`, `cargo-not-allowed` |
| `setServiceInterval` | vehicleId, days (0–1000) | `no-vehicle`, `invalid-value` |
| `createLine` | stations (array of ids), vehicleId (−1 none) | `no-station`, `owned-by-other`, `too-many-stops` |
| `deleteLine` | lineId | `no-line` |
| `renameLine` | lineId, name (≤ 32) | `no-line`, `name-too-long` |
| `editLineStops` | lineId, op (`add`, `remove`, `move`, `rule`), index, stationId, to, rule | `no-line`, `invalid-index`, `no-station`, `too-many-stops`, `last-stop` |
| `assignLine` | vehicleId, lineId (−1 unassign) | `no-vehicle`, `no-line`, `wrong-kind` (no stop of the vehicle's kind) |

Existing commands that now also refuse with `vehicle-in-the-way`: `removeRoad`, `setOneWay` is allowed, `removeStation`
(bay with a vehicle on it), `clearArea` (never touches roads), `terraform` (never touches road tiles).

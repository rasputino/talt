# Contract: station commands (recorded sessions)

All registered in `CommandSerializer`. Coordinates are tiles.

| type | properties | kind | refusal reasons (`construction.reason.*`) |
|------|------------|------|---------|
| `buildStation` | `companyId`, `pieceType` (`bus-bay`, `bus-stop`, `truck-bay`, `truck-stop`), `x`, `z` | construction | `out-of-map`, `not-flat`, `water`, `occupied`, `no-road`, `not-straight-road`, `not-owner`, `town-refuses`, `adjacent-two-stations`, `station-too-spread`, `not-enough-money` |
| `buildWarehouse` | `companyId`, `size` (`small`, `medium`, `large`), `x`, `z` (footprint min corner) | construction | as above (no join reasons) |
| `removeStation` | `companyId`, `x`, `z` | construction | `no-station`, `not-owner` |
| `renameStation` | `companyId`, `stationId`, `name` | plain | `no-station`, `not-owner`, `name-too-long` |

The client's tools send `bus-bay`/`bus-stop` (and truck equivalents) according to the tile under the
cursor: a straight road tile gives the drive-through `*-stop` piece, any other tile the `*-bay`.

Station construction commands clear the construction undo record. The road removal and road building
planners gain the reason `station-road` (a station uses this road); the clear tool gains
`use-station-tool` for station tiles.

Recorded session settings gain an optional object, written only when set:

```json
"stations": { "servicePercent": 60 }
```

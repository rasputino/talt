# Contract: Recorded Session Format

**Version**: 1 | **Encoding**: UTF-8 JSON | **Extension**: `.talt-session.json`

A recorded session contains everything needed to reproduce a game exactly: the scenario settings
and the ordered player commands with the tick at which each was processed.

## Schema

```json
{
  "formatVersion": 1,
  "gameVersion": "0.1.0",
  "settings": {
    "seed": 2026,
    "startYear": 1950,
    "mapSizeTiles": 512,
    "roadSpacing": 16,
    "roadRemovalChancePerMille": 150,
    "lineCount": 2000,
    "vehicleCount": 20000
  },
  "entries": [
    { "tick": 120, "outcome": "applied",
      "command": { "type": "addVehicle", "lineId": 3, "maxSpeed": 21845, "startEdgeIndex": 0 } },
    { "tick": 480, "outcome": "rejected", "reason": "vehicle-not-found",
      "command": { "type": "removeVehicle", "vehicleId": 999999 } }
  ],
  "checkpoints": [
    { "tick": 1000, "fingerprint": "9F3A51C0D2E4B776" },
    { "tick": 10000, "fingerprint": "0C11E2A9F07B3D45" }
  ]
}
```

## Rules

- `formatVersion` is mandatory; readers reject unknown major versions with a clear error.
- `entries` are ordered by `tick`, then by original submission order; several entries may share a
  tick.
- `tick` is the tick **at whose start** the command was processed (the simulation's `CurrentTick` value
  before increment).
- `command.type` is a stable discriminator: `addVehicle`, `removeVehicle` (test-only commands use a
  `test.` prefix and are only accepted by test builds).
- `outcome` is `applied` or `rejected`; `reason` is present only when rejected. On replay, a
  different outcome is a determinism failure.
- `checkpoints` are optional; fingerprints are 64-bit values as 16 uppercase hex digits. On replay,
  a mismatch at any checkpoint is a determinism failure reporting the first failing tick.
- All numbers are integers (no floating point anywhere in the format).

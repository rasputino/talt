# Contract: road commands (recorded-session JSON)

All are `ConstructionCommand`s: validate = plan + check money, apply = commit the validated plan, undo with
feature 006's `undoConstruction`. Registered in `CommandSerializer`. `roadType` is a type `id` from
roads.json. Coordinates are tiles.

| type | properties | meaning |
|---|---|---|
| `buildRoad` | companyId, roadType, x0, z0, x1, z1 | proposed path from (x0,z0) to (x1,z1); upgrades own roads of worse types on the path |
| `buildBridge` | companyId, roadType, x0, z0, x1, z1 | straight axis bridge between the two end tiles |
| `buildTunnel` | companyId, roadType, x, z, direction (Dir8, axis) | tunnel from portal (x,z) |
| `removeRoad` | companyId, x0, z0, x1, z1 | removes pieces along the 8-direction line |
| `setOneWay` | companyId, x, z | cycles two-way → one-way (lower direction first) → other way → two-way |
| `buildMotorway` | companyId, x0, z0, x1, z1 | two carriageways along the 8-direction line |
| `buildMotorwayConnector` | companyId, x, z, exit (bool) | exit/entry ramp on the outer side |

## New reason codes (`construction.reason.<code>`, en + es)

`no-path`, `road-type-unavailable`, `unknown-road-type`, `downgrade`, `industry`, `bridge-too-long`,
`bridge-ends`, `bridge-blocked`, `tunnel-no-exit`, `tunnel-too-long`, `tunnel-blocked`, `not-straight`,
`motorway-junction`, `not-a-road`, `town-needs-road`, `use-road-removal`, `not-a-carriageway`.
Existing codes reused: `water`, `too-steep`, `owned-by-other`, `too-large`, `not-enough-money`,
`nothing-to-do`, `object-in-the-way`.

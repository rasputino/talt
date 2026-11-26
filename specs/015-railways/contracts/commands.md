# Commands (015)

All commands are recorded in sessions and registered in `CommandSerializer`. Construction commands derive from
`ConstructionCommand` (plan → check → commit, money and undo of feature 006). Coordinates are tile coordinates.

| Type | Properties | Effect |
|---|---|---|
| `buildRail` | companyId, railType, x0, z0, x1, z1 | track along the proposed path (A*, ≤ 45° per tile, slopes) |
| `buildRailBridge` | companyId, railType, x0, z0, x1, z1 | straight rail bridge between two end tiles |
| `buildRailTunnel` | companyId, railType, x, z, direction | straight rail tunnel from a portal tile |
| `removeRail` | companyId, x0, z0, x1, z1 | removes track on a straight/45° line (refused under or reserved by a train) |
| `convertRail` | companyId, railType, x0, z0, x1, z1 | changes the type of the company's track on a line (electrify) |
| `setSignal` | companyId, x, z, kind, faces | kind 0 removes; faces bit 0 = towards the lower link direction, bit 1 = the other |
| `buildRailStation` | companyId, x, z, axis, platforms, length | P × L platform tiles starting at (x, z); axis 0 = along X, 1 = along Z |
| `buildRailDepot` | companyId, x, z, direction | depot with its entrance towards `direction` (a straight Dir8) |
| `removeRailDepot` | companyId, depotId | when empty |
| `buyRailVehicle` | companyId, depotId, model, cargo, trainId | new train (trainId −1) or appended to a train stopped in the depot |
| `sellRailVehicle` | companyId, trainId, part | part −1 sells the whole train; train must be in its depot |
| `startTrain` | companyId, trainId, start | start/stop |
| `sendTrainToDepot` | companyId, trainId | toggles |
| `assignTrainLine` | companyId, trainId, lineId | −1 unassigns |
| `setTrainCargo` | companyId, trainId, part, cargo | in depot, empty part |
| `cloneTrain` | companyId, trainId | same parts and line in the same depot |

Removal of a rail station tile goes through the existing `removeStation`, which also removes its track.
Reasons added to `ConstructionReasons`: `train-in-the-way`, `no-rail`, `signal-here`, `not-straight`, `turn-too-sharp`,
`no-depot`, `train-too-long`, `wrong-track`.

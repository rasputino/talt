# Contract: Construction Commands

All are `ISimulationCommand`s registered in `CommandSerializer`; JSON form inside recorded sessions
(`{"type": ..., ...}`). All coordinates are tile coordinates; rectangles are inclusive and may be
given in any corner order; they are clipped to the map.

| Type | JSON properties | Effect when applied |
|---|---|---|
| `terraform` | `companyId`, `mode` (`raise`/`lower`/`level`), `x0`, `z0`, `x1`, `z1`, `startX`, `startZ` (level target tile; ignored otherwise) | changes heights with cascade, removes trees on changed tiles, spends cost, records undo |
| `clearArea` | `companyId`, `x0`, `z0`, `x1`, `z1` | removes trees and the company's own objects in the rectangle, spends cost, records undo |
| `undoConstruction` | `companyId` | restores the last recorded action of the company, refunds |

## Validation order (all commands)

1. Scenario has construction state → else `no-map`.
2. Company exists → else `company-not-found`.
3. Plan (terraform / clear) or undo checks; first per-tile problem gives the reason.
4. Nothing would change → `nothing-to-do` (no cost, no undo record).
5. Cost > `maxActionCost` or changed tiles > `maxTilesPerAction` → `too-large`.
6. Cost > balance → `not-enough-money`.

## Reason codes

| Code | Meaning (player text key `construction.reason.<code>`) |
|---|---|
| `no-map` | the game has no map |
| `company-not-found` | unknown company |
| `nothing-to-do` | nothing would change |
| `water` | a tile that must change is water |
| `would-flood` | land would end below neighbouring water |
| `height-limit` | height below 1 or above the maximum |
| `too-steep` | levelling could not keep the one-step rule around the area |
| `object-in-the-way` | an own object stands on a tile that must change |
| `owned-by-other` | a tile is owned by a town or another company |
| `too-large` | too many tiles or cost too large |
| `not-enough-money` | cost above the balance |
| `nothing-to-undo` | no undo record for the company |
| `undo-expired` | the undo window has passed |
| `changed-since` | a tile of the last action changed since |

## Guarantees

- Validate never changes state; Apply only runs after a successful Validate in the same tick and
  applies exactly the plan validated (preview cost == money spent, SC-002).
- All-or-nothing: no partial application (FR-013); `clearArea` leaves others' objects standing (non-blocking problems) and clears the rest.
- Cost = Σ |Δheight| × `terraformStepCost` + trees × `clearTreeCost` + Σ objects' demolish cost.
- After a terraform, every 4-neighbour pair of land tiles with at least one changed tile differs by
  at most 1 (SC-004).

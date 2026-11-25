# Research: Economy and finance

## R1 Money representation

- **Decision**: keep the internal unit of features 005/006 (1 = one US dollar, whole units) in `long`.
  Products that can exceed 64 bits (payment, inflation, value) use one `Int128` intermediate and are
  rounded down once; results saturate at ±(2⁶²) instead of wrapping.
- **Rationale**: the HUD, construction costs and data files already use this unit; cents add nothing at
  tycoon scale; currencies convert at display time (`MoneyFormatter`).
- **Alternatives**: cents (changes every data file and golden); `decimal` (not allowed in state).

## R2 Payment formula

- **Decision**: `pay = floor(rate × amount × distance × timeFactor‰ × paymentLevel / (1000 × 1000 × 10⁶))`
  where `rate` is money per 1 000 units per tile (`finance.json` `payments.<cargo>.rate`), `distance` =
  max(`minDistance`, Manhattan distance between the station centres), and `timeFactor‰` = 1000 −
  decay‰ × max(0, days − `freeDays`), not below `minimumValuePercent` × 10 (decay and minimum from
  `cargo.json`, free days from `finance.json`). Paid to the delivery's company, booked as income of
  its cargo.
- **Rationale**: linear in amount and distance (spec US2 #3), simple to explain in the window, exact.
- **Alternatives**: curve tables per cargo (more data, no gain now); Euclidean distance (needs a square
  root).
- Rates live in `finance.json` (all balancing in one file) so `cargo.json` and the content fingerprint
  (industry goldens) are untouched; validation requires one entry per cargo.

## R3 Origin of a delivery

- **Decision**: `StationRegistry.TryGetCentre(id, out x, out z)` answers for live stations (centre of the
  bounding box) and removed ones (a `_removedCentre` dictionary filled on removal, hashed only when not
  empty). The destination is always live.
- **Alternatives**: coordinates in each packet/hold (widens 010 structures used by 012).

## R4 Accruals and exactness

- **Decision**: every periodic charge keeps a remainder: `charge = (yearly × num + carry) / den`,
  `carry = remainder`. Interest: `loan × ratePerMille` over `12 000`; maintenance: yearly total over 12;
  running costs: yearly total over days in the year (365/366). The carry is per company and per kind,
  hashed.
- **Rationale**: twelve monthly charges sum exactly to the yearly figure (SC-002, US3 #3).

## R5 Property counts

- **Decision**: `ConstructionState` keeps `OwnedRoadTiles[company][roadType]`, updated by a pair of
  `Uncount(tile)`/`Count(tile)` calls around every tile mutation path (`Commit`, `ApplyUndo`,
  `PlaceTownObject`, `PlaceTownRoad`, `PlaceInitialRoad`). Only tiles whose owner is a company and that
  have a road piece count. Not hashed (derived); a test compares it with a full scan after random
  actions. Station pieces and warehouses are counted at month end from the registry (O(stations)).
- **Bridges and tunnels**: their end tiles count as road tiles; spans have no maintenance of their own
  now (can be added as data later).
- **Alternatives**: monthly full scan (5–10 ms on 4 096²).

## R6 Company value

- **Decision**: at month end, value = cash − loan + Σ property × build cost × `propertyValuePercent` /
  100 × cost level (roads by type from `roads.json`, pieces and warehouses from `stations.json`), plus
  vehicle values from feature 012 (a hook in `RunningCosts`: each registration carries a value).
- **Rationale**: no duplicate price tables; value follows inflation.

## R7 Inflation

- **Decision**: two levels in parts per million (start 1 000 000). Each month end, while months applied <
  `years × 12`: `level += level × yearlyBasisPoints / 120 000` (rounded down). Costs use the cost level:
  construction (`ConstructionCommand.Check` multiplies the planned cost once, a flag on the plan prevents a
  second application), maintenance, running costs, loan step and maximum (rounded down to 1 000).
  Payments use the payment level. Interest rates do not inflate.
- With inflation off the levels stay at 1 000 000 and multiplying returns the same number, so nothing
  changes for existing games.

## R8 Loan

- **Decision**: one command `changeLoan(companyId, mode)` with `mode` = `borrow` | `repay` |
  `borrowMax` | `repayMax`. Borrow adds one step (or as many as fit) up to the maximum; repay removes one
  step (or as many as cash covers, the last one possibly partial when the loan is not a multiple of the
  step). Loan movements change cash but are not ledger categories (they are not income or expense).
- Reasons: `maxLoan`, `noLoan`, `notEnoughCash`, `gameOver`, `noMap`, `companyNotFound`.

## R9 Ledger

- **Decision**: categories `construction`, `vehiclePurchase`, `runningCosts`, `propertyMaintenance`,
  `loanInterest`, `other`, then one `income:<cargo>` per cargo. Each period is a `long[]` of that length.
  24 monthly periods in a ring plus a `List<long[]>` of years (one array allocated per in-game year).
  Every booking adds to the current month and current year. Month end: charges first, then rotation;
  year end (runs after month handlers): rotation. Events fired at a boundary therefore book into the
  period that just ended.

## R10 Bankruptcy and game over

- **Decision**: at month end after charges: `cash < 0 && loan >= maxLoan` → streak + 1, else 0. Streak =
  `warningMonths` (3) and every later month → a warning money event; streak ≥ `gameOverMonths` (6) and not
  sandbox → `IsGameOver`. `GameSimulation.ProcessCommands` rejects every command after game over with
  reason `gameOver`. The simulation keeps ticking; the client stops at the game-over screen.

## R11 Hashing

- **Decision**: `FinanceSystem.AppendTo` appends nothing until the first booking, loan change, running
  cost registration or non-default setting (like stations' FR-030). After that: rules fingerprint,
  settings, every company's loan, streak, game over, value, carries, ledger, levels, running costs.
  Company cash is already hashed by `ConstructionState`.
- Consequence: map games that build (construction, road and station replay tests) get new goldens on
  purpose (their ledgers are hashed); untouched map games and all map-less goldens are unchanged.

## R12 Debug paid service

- **Decision**: `DeliveryConsumer.DebugService`; `StationRegistry.DebugPickups` reports each loaded part
  to `DeliveryListener` with origin and destination = the group's key station (no change to station
  state or totals). `FinanceSystem` pays these only when `FinanceSettings.DebugServicePays`, using
  `debugServiceDistance` (40) instead of the station distance.

## R13 Client feed

- **Decision**: `FinanceSystem` writes `MoneyEvent(company, amount, x, z, kind)` into a fixed ring buffer
  (512, oldest overwritten), not state; the client drains it each frame into floating texts (rise 2 s,
  fade), projected like `TownLabels`, drawn only within the fog distance, at most 64 at once. Kinds:
  income, expense (construction at the cursor is already shown), bankruptcy warning, game over.

## R14 Undo

- **Decision**: undo refunds book `+cost` in `construction` in the current period (spec edge case).

## R15 Sandbox and options

- **Decision**: `FinanceSettings(Sandbox, Inflation, DebugServicePays)` in `ScenarioSettings.Finance`,
  written to sessions only when not default. New-game options (two toggles), client options
  `--sandbox`, `--inflation`, `--station-service-pays`, same in the headless tool.

## R17 Infinite money (requirement added after planning)

- **Decision**: `Company.HasInfiniteMoney` (+ `InfiniteMoneySinceTick`, −1 = never), hashed by finance and making
  it active. Turned on by `FinanceSettings.InfiniteMoney` at game creation (since tick 0) or by the recorded command
  `enableInfiniteMoney(companyId)`; refused when already on (`already-infinite`). `Company.Book` then updates the
  ledger only, so cash stays frozen while the window keeps showing what things cost and earn;
  `ConstructionCommand.Check` skips the money check; `changeLoan` is refused (`infinite-money`); bankruptcy resets
  and never counts. Nothing can turn it off. `FinanceSystem.UsedInfiniteMoney(companyId)` is the flag for feature
  020 (goals and scores).
- **UI**: a new-game option; a finance window button that opens a confirmation overlay (confirm / cancel) before
  submitting the command; the HUD shows "∞" instead of the cash and the window a line saying the mode is on.
- **Alternatives**: topping cash up to a huge value (wrong figures, overflow risk); hiding costs (the player loses
  the information of what things cost).

## R16 Starting cash

- **Decision**: stays as `startingBalance` in `construction.json` (feature 006 field, overridden by many
  tests); `finance.json` holds the loan (start 0, max 500 000, step 10 000, 5 %).

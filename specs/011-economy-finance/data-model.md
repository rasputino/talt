# Data model: Economy and finance

All values are integers. Money is `long` in the internal unit (1 = one US dollar).

## Company (`Talt.Core.Construction.Company`, extended)

| Field | Type | Notes |
|---|---|---|
| Id | int | 0 = player |
| Balance (cash) | long | hashed by `ConstructionState` (unchanged) |
| Loan | long | 0 … MaxLoan; hashed by finance |
| Ledger | Ledger | per company |
| Value | long | recomputed at month end |
| BankruptMonths | int | consecutive bankrupt month ends |
| IsGameOver | bool | set once; refuses commands |
| HasInfiniteMoney | bool | set once (research R17); never cleared |
| InfiniteMoneySinceTick | long | −1 until turned on |
| InterestCarry, MaintenanceCarry, RunningCarry | long | accrual remainders (R4) |

`Book(category, cargo, signedAmount)` changes cash and the ledger together; `Spend`/`Refund` of 006 book
`construction`. Loan movements use `Borrow`/`Repay` (cash and loan only).

## LedgerCategory

`Construction=0, VehiclePurchase=1, RunningCosts=2, PropertyMaintenance=3, LoanInterest=4, Other=5`;
income of cargo `c` is slot `6 + c`. `Ledger.SlotCount = 6 + cargoCount`.

## Ledger

| Field | Type | Notes |
|---|---|---|
| Months | long[24][slots] | ring; `MonthYear/MonthNumber` per entry |
| CurrentMonth | int | ring index |
| MonthCount | int | months recorded (≤ 24) |
| Years | List<(int Year, long[] Slots)> | one per game year, current last |

Queries: `MonthTotal(back, slot)`, `YearTotal(back, slot)`, `Income/Expenses(period)`.

## FinanceRules (`finance.json`)

| Field | Type | Validation |
|---|---|---|
| loan.start, loan.max, loan.step | long | 0 ≤ start ≤ max; step > 0; start and max multiples of step |
| loan.interestPerMille | int | 0 … 1000 (yearly) |
| payments.<cargoId>.rate | long | ≥ 0, money per 1 000 units per tile; one entry per cargo |
| payments.<cargoId>.freeDays | int | 0 … 3650 |
| minDistance | int | ≥ 1 |
| maintenance.roads.<roadTypeId> | long | ≥ 0 yearly per tile; one per road type |
| maintenance.stationPieces.<pieceId> | long | ≥ 0 yearly per piece; one per piece type |
| maintenance.warehouses.<sizeId> | long | ≥ 0 yearly per warehouse; one per size |
| propertyValuePercent | int | 0 … 100 |
| bankruptcy.warningMonths, bankruptcy.gameOverMonths | int | 1 ≤ warning ≤ gameOver ≤ 120 |
| inflation.costBasisPoints, inflation.paymentBasisPoints | int | 0 … 5 000 yearly |
| inflation.years | int | 0 … 1 000 |
| debug.serviceDistance | int | ≥ 1 |

Ids are checked against `GameContent`, `RoadRules` and `StationRules` when the simulation is created.

## FinanceSettings (scenario)

`Sandbox` (bool), `Inflation` (bool), `DebugServicePays` (bool), `InfiniteMoney` (bool); default all false.

## FinanceSystem

| Field | Type | Notes |
|---|---|---|
| CostLevel, PaymentLevel | long ppm | start 1 000 000 |
| InflationMonths | int | months applied |
| RunningCosts | RunningCosts | registrations |
| Events | MoneyEventFeed | not state |
| Active | bool | something booked / registered (hashing switch) |
| TotalPaid, Deliveries | long | statistics, hashed |

## RunningCosts

SoA: `Company[]`, `YearlyCost[]`, `Value[]`, `Active[]`, free list; `TotalYearly(company)` and
`TotalValue(company)` maintained on register/unregister. `Register(company, yearlyCost, value) → handle`,
`Unregister(handle)`.

## Payment (`PaymentQuote`, result)

`Amount`, `Distance`, `Days`, `TimeFactorPerMille` — from `PaymentCalculator.Quote(rate, freeDays, decay,
minPercent, amount, distance, days, paymentLevel)`.

## MoneyEvent (client feed)

`CompanyId`, `Amount` (signed), `X`, `Z` (tile; −1 = none), `Kind` (Income, Expense, BankruptcyWarning,
GameOver).

## State transitions

- Month end: maintenance → interest → inflation → company value → bankruptcy → ledger month rotation.
- Year end: ledger year rotation.
- Bankruptcy: streak 0 → … → warning (= warningMonths) → game over (≥ gameOverMonths, not sandbox);
  reset to 0 by any non-bankrupt month end; game over is final.

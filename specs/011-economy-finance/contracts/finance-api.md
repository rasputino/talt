# Contract: finance API (`Talt.Core.Finance`)

## GameSimulation

- `FinanceSystem? Finance` — null in map-less games.
- After `Finance.IsGameOver(Company.PlayerId)`, every queued command is rejected with `gameOver`.

## FinanceSystem

- `FinanceRules Rules`, `FinanceSettings Settings`
- `long CostLevel`, `long PaymentLevel` (ppm), `long Inflate(long cost)`, `long InflatePayment(long pay)`
- `long MaxLoan`, `long LoanStep` (inflated, rounded down to 1 000, step ≥ 1 000)
- `RunningCosts RunningCosts`
- `MoneyEventFeed Events` — `bool TryDequeue(out MoneyEvent e)`; not state, never blocks, overwrites oldest
- `long Pay(in CargoDelivery delivery)` — prices and books a delivery (also called by `OnDelivered`);
  returns the amount paid
- `PaymentQuote Quote(int cargo, int amount, int distance, int days)` — pure, for tests and the UI
- `CompanyFinance Report(int companyId)` — snapshot for the window (cash, loan, max, step, interest,
  value, streak, game over)
- `bool IsGameOver(int companyId)`
- Daily/monthly/yearly handlers are registered by `GameSimulation` (finance after stations).

## RunningCosts (for feature 012)

- `int Register(int companyId, long yearlyCost, long value)` → handle
- `void Unregister(int handle)`
- `long YearlyTotal(int companyId)`, `long ValueTotal(int companyId)`
- Charged daily: `(yearlyTotal × costLevel/10⁶ + carry) / daysInYear`.

## Company (extended)

- `long Loan`, `Ledger Ledger`, `long Value`, `int BankruptMonths`, `bool IsGameOver`
- `internal void Book(LedgerCategory category, long amount, int cargo = -1)`

## Ledger

- `int SlotCount`, `int MonthCount`, `int YearCount`
- `long Month(int back, int slot)`, `(int Year, int Month) MonthDate(int back)`
- `long Year(int back, int slot)`, `int YearNumber(int back)`
- `long MonthIncome(int back)`, `long MonthExpenses(int back)`, `long YearIncome/YearExpenses(int back)`
- `static int IncomeSlot(int cargo)`

## StationRegistry additions (feature 010)

- `bool TryGetCentre(int stationId, out int x, out int z)` — live or removed stations
- `int PieceCountOf(int companyId, int pieceType)`, `int WarehouseCountOf(int companyId, int size)` (month end)
- Debug pickups are reported with `DeliveryConsumer.DebugService`.

## ConstructionState additions

- `int OwnedRoadTiles(int companyId, int roadType)` (incremental)
- `long CostLevel { get; internal set; }` (set by finance; 1 000 000 without inflation)

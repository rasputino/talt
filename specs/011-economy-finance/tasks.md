# Tasks: Economy and finance

**Input**: design documents in `specs/011-economy-finance/` (plan, research R1–R16, data-model, contracts,
quickstart). Tests are required by the constitution (IV): each story has its tests.

## Phase 1: Setup

- [X] T001 Create `src/Talt.Core/Finance/Data/finance.json` with the values of contracts/data-files.md (one payment per cargo of `cargo.json`, maintenance for every road type, station piece and warehouse size) and embed it in `src/Talt.Core/Talt.Core.csproj`
- [X] T002 [P] Add `FinanceSettings` (data-model.md FinanceSettings.Sandbox/Inflation/DebugServicePays) in `src/Talt.Core/Finance/FinanceSettings.cs` and `ScenarioSettings.Finance` + `EffectiveFinance` in `src/Talt.Core/Scenarios/ScenarioSettings.cs`

## Phase 2: Foundational

- [X] T003 Implement `FinanceRules` parsing and validation of every field of data-model.md FinanceRules (loan.*, payments.*, minDistance, maintenance.*, propertyValuePercent, bankruptcy.*, inflation.*, debug.serviceDistance), cross-checks against `GameContent`, `RoadRules`, `StationRules`, and a rules fingerprint in `src/Talt.Core/Finance/FinanceRules.cs`
- [X] T004 [P] Implement `LedgerCategory` (data-model.md LedgerCategory values; `VehiclePurchase` stays empty until feature 012) and `Ledger` (data-model.md Ledger.Months/CurrentMonth/MonthCount/Years; month and year rotation, queries, hashing) in `src/Talt.Core/Finance/LedgerCategory.cs` and `src/Talt.Core/Finance/Ledger.cs`
- [X] T005 Extend `Company` (data-model.md Company.Loan/Ledger/Value/BankruptMonths/IsGameOver/carries) with `Book`, `Borrow`, `Repay`; `Spend`/`Refund` book `construction` in `src/Talt.Core/Construction/Company.cs`
- [X] T006 Implement `FinanceSystem` skeleton (creation, attach to stations as `DeliveryListener`, hashing switch R11 with data-model.md FinanceSystem.Active, `MoneyEventFeed` R13 with data-model.md MoneyEvent.CompanyId/Amount/X/Z/Kind) in `src/Talt.Core/Finance/FinanceSystem.cs` and `src/Talt.Core/Finance/MoneyEvent.cs`; create it in `src/Talt.Core/Simulation/GameSimulation.cs` (map games only, events after stations, AppendTo after stations)
- [X] T007 [P] Register finance types in `tests/Talt.Core.Tests/Determinism/NoFloatingPointStateTests.cs`
- [X] T008 [P] Tests for `FinanceRules` (shipped file valid, each field error names the field, missing/unknown cargo/road/piece ids) in `tests/Talt.Core.Tests/Finance/FinanceRulesTests.cs`
- [X] T009 [P] Tests for `Ledger` (bookings in month and year, rotation keeps 24 months, year list, totals) in `tests/Talt.Core.Tests/Finance/LedgerTests.cs`

**Checkpoint**: build passes; map-less goldens unchanged.

## Phase 3: User Story 1 - Start money and loan (P1)

**Goal**: cash, loan facility, monthly interest. **Independent test**: commands change cash/loan by steps; interest per formula.

- [X] T010 [US1] Implement `ChangeLoanCommand` (contracts/commands.md modes and reasons) in `src/Talt.Core/Finance/LoanCommand.cs`; register in `src/Talt.Core/Commands/CommandSerializer.cs`
- [X] T011 [US1] Monthly interest with carry (R4) and `MaxLoan`/`LoanStep` in `src/Talt.Core/Finance/FinanceSystem.cs`
- [X] T012 [US1] Refuse every command after game over with `gameOver` in `src/Talt.Core/Simulation/GameSimulation.cs`
- [X] T013 [P] [US1] Tests: start values, borrow/repay/borrowMax/repayMax, refusals, interest exact over a year, command JSON round trip in `tests/Talt.Core.Tests/Finance/LoanTests.cs`

## Phase 4: User Story 2 - Deliveries pay (P1)

**Goal**: payment formula wired to 010's deliveries. **Independent test**: hold load/unload pays exactly.

- [X] T014 [P] [US2] Implement `PaymentCalculator.Quote` (R2) in `src/Talt.Core/Finance/PaymentCalculator.cs`
- [X] T015 [US2] `StationRegistry.TryGetCentre` with removed-station centres (R3, hashed only when not empty) in `src/Talt.Core/Stations/StationRegistry.cs` and `src/Talt.Core/Stations/StationRegistry.Construction.cs`
- [X] T016 [US2] `FinanceSystem.Pay`/`OnDelivered`: price, book income of the cargo, money event at the destination station in `src/Talt.Core/Finance/FinanceSystem.cs`
- [X] T017 [US2] Debug paid service (R12): `DeliveryConsumer.DebugService` in `src/Talt.Core/Stations/CargoDelivery.cs`, report debug pickups in `src/Talt.Core/Stations/StationRegistry.Supply.cs`, pay with `debug.serviceDistance` only when `DebugServicePays`
- [X] T018 [P] [US2] Tests: formula (linear in amount and distance, free days, decay, minimum, min distance, payment level, zero), real load→unload pays and books, same-group unload pays nothing, removed origin, debug pays on/off in `tests/Talt.Core.Tests/Finance/PaymentTests.cs`

## Phase 5: User Story 3 - Running and maintenance costs (P1)

**Goal**: recurring costs. **Independent test**: counts × data; running costs exact per year.

- [X] T019 [US3] Incremental owned-road counts (R5) around every tile mutation path in `src/Talt.Core/Construction/ConstructionState.cs`
- [X] T020 [US3] `StationRegistry.PieceCountOf`/`WarehouseCountOf` in `src/Talt.Core/Stations/StationRegistry.cs`
- [X] T021 [P] [US3] Implement `RunningCosts` (data-model.md RunningCosts) in `src/Talt.Core/Finance/RunningCosts.cs`
- [X] T022 [US3] Monthly maintenance and daily running costs with carries in `src/Talt.Core/Finance/FinanceSystem.cs`
- [X] T023 [P] [US3] Tests: road counts equal a full scan after building/removing/undo/town growth, maintenance per month exact over a year, towns' roads free, running costs exact over a year, cash may go negative in `tests/Talt.Core.Tests/Finance/CostTests.cs`

## Phase 6: User Story 4 - Finance window (P2)

**Goal**: window with ledger, value, loan controls, chart. **Independent test**: figures equal the ledger.

- [X] T024 [US4] Company value at month end (R6) and `Report` snapshot in `src/Talt.Core/Finance/FinanceSystem.cs`
- [X] T025 [P] [US4] `BarChart` widget in `src/Talt.Client/Ui/Widgets/BarChart.cs`
- [X] T026 [US4] `FinanceWindow` (three-year table, totals, cash/loan/max/interest/value, borrow/repay buttons with refusal messages, 24-month chart) in `src/Talt.Client/Screens/FinanceWindow.cs`; HUD money button opens it, negative cash red, key binding in `src/Talt.Client/Input/Data/actions.json`, in `src/Talt.Client/Screens/InGameScreen.cs`
- [X] T027 [US4] Floating money texts drained from the feed in `src/Talt.Client/Screens/MoneyTexts.cs` and `src/Talt.Client/GameSession.cs`
- [X] T028 [P] [US4] Localization keys (en, es) in `src/Talt.Client/Localization/Strings.en.json` and `Strings.es.json`
- [X] T029 [P] [US4] Tests: ledger = cash change over years (property test), value formula in `tests/Talt.Core.Tests/Finance/LedgerCashTests.cs`; window texts and chart data in `tests/Talt.Client.Tests/Screens/FinanceWindowTests.cs`

## Phase 7: User Story 5 - Bankruptcy (P2)

- [X] T030 [US5] Bankruptcy streak, warning and game over (R10) in `src/Talt.Core/Finance/FinanceSystem.cs`
- [X] T031 [US5] Warning message and game-over screen (final figures, back to menu) in `src/Talt.Client/Screens/InGameScreen.cs` and `src/Talt.Client/Screens/Screens.cs`
- [X] T032 [US5] Sandbox and inflation toggles in `src/Talt.Client/Screens/NewGameOptions.cs`; options `--sandbox`, `--inflation`, `--station-service-pays` in `src/Talt.Client/Program.cs` and `tools/Talt.Headless/Program.cs`; `finance` settings in `src/Talt.Core/Commands/RecordedSession.cs`
- [X] T033 [P] [US5] Tests: streak, reset, warning month, game over month, sandbox never ends, commands refused, session settings round trip in `tests/Talt.Core.Tests/Finance/BankruptcyTests.cs`

## Phase 8: User Story 6 - Inflation (P3)

- [X] T034 [US6] Inflation levels (R7; data-model.md FinanceSystem.CostLevel/PaymentLevel/InflationMonths) and cost level in `ConstructionState`; apply once in `ConstructionCommand.Check` (plan flag) in `src/Talt.Core/Construction/ConstructionCommand.cs`, `ConstructionPlan.cs`, `src/Talt.Core/Finance/FinanceSystem.cs`; update the client call in `src/Talt.Client/Construction/ToolController.cs`
- [X] T035 [P] [US6] Tests: off = unchanged, monthly growth, stop after years, construction/maintenance/loan/payment scaled in `tests/Talt.Core.Tests/Finance/InflationTests.cs`

## Phase 8b: User Story 7 - Optional infinite money (P3, added after planning)

- [X] T042 [US7] `Company.HasInfiniteMoney`/`InfiniteMoneySinceTick` (data-model.md Company.HasInfiniteMoney/InfiniteMoneySinceTick), ledger-only bookings, hashing in `src/Talt.Core/Construction/Company.cs`; `FinanceSettings.InfiniteMoney` (data-model.md FinanceSettings) applied at creation in `src/Talt.Core/Finance/FinanceSystem.cs`
- [X] T043 [US7] `EnableInfiniteMoneyCommand` in `src/Talt.Core/Finance/InfiniteMoneyCommand.cs` (registered in `CommandSerializer`); loans refused, money check skipped in `ConstructionCommand.Check`, bankruptcy never counts; reasons in `ConstructionReasons.cs`; session settings in `RecordedSession.cs`
- [X] T044 [US7] Client: new-game option, `--infinite-money` (client and headless), finance window button with a confirmation overlay, HUD "∞", window line, strings en/es in `src/Talt.Client/Screens/*.cs` and `Localization/Strings.*.json`
- [X] T045 [P] [US7] Tests: settings start, command turns on once and never off, construction with no cash, cash frozen and ledger counts, loans refused, no bankruptcy, replay determinism in `tests/Talt.Core.Tests/Finance/InfiniteMoneyTests.cs`; client confirmation flow in `tests/Talt.Client.Tests/Screens/FinanceClientTests.cs`

## Phase 9: Polish

- [X] T036 Headless finance summary line in `tools/Talt.Headless/Program.cs`
- [X] T037 Goldens: run all determinism tests; update changed map goldens on purpose (`tests/Talt.Core.Tests/Determinism/*ReplayTests.cs`), confirm `ci/golden-fingerprints.json` and `ci/sessions/windows-reference.json` unchanged; add a finance replay golden and a parallel = sequential test in `tests/Talt.Core.Tests/Determinism/FinanceReplayTests.cs`
- [X] T038 [P] Allocation tests (daily/monthly/payment, `AllocationAssert`) in `tests/Talt.Core.Tests/Finance/FinanceAllocationTests.cs`
- [X] T039 [P] `FinanceBenchmarks` (daily, monthly with 5 000 stations on 4 096, payment) in `benchmarks/Talt.Benchmarks/FinanceBenchmarks.cs`
- [X] T040 Docs: Finance section, data flow, benchmark numbers in `docs/ARCHITECTURE.md`; options in `README.md`; decisions in `docs/VISION.md`
- [ ] T041 Run quickstart.md; launch the client for the developer's hand test

## Dependencies

Setup → Foundational → US1, US2, US3 (independent; all touch `FinanceSystem.cs`, so in sequence) → US4
(needs ledger + value) → US5 → US6 → Polish. Client tasks T025, T028 can run beside Core tasks.

## Parallel examples

- Foundational: T004, T007, T008, T009 together after T003.
- US2: T014 and T018 (tests) beside T015.
- Polish: T038, T039 together.

## Implementation strategy

MVP = Phases 1–5 (loans, payments, costs, all tested in Core). Then the window (US4) for the hand test,
then bankruptcy, inflation and polish.

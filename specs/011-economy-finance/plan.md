# Implementation Plan: Economy and finance

**Branch**: `011-economy-finance` (worktree branch `claude/011-economy-543ffa`) | **Date**: 2026-11-25 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/011-economy-finance/spec.md`

## Summary

Add a deterministic finance system to `Talt.Core` (namespace `Talt.Core.Finance`), owned by
`GameSimulation` in games with a map. `Company` (feature 006) becomes a full account: cash (the old
`Balance`), loan, a `Ledger` with totals by category for the last 24 months and every year, company
value, bankruptcy streak and game-over flag. Every money change goes through one booking method with a
category, so ledger totals always equal the change in cash (loan movements apart).

`FinanceSystem` listens to the stations' deliveries (`IDeliveryListener`, feature 010) and pays them
with an integer formula from data (rate × amount × distance × time factor × payment level, one
128-bit intermediate, rounded down once). Each day it charges vehicle running costs registered through a
`RunningCosts` table (feature 012 fills it); each month end it charges property maintenance (owned road
tiles by type, counted incrementally by `ConstructionState`; station pieces and warehouses from the
registry), loan interest, updates company value, inflation levels and the bankruptcy streak, then
rotates the ledger month; each year end it rotates the ledger year. Accruals keep their remainders, so
yearly totals are exact. Loans are a recorded command (`changeLoan`). Inflation (off by default) scales
construction costs (in `ConstructionCommand.Check`), maintenance, running costs and the loan limits by a
cost level, and payments by a payment level. Sandbox games never go bankrupt. A debug option
(`--station-service-pays`) pays the cargo collected by `--station-service`. Balancing is in a new
validated `Finance/Data/finance.json`; starting cash stays in `construction.json`.

An optional infinite-money mode (new-game option or a recorded `enableInfiniteMoney` command after a confirmation
in the client; never turned off) freezes the cash, never refuses construction for money, refuses loans and never
goes bankrupt, while the ledger keeps listing costs and income (research R17).

The client gets a finance window (table by category for three years, 24-month bar chart, cash, loan,
limits, value, borrow/repay buttons) opened from the HUD money, red negative cash, floating money texts
on the map, a bankruptcy warning message and a game-over screen. New-game options and command-line
options select sandbox and inflation. The headless tool prints a finance summary. Benchmarks cover the
daily and monthly finance updates and the payment of a delivery.

## Technical Context

**Language/Version**: C# (latest) on .NET 10

**Primary Dependencies**: none new (System.Text.Json `JsonDocument`, `System.Int128` for exact
intermediates).

**Storage**: embedded `Talt.Core.Finance.Data.finance.json`; recorded sessions gain an optional
`finance` settings object; one new command type (`changeLoan`).

**Testing**: xUnit `tests/Talt.Core.Tests/Finance` (rules validation, payment formula, ledger sums and
rotation, loan rules, interest/maintenance/running-cost accruals, property counts, company value,
bankruptcy and sandbox, inflation, debug pays, commands round trip, fingerprints, parallel = sequential,
allocation via `AllocationAssert`); client tests for the window texts, chart data, floating texts,
options and localization; BenchmarkDotNet `FinanceBenchmarks`.

**Target Platform**: Windows, Linux x64/arm64, macOS x64/arm64; identical results on all.

**Project Type**: Desktop game (Core library + client + headless tool + benchmarks).

**Performance Goals**: daily finance update < 0.1 ms, monthly < 1 ms with 5 000 stations, payment of a
delivery < 1 µs, all 0 B in steady state (SC-003); 60 FPS on the 4 096 map with the window open
(SC-004); tick cost unchanged (finance runs on deliveries and day/month boundaries only).

**Constraints**: integer-only state (no `float`/`double`/`decimal` in Core finance); map-less
fingerprints unchanged; finance hashes nothing until the first booking, loan or registration (as
stations do), so map games without player activity keep their goldens; `Talt.Core` graphics-free.

**Scale/Scope**: one company now (model per company); 13 cargo types; up to ~16 M road tiles counted
incrementally; 24 monthly + N yearly ledger periods.

## Constitution Check

*Initial gate and post-design re-check: PASS. Justified choices in Complexity Tracking.*

| Principle | Status | Notes |
|-----------|--------|-------|
| I Core/Client separation | PASS | all money rules in `Talt.Core.Finance`; client only formats, draws and submits commands |
| II Performance | PASS | O(1) daily work, O(stations) monthly, incremental road counts; `FinanceBenchmarks` recorded in ARCHITECTURE.md |
| III Deterministic, data-oriented | PASS | 64-bit integers, Int128 intermediates, remainders carried, fixed iteration order; loan is a command; finance types registered in `NoFloatingPointStateTests` |
| IV Tested increments | PASS | tests per user story; ledger = cash property test over years |
| V Readable C# | PASS | small files (`FinanceRules`, `Ledger`, `PaymentCalculator`, `FinanceSystem`, `RunningCosts`, `LoanCommand`) with XML docs |
| VI Original content | PASS | own formula and values; mechanics only studied |
| VII Data-driven | PASS | loan, interest, payments, maintenance, property value, bankruptcy, inflation, debug distance in `finance.json`, validated against cargo, road and station ids |
| VIII Living docs | PASS | ARCHITECTURE.md (Finance section, data flow, numbers), README options, VISION decisions |

## Project Structure

### Documentation (this feature)

```text
specs/011-economy-finance/
├── spec.md, plan.md, research.md, data-model.md, quickstart.md
├── contracts/ (finance-api.md, commands.md, data-files.md, cli.md)
├── checklists/requirements.md
└── tasks.md
```

### Source Code (repository root)

```text
src/Talt.Core/
├── Finance/
│   ├── Data/finance.json
│   ├── FinanceRules.cs           # parsed + validated finance.json (+ fingerprint)
│   ├── FinanceSettings.cs        # Sandbox, Inflation, DebugServicePays (scenario settings)
│   ├── LedgerCategory.cs         # categories + index helpers
│   ├── Ledger.cs                 # 24 monthly + yearly period totals, rotation, hashing
│   ├── PaymentCalculator.cs      # pure payment formula
│   ├── RunningCosts.cs           # yearly cost per registered vehicle, daily charge with carry
│   ├── FinanceSystem.cs          # IDeliveryListener, daily/monthly/yearly, inflation, value, bankruptcy, events
│   ├── MoneyEvent.cs             # client feed (ring buffer; not state)
│   └── LoanCommand.cs            # changeLoan command
├── Construction/ (Company: account + Book; ConstructionState: owned road counts, cost level;
│                  ConstructionCommand.Check applies the cost level; reasons)
├── Stations/ (CargoDelivery: DeliveryConsumer.DebugService; registry: station centres incl. removed,
│              counts by owner, debug pickups reported)
├── Commands/CommandSerializer.cs, RecordedSession.cs ("finance")
├── Scenarios/ScenarioSettings.cs (Finance)
└── Simulation/GameSimulation.cs (create, events, game-over command refusal, hashing)

src/Talt.Client/
├── Screens/FinanceWindow.cs, GameOverScreen (in Screens.cs), InGameScreen.cs (HUD money button, red, warning)
├── Ui/Widgets/BarChart.cs
├── Screens/MoneyTexts.cs        # floating texts (projected like TownLabels)
├── Screens/NewGameOptions.cs (sandbox, inflation), Program.cs (options), GameSession.cs (drain events)
├── Input/Data/actions.json (finance window key), Localization/Strings.*.json
tools/Talt.Headless/ (options, finance summary)
tests/Talt.Core.Tests/Finance/*, tests/Talt.Client.Tests/*Finance*
benchmarks/Talt.Benchmarks/FinanceBenchmarks.cs
docs/ARCHITECTURE.md, README.md, docs/VISION.md
```

**Structure Decision**: one new Core folder `Finance/` beside `Stations/`; `Company` stays in
`Construction` (it is referenced everywhere) and gains the account; small hooks in construction and
stations. The client adds one window, one widget and one overlay, following `StationWindow` and
`TownLabels`.

## Complexity Tracking

| Choice | Why Needed | Simpler Alternative Rejected Because |
|--------|------------|-------------------------------------|
| Incremental owned-road counts in `ConstructionState` (R5) | monthly maintenance must not scan 16 M tiles (< 1 ms budget) | a monthly full scan costs ~5–10 ms on the 4 096 map |
| Remainder carries in accruals (R4) | yearly totals must be exact (SC-002) | rounding each month loses up to 11 units a year per item |
| Finance hashes nothing until first activity (R11) | keep map goldens of untouched games, like stations did | always hashing changes every map golden for no rule change |
| Removed stations keep their centre (R3) | FR-008: distance for cargo whose origin was removed | storing coordinates in every packet and hold widens 010's hot structures |

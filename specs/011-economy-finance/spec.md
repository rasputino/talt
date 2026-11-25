# Feature Specification: Economy and finance

**Feature Branch**: `011-economy-finance`

**Created**: 2026-11-25

**Status**: Draft

**Input**: User description: "The player's company earns money by delivering cargo and spends it on
construction, vehicles and running costs. Finances must be clear and the economy balanced so good
networks are profitable and bad ones are not. P1: I start with an initial amount of money and a loan
facility; I can borrow and repay in steps up to a maximum loan, paying interest monthly. P1: each
delivery pays according to cargo type, amount, distance between pickup and drop-off, and travel time; I
see the income appear on the map at the vehicle. P1: vehicles and infrastructure have running and
maintenance costs charged over time. P2: a finance window with income and expenses by category for the
current and previous years, cash, loan and company value. P2: if I stay bankrupt (negative money with
maximum loan) for a grace period, the game ends (except in sandbox mode). P3: optional inflation
increases prices and payments over the years. Success: all money calculations are exact and
deterministic (no floating-point drift across platforms); balancing values live in data files. Out of
scope: share trading, competitors."

## Context

Feature 006 gave the player's company a single balance that construction actions spend (terraform,
clearing, demolition), with a starting amount from a data file; features 007 (roads) and 010 (stations
and warehouses) added more construction actions with costs. Feature 005 shows the balance in the HUD
in a currency the player chooses. Feature 009 defined cargo types with a value that decays with days in
transit, and feature 010 reports every accepted delivery (cargo, amount, origin and destination
station, pickup and delivery day, consumer) through a listener that nobody listens to yet.

So far money only goes down. This feature turns the balance into a full company account: income from
deliveries, recurring costs for what the company owns, a loan with interest, a ledger by category and
period, company value, bankruptcy and optional inflation. Vehicles that actually deliver cargo arrive in
feature 012; this feature defines how they will be paid and charged and proves the rules without real
vehicles.

## Clarifications

### Session 2026-11-25

- Q: What money and loan does the company start with? → A: 500 000 of its own money and no loan; it may
  borrow up to 500 000 in steps of 10 000 at 5 % a year, paid monthly (all data values).
- Q: How do bankruptcy and sandbox work? → A: 3 consecutive bankrupt month ends (negative cash with the
  maximum loan) → warning; 6 → game over. Sandbox is a new-game option that only disables bankruptcy;
  money still counts and construction still needs enough cash.
- Q: Is inflation on by default, and how fast? → A: Off by default (new-game option). When on: costs +2 % a
  year, payments +1.5 % a year, compounded monthly, for 100 years (data values).
- Q: How can income be seen in a game before vehicles exist (feature 012)? → A: A debug option
  `--station-service-pays` (client and headless, kept in the scenario settings so sessions replay it): the
  cargo collected by `--station-service` is paid as a delivery over a data-defined distance (40 tiles) with
  a floating text at the station.
- Q: What history does the finance window show? → A: The table by category for the current year and the
  two previous years, and below it a simple bar chart of income and expenses for the last 24 months.
- Q (added after planning, user request relayed 2026-11-25): Can the player play without money limits? → A: Yes,
  an optional infinite-money mode, turned on when starting a new game or at any time during play (after a
  confirmation); once on it can never be turned off for that game.

## Scope and deferrals

- **In scope now**: company account (cash, loan, ledger); loans in steps with monthly interest; delivery
  payments from data-defined rates (cargo, amount, distance, days in transit); infrastructure
  maintenance (roads, stations, warehouses owned by the company); a running-cost interface for vehicles
  and its charging rules; a ledger by category for months and years; company value; bankruptcy warning
  and game over with a sandbox exception; optional inflation of costs and payments; the finance window,
  the loan controls, the HUD money and floating income/expense texts on the map; a debug way to produce
  paid deliveries before vehicles exist.
- **Deferred to feature 012**: buying vehicles (the "vehicle purchase" category exists, filled by 012),
  real vehicle running costs (012 registers its vehicles with the running-cost interface), floating
  income texts above a moving vehicle (until then they appear at the unloading station).
- **Out of scope**: share trading, competitors and company takeovers, saving and loading games (no save
  system yet).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Start money and the loan facility (Priority: P1)

A new game starts with the company's starting money and a loan facility. From the finance window the
player borrows more, in fixed steps, up to a maximum loan, or repays in the same steps while there is
money to do so. At the end of every month the company pays interest on the loan it holds.

**Why this priority**: Money is the constraint of every decision; borrowing is how a player builds the
first network before income arrives.

**Independent Test**: Start a game, borrow and repay through the player's commands and check cash and
loan change by exactly one step each time, that the limits are refused with a reason, and that interest
charged at each month end equals the data-defined formula on the loan held.

**Acceptance Scenarios**:

1. **Given** a new game, **When** it starts, **Then** the company has the data-defined starting cash
   (500 000) and no loan, and the HUD shows the cash.
2. **Given** a loan below the maximum, **When** the player borrows one step, **Then** cash and loan both
   rise by exactly one step.
3. **Given** a loan at the maximum, **When** the player tries to borrow, **Then** the action is refused
   with the reason "maximum loan reached" and nothing changes.
4. **Given** a loan and enough cash, **When** the player repays one step (or "repay all"), **Then** cash
   and loan fall by exactly that amount; with less cash than the step, or no loan, it is refused with a
   reason.
5. **Given** a loan, **When** a month ends, **Then** the month's interest is taken from cash and booked
   as "loan interest" in the ledger, with the same result on every platform.

---

### User Story 2 - Deliveries pay (Priority: P1)

Every accepted delivery pays the company that delivered it. The payment depends on the cargo type, the
amount, the distance between the station where the cargo was first picked up and the station where it
was delivered, and the days it spent in transit: slow deliveries pay less, down to a minimum share. The
amount appears as a floating text on the map where the delivery happened.

**Why this priority**: Income is what makes a good network profitable; without it the economy has no
purpose.

**Independent Test**: Without vehicles, load cargo at one station into a test cargo hold and unload it at
an accepting station some distance away a number of days later, and check the company's cash rises by
exactly what the formula gives, that the income is booked under the cargo's income category, and that a
floating text event with the amount and the place is produced.

**Acceptance Scenarios**:

1. **Given** coal picked up 40 tiles from a power station's station, **When** it is delivered quickly,
   **Then** the company receives the coal rate × amount × distance, booked as coal income.
2. **Given** the same delivery taking more days than the cargo's free transit time, **When** it is
   delivered, **Then** it pays less, by the cargo's daily decay, and never less than the cargo's minimum
   share.
3. **Given** two deliveries that differ only in amount or distance, **When** both are paid, **Then** the
   payments are in proportion within one money unit of rounding.
4. **Given** cargo unloaded at a station in the same station group it came from, **When** unloaded,
   **Then** nothing is delivered and nothing is paid (feature 010 rule).
5. **Given** a delivery, **When** it is paid, **Then** a floating text with the formatted amount appears
   above the unloading station (the vehicle from feature 012 on) and rises and fades.
6. **Given** the origin station was removed while the cargo travelled, **When** it is delivered,
   **Then** the distance is measured from where that station stood.

---

### User Story 3 - Running and maintenance costs (Priority: P1)

What the company owns costs money over time: each road tile, station piece and warehouse it owns has a
data-defined yearly maintenance cost charged at the end of each month; vehicles (feature 012) have a
yearly running cost charged a little every day while they exist. Costs can take the company's cash below
zero.

**Why this priority**: Costs are what make a bad network lose money and stop the player from building
everything everywhere.

**Independent Test**: Build roads, stations and a warehouse, advance a month and check the maintenance
booked equals the counts times the data values; register test vehicles with the running-cost interface,
advance days and check the running costs booked per day and per year match the data.

**Acceptance Scenarios**:

1. **Given** the company owns roads, stations and warehouses, **When** a month ends, **Then** one twelfth
   of their yearly maintenance (by type) is charged and booked as "property maintenance".
2. **Given** a town's roads or another company's property, **When** a month ends, **Then** the company
   pays nothing for them.
3. **Given** registered vehicles with a yearly running cost, **When** a day ends, **Then** the day's share
   is charged and booked as "running costs", and over a full year the total equals the yearly cost within
   one money unit per vehicle.
4. **Given** low cash, **When** costs are charged, **Then** cash may become negative; construction then
   is refused for lack of money but costs keep being charged.

---

### User Story 4 - Finance window (Priority: P2)

The player opens a finance window from the HUD. It lists income and expenses by category for the current
year and the two previous years (when they exist), with totals, a bar chart of income and expenses for
the last 24 months, and shows cash, loan, maximum loan, interest rate and company value, with buttons to
borrow and repay.

**Why this priority**: Clear finances let the player see which parts of the network pay; the game works
without the window but the player plays blind.

**Independent Test**: Run a scripted game with known construction, deliveries, interest and maintenance,
open the window and check every figure equals the ledger, and that years roll over on 1 January.

**Acceptance Scenarios**:

1. **Given** a game with activity, **When** the player opens the finance window, **Then** each category
   row shows the year's income (positive) or expense (negative) and the column totals equal the sum of the
   rows and the change in cash for that year (loan movements apart).
2. **Given** the window is open, **When** money changes, **Then** the figures update without reopening.
3. **Given** 1 January, **When** the year ends, **Then** the current year's column becomes the previous
   year's and a new empty column starts.
4. **Given** the borrow and repay buttons, **When** used, **Then** they send the loan commands and show the
   refusal reason when refused.
5. **Given** a month end, **When** company value is recalculated, **Then** the window shows cash minus loan
   plus the data-defined value of the company's property.

---

### User Story 5 - Bankruptcy (Priority: P2)

When a month ends with negative cash and the loan at its maximum, the company is bankrupt for that
month. After a number of consecutive bankrupt months the player is warned; if it continues to the grace
limit the game ends with a game-over screen. In sandbox mode the company can stay in debt forever.

**Why this priority**: Losing gives decisions weight; it is not needed for a network to make money.

**Independent Test**: Force cash negative with the maximum loan and advance month ends, checking the
counter, the warning month, the game-over month, that one month with positive cash or a loan below the
maximum resets it, and that sandbox games never end.

**Acceptance Scenarios**:

1. **Given** negative cash and the maximum loan at a month end, **When** it happens for the warning number
   of consecutive months, **Then** the player sees a bankruptcy warning.
2. **Given** a bankruptcy streak, **When** a month ends with cash at zero or more (or a loan below the
   maximum), **Then** the streak resets.
3. **Given** the streak reaches the grace limit outside sandbox mode, **When** that month ends, **Then**
   the simulation marks the game over, refuses every player command, and the
   client shows a game-over screen with the final figures and a way back to the main menu.
4. **Given** a sandbox game, **When** the company stays bankrupt any number of months, **Then** the game
   continues (the window still shows the debt).

---

### User Story 6 - Optional inflation (Priority: P3)

When inflation is enabled for a game, prices rise each year: construction and maintenance costs, running
costs, maximum loan and delivery payments grow by data-defined yearly rates (payments a little slower than
costs), for a data-defined number of years.

**Why this priority**: Adds long-game pressure; the economy is complete without it.

**Independent Test**: Run games with and without inflation for several years and check the multipliers
applied to a fixed construction action, to maintenance, to the maximum loan and to a fixed delivery match
the data rates compounded per month, and that games without inflation never change prices.

**Acceptance Scenarios**:

1. **Given** inflation off, **When** years pass, **Then** every price and payment stays as in the data.
2. **Given** inflation on, **When** a month ends, **Then** the cost and payment multipliers grow by the
   monthly share of their yearly rates, and new prices use them.
3. **Given** inflation on, **When** the data-defined number of inflation years has passed, **Then** the
   multipliers stop growing.

---

### User Story 7 - Optional infinite money (Priority: P3)

The player can start a game with infinite money (new-game option) or turn it on at any time from the finance
window, after confirming that it cannot be undone. From then on money never limits the player: construction is
never refused for lack of money, the cash shown in the HUD is replaced by an infinite-money mark, costs and income
are still computed and listed in the finance window but no longer change the cash, loans cannot be taken or
repaid, and the company can never go bankrupt. The mode cannot be turned off for that game, and the game
remembers it used the mode (later goals and scores may exclude it).

**Why this priority**: A relaxed way to build; the economy is complete without it.

**Independent Test**: Turn the mode on through its recorded command, check that construction succeeds with no
cash, that the cash stays as it was while the ledger keeps counting, that loan commands and a second enable are
refused, that bankruptcy never counts, and that a recorded session replays to the same fingerprint.

**Acceptance Scenarios**:

1. **Given** the new-game option, **When** the game starts, **Then** it is in infinite-money mode from day one.
2. **Given** a normal game, **When** the player chooses "infinite money" in the finance window, **Then** a
   confirmation explains it cannot be undone, and only after confirming the mode turns on.
3. **Given** infinite money, **When** the player builds anything, **Then** it is never refused for lack of money
   and the cash does not change; the cost is listed under construction in the finance window.
4. **Given** infinite money, **When** the player tries to turn it on again, borrow or repay, **Then** the action is
   refused with a reason; nothing can turn the mode off.
5. **Given** infinite money, **When** month ends pass with any figures, **Then** the company is never bankrupt.

---

### Edge Cases

- A delivery of 0 units, or one whose payment rounds to 0, pays nothing and makes no floating text.
- Distance 0 (cargo delivered at the station where it was picked up but to another group cannot happen;
  two stations side by side): pays the cargo's minimum distance from data.
- Very large games: cash, loan and ledger totals never overflow (amounts are checked and saturate at the
  documented limits instead of wrapping).
- Repaying more than the loan held repays only the loan; borrowing past the maximum is refused.
- Undoing a construction action refunds its cost and removes it from the construction category of the
  period it was booked in (or books a negative construction expense if the period has rolled over).
- The station that a delivery was made to is removed later in the same day: the payment is already made.
- Bankruptcy streak when the maximum loan grows with inflation: the loan is no longer at the maximum, so
  the streak resets unless the player borrows.
- Commands submitted after game over are refused with "the game is over".
- Map-less scenarios (tests, benchmarks without a map) have no company account: their fingerprints do
  not change.
- Infinite money turned on while bankrupt: the streak resets at the next month end and never counts again; a
  loan held stays (its interest is listed but changes nothing).

## Requirements *(mandatory)*

### Functional Requirements

**Account and loan**

- **FR-001**: The player's company MUST have cash and a loan, both whole amounts of the internal money
  unit, starting from data-defined starting cash and starting loan.
- **FR-002**: The player MUST be able to borrow and repay in data-defined steps through recorded commands:
  borrow one step, repay one step, borrow to the maximum, repay all affordable steps.
- **FR-003**: Borrowing past the maximum loan, repaying with no loan, and repaying more than the cash
  available MUST be refused with a reason that names the problem.
- **FR-004**: At each month end the company MUST pay interest equal to the loan held × the data-defined
  yearly rate / 12, rounded by one documented rule.

**Payments**

- **FR-005**: Every delivery reported by the stations MUST pay the delivering company: cargo rate ×
  amount × distance factor × time factor, computed in whole numbers with one documented rounding step.
- **FR-006**: Distance MUST be the tile distance between the origin station and the destination station
  (as data-defined: Manhattan distance between their centres), with a data-defined minimum.
- **FR-007**: The time factor MUST be full value up to the cargo's data-defined free transit days, then
  fall by the cargo's daily decay per mille, never below its minimum value percent.
- **FR-008**: The distance of a delivery whose origin station no longer exists MUST be measured from the
  last position of that station.
- **FR-009**: Each payment MUST produce a client-visible event with the company, amount and map position,
  used for floating texts; events are not simulation state.

**Costs**

- **FR-010**: Each road tile, station piece and warehouse owned by the company MUST cost a data-defined
  yearly maintenance (roads by road type), charged as one twelfth at each month end.
- **FR-011**: The finance model MUST offer vehicles (feature 012) a way to register a yearly running cost
  per vehicle, charged daily as yearly cost × days elapsed / days in the year with no cumulative drift.
- **FR-012**: Every existing construction spend and undo refund MUST be booked in the construction
  category; vehicle purchases (feature 012) MUST have their own category.
- **FR-013**: Recurring costs MAY take cash below zero; construction keeps requiring enough cash.

**Ledger, value and history**

- **FR-014**: Every change of cash MUST be booked in exactly one category: construction, vehicle
  purchase, running costs, property maintenance, loan interest, income per cargo type, other.
- **FR-015**: The ledger MUST keep the totals by category for each of the last 24 months and for every
  year of the game, so the window can show the current and two previous years.
- **FR-016**: Company value MUST be computed at each month end as cash − loan + the data-defined value of
  owned property (and of vehicles from feature 012).

**Bankruptcy and sandbox**

- **FR-017**: A month end with negative cash and the loan at its maximum MUST count as a bankrupt month;
  any other month end MUST reset the count.
- **FR-018**: After a data-defined number of consecutive bankrupt months (3) the player MUST be warned; at
  a data-defined grace limit (6) the game MUST end, unless the game is a sandbox game. Sandbox only
  disables bankruptcy: money and the cash check of construction work as usual.
- **FR-019**: Sandbox MUST be a new-game option (and a client and headless command-line option) kept in
  the scenario settings so recorded sessions replay it.
- **FR-020**: After game over the simulation MUST refuse player commands and the client MUST show a
  game-over screen with the final cash, loan and company value and a way back to the main menu.

**Inflation**

- **FR-021**: Inflation MUST be a new-game option, off by default, kept in the scenario settings; its
  data-defined rates are costs +2 % and payments +1.5 % a year, for 100 years.
- **FR-022**: With inflation on, a cost multiplier and a payment multiplier MUST grow at each month end by
  the data-defined yearly rates compounded monthly, in fixed point, for a data-defined number of years;
  costs (construction, maintenance, running costs, maximum loan and loan step) use the cost multiplier
  and payments use the payment multiplier.

**Data, determinism and interface**

- **FR-023**: All balancing values (starting money, loan, interest, rates, maintenance, property values,
  bankruptcy months, inflation) MUST live in validated data files with errors that name the field.
- **FR-024**: All finance state MUST be whole numbers advanced only by the simulation's ticks, daily and
  monthly events and commands; it MUST be part of the fingerprint in games with a map, and games without
  a map MUST keep their existing fingerprints.
- **FR-025**: The HUD MUST show cash in the player's currency, colour negative cash, and open the finance
  window; the finance window MUST use the existing UI framework and currency formatting, and draw a bar
  chart of monthly income and expenses for the last 24 months.
- **FR-026**: Player-facing text MUST be localized in English and Spanish.
- **FR-027**: A debug option `--station-service-pays` (client and headless, kept in scenario settings) MUST
  let station service (feature 010) pay its collected cargo as deliveries over a data-defined distance
  (40 tiles), with floating texts at the station, so income can be seen before vehicles exist.
- **FR-028**: The headless tool MUST print the company's finances at the end of a run.

**Infinite money**

- **FR-029**: Infinite money MUST be a new-game option (and client and headless command-line option) kept in the
  scenario settings, and a recorded command that turns it on during play; the client MUST ask for confirmation
  before sending the command.
- **FR-030**: Once on, the mode MUST never turn off for that game; a second enable is refused.
- **FR-031**: In infinite-money mode construction MUST never be refused for lack of money, cash MUST NOT change
  (costs and income are still booked in the ledger), loan commands MUST be refused, and bankruptcy MUST never
  count.
- **FR-032**: The mode MUST be part of the game state and fingerprint, shown in the HUD and finance window, and
  exposed to later features (whether and since when the game used it).

### Key Entities

- **Company account**: cash, loan, bankruptcy streak, game-over flag, company value; belongs to a
  company.
- **Ledger**: totals by category for the last 24 months and each year; categories as in FR-014 with one
  income entry per cargo type.
- **Finance rules**: the data file's balancing values (loan, interest, maintenance, property values,
  bankruptcy, inflation, debug delivery distance), plus each cargo's payment rate and free transit days.
- **Payment**: the result of pricing one delivery (company, cargo, amount, distance, days, money,
  position); produced from feature 010's delivery report.
- **Running-cost registration**: a yearly cost per registered vehicle, charged daily (feature 012 adds
  vehicles to it).
- **Inflation state**: cost and payment multipliers and the months of inflation applied.
- **Infinite money**: per company, on/off (never back) and the tick it was turned on.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: The same scenario and commands give the same cash, loan, ledger and fingerprint on Windows,
  Linux and macOS (x64 and arm64) at every tick (CI fingerprint gate).
- **SC-002**: Every money figure is exact: ledger totals equal the change in cash to the unit over any
  number of game years, in tests.
- **SC-003**: The daily finance update and the monthly update (maintenance, interest, value, inflation)
  of the reference 4096 × 4096 game with 5 000 stations each take under 1 ms and allocate nothing.
- **SC-004**: The 4096 × 4096 map keeps 60 FPS with the finance window open and floating texts showing.
- **SC-005**: A player can borrow, repay and read a year's income and expenses in under 30 seconds from
  the HUD.
- **SC-006**: Changing any balancing value only requires editing a data file; an invalid value stops the
  game at load with a message naming the file and field.

## Assumptions

- Money stays in the internal unit of features 005 and 006 (1 = one US dollar, whole units); display
  currencies convert it with fixed rates. Amounts are 64-bit.
- Starting cash stays where feature 006 put it (500 000, construction data file); the finance data file
  holds the loan (starting loan 0).
- Distances between stations are measured in tiles; a "unit" of cargo is the cargo's own unit.
- There is one company (the player's); the model is written per company so competitors could be added.
- Interest, maintenance and inflation are monthly; running costs daily; property value monthly.
- The game-over check runs only for the player's company.
- The existing synthetic traffic vehicles (debug traffic) belong to no company and cost nothing.

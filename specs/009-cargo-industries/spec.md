# Feature Specification: Cargo Types and Industries

**Feature Branch**: `009-cargo-industries`

**Created**: 2026-11-23

**Status**: Draft

**Input**: User description: "Cargo types and industries. Industries give the map its freight economy.
The player does not manage production; industries produce and accept cargo on their own, and the
player profits by transporting it. P1: see industries on the map (coal mine, forest, farm, oil well,
power station, sawmill, steel mill, refinery, factory) with distinctive voxel models. P1: an industry
window showing what it produces per month, what it accepts, and how much was transported last month.
P1: raw materials and most processed goods can be delivered to any industry or town that accepts
them; the player does not choose a specific destination. P2: processing industries turn accepted cargo
into other cargo automatically. P2: production changes over time; well-served industries grow,
neglected ones shrink or close, and new industries occasionally open. P3: cargo types have properties
(category, how quickly their value drops with travel time, which vehicles can carry them). Success:
1 000 industries update daily in under 1 ms total; all cargo and industry definitions come from data
files validated at startup. Out of scope: passenger/mail/goods destinations (013)."

## Context

Feature 004 reserves industry sites on every generated map, and feature 010 will let stations collect
and load cargo. This feature fills the gap between them: it defines the world's cargo types and the
industries that produce and consume them, so that a generated map already contains a working freight
economy before the player builds anything.

The player never manages production. Industries are part of the living world: they make cargo, accept
cargo, and change over time. The player's role is to connect them. Until stations and cargo handling
exist (feature 010), produced cargo accumulates at its industry and can be inspected, and the
industry's transport figures stay at zero.

## Clarifications

### Session 2026-11-23

- Q: Which cargo types exist in version 1? A: passengers, mail, coal, wood, iron ore, grain, livestock,
  oil, steel, lumber, fuel, food and goods. Passengers, mail and goods/food for towns are
  destination-based and are not produced by industry sites in this feature.
- Q: How is "well served" measured before stations exist? A: An industry's recent deliveries are the
  measure. Until feature 010 delivers cargo, a neglected industry enters a slow decline after about
  24 months without deliveries and may close after about five years; new industries can still open on
  free sites, so the world changes during feature 009.
- Q: How are cargo amounts measured in production, deliveries and stock? A: Every cargo type declares
  its own unit in the data file (tonnes for freight, counts for passengers and mail). All amounts are
  stored and simulated as whole integers in that unit; no fractional cargo exists.
- Q: What happens to cargo that an industry accepts but has no recipe for? A: It is consumed: the
  delivery is recorded and the cargo disappears, as a power station burns coal.
- Q: How many reserved industry sites start occupied? A: About 75 % of the suitable sites at game
  start (roughly 750 of 1 000 on a 4096 × 4096 map); the remaining sites stay free so new industries
  can open over the years.
- Q: Does waiting cargo have a limit? A: Yes. Each industry kind defines a per-cargo storage cap in
  the data file (for example several months of production). Production beyond the cap is discarded,
  and processing waits, keeping its input, when the output store is full.

## Scope and deferrals

- **In scope now**: cargo and industry definitions as validated data; the freight economy state on a
  generated map (industry instances on the sites from feature 004); production, acceptance,
  automatic processing and monthly production changes; industries opening, shrinking and closing;
  the industry window; showing industries on the map; tests, benchmark and documentation.
- **Deferred to feature 010**: stations, catchment areas, loading and unloading, delivery of cargo to
  industries by vehicles, and the transport figures that only become non-zero once vehicles carry
  cargo.
- **Deferred to feature 011**: money, cargo value and payment; this feature only defines how a cargo
  type's value decays with travel time.
- **Deferred to feature 013**: destination flows for passengers, mail and town goods.
- **Deferred to feature 018**: final industry art; this feature uses simple, distinctive procedural
  models.
- Feature 006 owns construction. Industry footprints are published as occupied tiles so construction
  can refuse to build over them; this feature does not add construction tools or HUD work.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - See industries on the map (Priority: P1)

A player starts a game and finds industries across the map: coal mines in hills, forests in wooded
areas, farms on plains, oil wells near coasts, and power stations, sawmills, steel mills, refineries
and factories in suitable places. Each kind has a recognisable model, so the map reads as a working
economy.

**Why this priority**: Without visible producers and consumers there is no freight economy to play
with; every later cargo feature depends on these places existing.

**Independent Test**: Generate maps with several seeds and confirm that industries of the expected
kinds appear on the reserved industry sites, that their models differ by kind, and that no two
industries overlap.

**Acceptance Scenarios**:

1. **Given** a newly generated map, **When** the player looks at it, **Then** industries stand on the
   reserved industry sites, each with the model of its kind.
2. **Given** a map with hills, forests, plains and coast, **When** industries are placed, **Then** the
   kinds suit their surroundings: mines and forests on hills and woods, farms on plains, oil wells
   near water.
3. **Given** the same map settings and seed, **When** the map is generated again, **Then** the same
   industries appear in the same places with the same kinds.

---

### User Story 2 - Inspect an industry (Priority: P1)

A player selects an industry and reads what it produces per month, what it accepts, and how much cargo
was moved to and from it last month.

**Why this priority**: The window is how the player decides which industries are worth connecting.

**Independent Test**: Open the window for several kinds of industry and check that the shown
production, acceptance and monthly figures match the data and the simulated month.

**Acceptance Scenarios**:

1. **Given** a coal mine, **When** the player opens its window, **Then** it shows the monthly coal
   production, no accepted cargo and the amount transported last month.
2. **Given** a processing industry such as a steel mill, **When** the player opens its window, **Then**
   it shows both the cargo it accepts and the cargo it produces.
3. **Given** a month has passed, **When** the player reopens the window, **Then** the "last month"
   figures describe the month that just ended.
4. **Given** the industry produced cargo but nothing has transported it, **When** the player reads the
   window, **Then** the available (waiting) amount is shown separately from the transported amount.

---

### User Story 3 - Deliver raw materials anywhere they are accepted (Priority: P1)

A player who delivers a raw material to any industry that accepts it is successful, without choosing a
specific destination. The same applies to most processed goods.

**Why this priority**: Destination-free delivery is the core loop of the freight economy and keeps
early gameplay simple.

**Independent Test**: Deliver the same cargo to each industry that accepts it and confirm acceptance;
deliver it to an industry that does not accept it and confirm refusal.

**Acceptance Scenarios**:

1. **Given** a cargo that some industries accept, **When** it is delivered to any of them, **Then** the
   delivery is accepted and recorded.
2. **Given** a cargo that an industry does not accept, **When** it is delivered there, **Then** the
   delivery is refused and the industry's state does not change.
3. **Given** a destination-free cargo, **When** a player plans a delivery, **Then** no specific
   destination has to be chosen.

---

### User Story 4 - Processing industries turn cargo into other cargo (Priority: P2)

Delivering raw materials to a sawmill, steel mill, refinery or factory immediately makes the resulting
processed goods available there, ready to be transported onward.

**Why this priority**: Processing chains are what make the network interesting, but a working
producer/consumer economy (stories 1–3) is already valuable without them.

**Independent Test**: Deliver a recipe's input to a processing industry and confirm the output amount
appears at the industry immediately, in the quantity the recipe defines.

**Acceptance Scenarios**:

1. **Given** a sawmill that accepts wood and produces lumber, **When** wood is delivered, **Then** the
   matching amount of lumber becomes available there at once.
2. **Given** an industry with no matching recipe for a delivered cargo, **When** the cargo is accepted,
   **Then** it is recorded as accepted and consumed without creating a different cargo.
3. **Given** an industry has produced output, **When** a month passes with no transport, **Then** the
   output remains available (it does not vanish).

---

### User Story 5 - Industries change over time (Priority: P2)

Production rates drift, well-served industries grow, neglected ones shrink or may close, and new
industries occasionally open on unused sites.

**Why this priority**: A changing world keeps long games alive, but the economy works without it.

**Independent Test**: Run many simulated months with and without deliveries and observe production
changes, closures and openings; repeat with the same seed and obtain the same history.

**Acceptance Scenarios**:

1. **Given** an industry that has received regular deliveries, **When** months pass, **Then** its
   production rate tends to increase.
2. **Given** an industry with no deliveries, **When** many months pass, **Then** its production rate may
   drift and the industry may eventually close.
3. **Given** unused industry sites, **When** months pass, **Then** new industries occasionally open on
   them, never on occupied sites.
4. **Given** the same seed and commands, **When** the game is replayed, **Then** every industry's
   history is identical.

---

### User Story 6 - Cargo types have properties (Priority: P3)

A player can read what kind of cargo each type is, how quickly its value falls with travel time, and
which kinds of vehicle can carry it.

**Why this priority**: These properties drive later pricing and vehicle features; they are data the
player benefits from seeing but do not enable play by themselves.

**Independent Test**: Read the cargo information and compare it with the data file values.

**Acceptance Scenarios**:

1. **Given** any cargo type, **When** the player reads its information, **Then** its category, unit,
   value decay with travel time, destination requirement and carrying vehicle kinds are shown.

---

### Edge Cases

- An industry site cannot fit the footprint of its chosen kind: another suitable kind is chosen, or
  the site is left unused; industries never spill onto water or unbuildable ground.
- A map has very few or no industry sites: the game still starts, with an empty or small industry
  list, and no errors.
- Industries open every month until all suitable sites are used; the world then stops growing.
- A closed industry leaves its tiles free for a later industry but never reopens with its old state.
- A delivery that does not fill a whole processing batch leaves the remainder as stored input at the
  industry and processes it with the next delivery; no cargo is created out of nothing.
- A cargo whose production rate becomes zero still exists and can be delivered.
- An industry's storage cap for a cargo is reached: further production is discarded and existing cargo
  stays; a full output store postpones processing instead of destroying its input.
- Two industries must not occupy the same tiles or overlap their reserved free squares.
- A delivery amount larger than a recipe's capacity is fully consumed up to the amounts available;
  remainders are handled without creating cargo out of nothing.
- An invalid or inconsistent data file (unknown cargo in a recipe, negative rate, duplicated id,
  footprint too large) stops the game at startup with a message naming the file and the problem.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The game MUST ship cargo types as data, each with an identifier, a localizable name, a
  category, a unit, a value-decay profile over travel time, whether it needs a destination, a colour
  and the vehicle kinds that can carry it.
- **FR-002**: The game MUST ship industry kinds as data, each with an identifier, a localizable name,
  footprint, model, monthly production table, accepted cargo, processing recipes, placement rules,
  era range and probability weight.
- **FR-003**: All cargo and industry data MUST be validated when loaded, and any error MUST stop
  startup with a message naming the file and the invalid value.
- **FR-004**: Every generated map MUST place industry instances on about three quarters of the
  industry sites reserved by map generation, choosing a kind that fits the site and its probability,
  without overlapping other industries or towns.
- **FR-005**: The placement of industries MUST be reproducible from the same map settings and seed.
- **FR-006**: Industries MUST produce their output on a monthly schedule, and the production state
  MUST advance through the simulation's scheduled day/month events.
- **FR-007**: Production MUST vary modestly from month to month through the simulation's seeded
  randomness, without ever becoming negative.
- **FR-008**: Industries MUST accept only the cargo listed for their kind, and MUST record accepted
  and transported amounts per cargo and month.
- **FR-009**: A processing industry MUST convert accepted cargo into its recipe's output immediately,
  in the quantity the recipe defines, limited by the amount delivered. Cargo accepted without a
  matching recipe MUST be consumed and recorded, without producing another cargo.
- **FR-010**: Produced and processed cargo MUST remain available at the industry up to its per-cargo
  storage cap; it MUST NOT disappear when a month ends. Production beyond the cap MUST be discarded,
  and processing MUST wait, keeping its input, while the output store is full.
- **FR-011**: The system MUST track each industry's recent deliveries and use them to make well-served
  industries grow. An industry with no deliveries for about 24 months MUST enter a slow decline, and
  one neglected for about five years may close.
- **FR-012**: New industries MUST be able to open on unused industry sites over time.
- **FR-013**: Industries MUST have an era range; kinds outside the current year MUST NOT open.
- **FR-014**: The industry state MUST be part of the simulation's complete state, so that replaying
  the same seed and commands yields the same industries, amounts and history.
- **FR-015**: Selecting an industry on the map MUST open an information window with its name, kind,
  monthly production, accepted cargo, processing recipe, available cargo and last month's transport
  figures.
- **FR-016**: Cargo names, industry names, categories, units and window labels MUST be localizable,
  with English and Spanish text.
- **FR-017**: Industry tiles MUST be published as occupied so that construction (feature 006) can
  refuse to build over them.
- **FR-018**: The simulation MUST expose an operation that delivers a quantity of a cargo to an
  industry, refusing cargo the kind does not accept, for use by later cargo-handling features.
- **FR-019**: A player MUST be able to identify every industry in the world through an enumeration of
  industries with their position, kind and current state, for rendering, picking and tools.
- **FR-020**: The headless runner and command-line tools MUST be able to run a game with industries
  generated from map settings, so their behaviour can be checked without a GPU.

### Key Entities *(include if data involved)*

- **Cargo type**: What can be moved. Identifier, name, category (for example raw material, processed
  good, passenger, mail), unit, value-decay profile by travel time, destination requirement, colour
  and allowed vehicle kinds. Amounts of a cargo are whole numbers in its unit.
- **Industry kind**: A template for a place in the economy. Identifier, name, footprint, model,
  monthly production per cargo, accepted cargo, processing recipes (input cargo and amount → output
  cargo and amount), placement rules (terrain it suits), era range, probability weight and per-cargo
  storage caps.
- **Industry instance**: One place on the map. Kind, position, occupied tiles, production rates,
  available cargo amounts, stored input for pending recipes, accepted and transported totals for the
  current and last month, recent deliveries, open/closed state.
- **Site**: A reserved location from map generation (feature 004) where an industry may stand.
- **Delivery**: A quantity of one cargo offered to one industry, accepted or refused, with its effect
  on available cargo and processing.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 1 000 industries update their daily production in under 1 ms in total on the reference
  machine.
- **SC-002**: 1 000 industries complete a monthly update (production change, statistics, possible
  opening or closing) in under 5 ms in total on the reference machine.
- **SC-003**: Every cargo and industry value shown in the game comes from a data file; changing a
  data file changes the game without recompiling.
- **SC-004**: Starting a game with a deliberately broken data file stops it before the world appears,
  naming the file and the problem.
- **SC-005**: On a 4096 × 4096 map, industries appear on the reserved sites and a player can find and
  inspect one within 30 seconds of the world appearing.
- **SC-006**: Replaying the same seed and commands reproduces all industry state exactly, on every
  supported platform.

## Assumptions

- The 1 000-industry reference count comes from the project's reference scenario (4096 × 4096 map,
  500 towns, 1 000 industries, 50 000 vehicles).
- Industry production is expressed per month, because the player plans in months; daily accumulation
  exists only to keep the simulation responsive and smooth.
- Industry sites are already spaced and guaranteed buildable by feature 004; this feature does not
  change how sites are chosen. On a 4096 × 4096 map, feature 004's density yields about 1 000 industry
  sites, which matches the reference scenario.
- The first version has no competitors and no player-owned industries; all industries belong to the
  world.
- Transport figures are zero until feature 010 lets vehicles carry cargo; the window still shows them
  so the player learns what to expect.
- Placeholder industry models are acceptable now; feature 018 replaces them with final art.
- Passengers, mail and town goods are listed as cargo types for completeness but are not produced or
  consumed by industry sites in this feature.

# Feature Specification: Towns

**Feature Branch**: `008-towns`

**Created**: 2026-11-24

**Status**: Draft

**Input**: User description: "Towns are the source and destination of passengers and mail and consume
goods. They should feel alive: they grow, build roads and buildings, and react to the service the
player provides. P1: towns with names, populations and voxel buildings of different sizes and types
(houses, apartments, shops, offices) placed along their roads. P1: towns grow over time by adding
buildings and roads; growth speeds up with good transport service and stalls without it. P2: a town
window with population, buildings, passengers and mail generated and transported last month, and the
town's rating of the player's company. P2: town ratings fall when the player demolishes town property
or trees and rise with good service; low ratings block some actions (for example building stations in
town). P3: larger towns get bigger buildings and a denser centre. Success: 500 towns update their
monthly growth in under 50 ms total; town names are generated from original name lists (English and
Spanish style). Out of scope: destination flows (013), station catchment details (010)."

## Context

Feature 004 reserves town sites on every generated map, and feature 007 lays a small street grid on
each of them, owned by that site's town. Feature 006 gave every tile object an owner (company, town,
industry or nobody) and made town-owned objects impossible for the player to demolish. Feature 009
defined passengers, mail, goods and food as cargo types whose sources and destinations are towns.

This feature turns those empty street grids into towns: named places with buildings, people and a
memory of how the player has treated them. Towns produce passengers and mail every month and accept
goods and food, but nothing carries them yet: stations (010) and vehicles (012) make the transport
figures non-zero. Towns grow only when served, so until feature 010 growth is shown with a debug
option that simulates service.

## Clarifications

### Session 2026-11-24

- Q: How do towns grow without transport service (before stations exist, feature 010)? → A: They do
  not grow at all; only towns whose passengers and mail are transported grow, faster with a higher
  share transported.
- Q: How is the style of a map's town names chosen? → A: A new-game option: English, Spanish or Mixed
  (default Mixed). It is part of the game's settings, so the same settings and seed give the same names.
- Q: Can the player demolish town buildings with the clear tool? → A: Yes, paying a cost per building
  (by size), with a large rating drop, and only while the company's rating in that town is above
  "poor"; the same rating rule applies to removing town roads.
- Q: What size are towns at game start? → A: Varied: many villages (about 100–500 residents), some
  medium towns (1 000–3 000) and a few large ones (5 000 and more), with a new-game option Small /
  Normal / Large that scales the whole distribution.
- Q: How is growth shown in this feature when no town has service yet? → A: A debug option of the
  client and the headless tool (for example `--town-service 60`) simulates that percentage of every
  town's passengers and mail being transported, through the same entry point feature 010 will use. It
  is part of the scenario settings, so recorded sessions replay it.

## Scope and deferrals

- **In scope now**: founding a town on every reserved town site at game start; original generated
  names; building definitions as validated data; placing buildings along town roads; monthly growth
  (new buildings, replaced buildings, new roads) driven by size and service; monthly passenger and
  mail generation and goods/food acceptance figures; per-company town ratings and the actions they
  allow or block; demolishing town buildings by the player under rating rules; town buildings drawn on
  the map; town name labels; the town window; tests, benchmark and documentation.
- **Deferred to feature 010**: stations, catchment areas, picking up passengers and mail, delivering
  goods and food. The "building stations in town" rule is defined here (a rating threshold the station
  tool will check) but enforced by 010.
- **Deferred to feature 011**: money from transported passengers and mail; town-related company
  finances (funding buildings, advertising).
- **Deferred to feature 013**: where passengers and mail want to go.
- **Deferred to feature 018**: final building art; this feature uses simple procedural voxel models
  (boxes with roofs and windows) that differ by building type and size.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - See named towns with buildings along their roads (Priority: P1)

A player starts a game on a generated map and finds towns spread across it. Each town has a name and a
population, shown in a label above it, and its streets are lined with buildings of different types and
sizes: small houses, apartment blocks, shops and offices.

**Why this priority**: Towns are the main source of passengers and mail and the destination of goods;
without visible towns there is nothing to connect.

**Independent Test**: Generate maps with several seeds and check that every town site holds a town
with a name, a population equal to the sum of its buildings' residents, and buildings that each touch
one of the town's roads, never overlap roads, industries, water or each other, and differ visibly by
type and size.

**Acceptance Scenarios**:

1. **Given** a newly generated map, **When** the player looks at a town site, **Then** a named town
   stands there with buildings along its streets and a label showing its name and population.
2. **Given** a town, **When** its buildings are listed, **Then** each one stands next to one of the
   town's roads on land it may use (no water, road, industry or other building, slope within limits).
3. **Given** the same map settings and seed, **When** the game is created again, **Then** the same
   towns have the same names, populations and buildings in the same places.
4. **Given** a map, **When** names are listed, **Then** no two towns share a name and every name comes
   from the game's own original name lists.

---

### User Story 2 - Towns grow over time (Priority: P1)

While the player builds and the months pass, towns add buildings on free plots along their streets,
replace small buildings with larger ones and extend their streets outward. A town whose passengers and
mail are carried grows noticeably faster than one that is ignored.

**Why this priority**: Growth makes the world feel alive and rewards the player's service, which is
the core loop of the game.

**Independent Test**: Run a game headless for several in-game years and check that total population,
building count and town road length increase, that every new building and road satisfies the placement
rules, and that the same seed produces the same towns at every month whether ticks run in parallel or
sequentially. Compare a town with simulated good service against the same town without.

**Acceptance Scenarios**:

1. **Given** a town without any transport service, **When** several years pass, **Then** it does not
   grow (its buildings may still be renewed with the same size).
2. **Given** two otherwise equal towns, **When** one has a high share of its passengers and mail
   transported, **Then** it grows faster than the other.
3. **Given** a town with no free plots left along its streets, **When** it grows, **Then** it extends
   a street (following its own street pattern) before placing more buildings.
4. **Given** a town whose growth would need a tile the player owns or an industry occupies, **When** it
   grows, **Then** it goes around it and never changes or removes anything that is not its own.

---

### User Story 3 - Inspect a town (Priority: P2)

The player clicks a town building or a town's label and a window opens with the town's name,
population, number of buildings, passengers and mail generated and transported last month, the goods
and food it accepts, and its rating of the player's company.

**Why this priority**: The window is how the player judges which towns are worth serving and how they
are being treated.

**Independent Test**: Open the window on several towns and check that every figure matches the town's
simulated state and the month that just ended.

**Acceptance Scenarios**:

1. **Given** a town, **When** the player clicks one of its buildings or its label, **Then** the town
   window opens with its figures.
2. **Given** a month has ended, **When** the window is open, **Then** the "last month" figures update
   to the month that just ended.
3. **Given** the player has not served the town, **When** the window is read, **Then** transported
   figures are zero and generated figures are not.
4. **Given** the window is open, **When** the player changes the interface language, **Then** labels
   change language; the town's name does not.

---

### User Story 4 - Town ratings react to the player (Priority: P2)

Each town remembers how each company treats it. Demolishing town buildings, removing town roads and
cutting trees in and around the town lower its rating of the company; good service raises it slowly
over time. A town with a poor rating refuses some actions of that company.

**Why this priority**: Ratings make the player's actions in towns matter and are the hook for later
station and finance features.

**Independent Test**: Demolish a town building, clear trees near a town and remove a town road, and
check that the rating falls by the amounts in the data; let months pass with and without service and
check the rating moves as the rules say; lower the rating below the threshold and check that the
blocked actions are refused with a clear reason.

**Acceptance Scenarios**:

1. **Given** a town with a neutral rating, **When** the player clears trees inside the town's zone,
   **Then** the rating falls by the per-tree amount, up to a per-action limit.
2. **Given** a town building, **When** the player demolishes it with the clear tool, **Then** the
   building is removed, the cost is paid and the rating falls by the demolition amount.
3. **Given** a rating below the threshold for demolition, **When** the player tries to demolish a town
   building or remove a town road, **Then** the action is refused with the reason "the town refuses".
4. **Given** months pass with no further damage, **When** the rating is read, **Then** it has moved
   back toward neutral, faster when the town is served.
5. **Given** a rating below the station threshold, **When** a later feature asks whether the company
   may build a station in that town, **Then** the answer is no.

---

### User Story 5 - Larger towns get bigger buildings and a denser centre (Priority: P3)

As a town grows, its centre fills with taller apartment blocks, shops and offices, while the outskirts
keep small houses. Buildings of later eras appear only once their era has started.

**Why this priority**: Visual variety and a recognisable town shape make large maps readable, but the
town works without it.

**Independent Test**: Grow a town to a large population and check that buildings near the centre are,
on average, taller and larger than those at the edge, and that no building appears outside its era or
below its minimum town population.

**Acceptance Scenarios**:

1. **Given** a large town, **When** the player looks at it, **Then** its centre has larger and taller
   buildings than its outskirts.
2. **Given** a building type whose era has not started, **When** towns grow, **Then** it is never
   placed.
3. **Given** a small building in a large town's centre, **When** the town grows, **Then** it may be
   replaced by a larger building on the same or a merged plot.

---

### Edge Cases

- A town site whose street grid could not be laid (steep ground, water, an industry in the way):
  the town is founded with whatever roads exist; with no road at all it still exists, with its name
  and a centre, and grows its first street from the centre once it is served.
- Two towns growing toward each other: a town never takes tiles owned by another town; streets may
  meet and join, each tile keeping its owner.
- A town's street meets a player road: the town may connect to it but never changes it.
- The player demolishes a whole town: the town keeps its name and centre with zero population and
  can grow again from its remaining roads.
- More towns than names in the lists: names combine parts so the space of names is far larger than
  any map's town count; if a name repeats, it is regenerated deterministically.
- Map-less scenarios (replays and benchmarks from feature 001): no towns, and their fingerprints are
  unchanged.
- The player terraforms under a town building: refused until the building is demolished first (feature
  006 rule), and the demolition affects the rating.
- A building would sit across a slope: only plots whose ground differs by at most one step are used,
  and the building stands on a levelled foundation at the highest corner.
- Very large towns near the map edge: growth stays inside the map.

## Requirements *(mandatory)*

### Functional Requirements

**Founding and names**

- **FR-001**: Every reserved town site of a generated map MUST hold a town at game start, centred on
  the site, owning the street grid feature 007 lays there.
- **FR-002**: Each town MUST have an initial size chosen deterministically from the seed and the
  site, so a map has many villages (about 100–500 residents), some medium towns (1 000–3 000) and a
  few large ones (5 000 and more), distribution from data; a new-game option Small / Normal / Large
  (default Normal) scales the whole distribution.
- **FR-003**: Each town MUST get a name generated from the game's own original name parts (English
  style and Spanish style); names MUST be unique on a map and the same for the same settings and seed.
  A new-game option chooses the style: English, Spanish or Mixed (default Mixed, each town picks a
  style from the seed).
- **FR-004**: Town names are proper names: they MUST NOT change with the interface language; all
  other town texts MUST be localized (English and Spanish).

**Buildings**

- **FR-005**: Building types MUST be defined in a data file validated at load: id, category (house,
  apartment, shop, office and others), footprint, height, residents, monthly passenger and mail
  production, goods and food acceptance, era range, minimum town population, preferred distance from
  the centre, placement weight and model description. Invalid files MUST be rejected with a clear
  message naming the entry and field.
- **FR-006**: A building MUST occupy only tiles that are land, not water, have no road, industry,
  player object or other building, lie inside the map, and whose heights differ by at most one step;
  at least one footprint tile MUST be next to a road of its town.
- **FR-007**: Building tiles MUST be owned by their town and MUST block terraforming and road building
  by companies like other owned objects (feature 006).
- **FR-008**: A town's population MUST be the sum of its buildings' residents.

**Growth**

- **FR-009**: Towns MUST grow only during the simulation's monthly update, deterministically: the same
  seed and commands give the same towns, and parallel ticks give the same result as sequential ones.
- **FR-010**: Each month a town MUST decide how much to grow from its size and its service level
  (share of last month's passengers and mail that were transported); a town without service MUST NOT
  grow, and growth MUST speed up with the share transported (rates from data).
- **FR-010a**: The client and the headless tool MUST offer a debug option that simulates a given
  percentage of every town's passengers and mail as transported, through the same entry point feature
  010 will use; the option MUST be part of the scenario settings so recorded sessions replay it.
- **FR-011**: Growth MUST place new buildings on free plots along the town's roads, prefer larger and
  taller types nearer the centre as the town gets bigger, and MAY replace an existing building of the
  town with a larger one.
- **FR-012**: When a town has too few free plots, it MUST extend its streets outward by building road
  pieces it owns, following its street pattern (grid or organic, chosen per town); new streets MUST
  follow the feature 007 road rules (slopes, bridges are not built by towns) and update the road
  network incrementally.
- **FR-013**: Growth MUST never remove, retype or take over objects that are not the town's own (it
  MAY add a connection from its new street to a neighbouring road of another owner, as a player road
  may), MUST NOT terraform and MUST NOT cut trees outside the plots and street tiles it builds on.

**Statistics**

- **FR-014**: Every month each town MUST record passengers and mail generated (from its buildings'
  production) and transported (zero until feature 010 reports pickups), keeping the last month's
  figures for display.
- **FR-015**: Each town MUST report the goods and food it accepts (from its buildings' acceptance) for
  feature 010.

**Ratings**

- **FR-016**: Each town MUST keep a rating for every company, starting neutral, within a fixed range
  with named bands (for example "hostile", "poor", "neutral", "good", "excellent").
- **FR-017**: Demolishing a town building, removing a town road and removing trees within the town's
  zone MUST lower the company's rating by amounts from data, applied with the action.
- **FR-018**: Each month a company's rating MUST move toward a target set by its service in the town
  (neutral without service) at rates from data.
- **FR-019**: The player MUST be able to demolish town buildings with the clear tool at a cost per
  building from data (by size), and remove town roads under the feature 007 conditions, only while the
  company's rating in that town is above the "poor" band; otherwise the action MUST be refused with a
  localized reason ("the town refuses").
- **FR-020**: The game MUST expose whether a company may build a station in a town (rating at or above
  the station threshold) for feature 010.

**Presentation**

- **FR-021**: Town buildings MUST be drawn as procedural voxel models whose shape and colour differ by
  category and size, with roofs and windows, without breaking the 60 FPS budget on the largest map.
- **FR-022**: Each town MUST show a label with its name and population above its centre, visible from
  far away and readable at every zoom.
- **FR-023**: Clicking a town building or label MUST open the town window (US3); the window MUST update
  when the month ends (towns are never removed, so the window stays valid).
- **FR-024**: Construction previews MUST show town buildings as blockers with the owner and, for the
  clear tool, the cost and the rating change.

**Performance and determinism**

- **FR-025**: Town state MUST be fixed-point integers, part of the state fingerprint when the game has
  a map, and change only in the simulation tick.
- **FR-026**: The monthly update of 500 towns on the largest map MUST take under 50 ms in total and
  MUST NOT allocate in steady state.

### Key Entities

- **Town**: id, name, centre tile, street pattern, population, buildings, owned roads, growth counters,
  monthly statistics (passengers and mail generated and transported, this and last month), and a rating
  per company.
- **Building type**: a data definition (category, footprint, height, residents, production,
  acceptance, era range, minimum town population, preferred distance from the centre, weight, model).
- **Town building**: a placed building: type, origin tile, town, construction year.
- **Town rating**: a company's standing in a town, a bounded integer with named bands and thresholds
  that allow or block actions.
- **Town name parts**: original syllable and word lists per style, from a data file.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: On every tested seed, every town site holds a named town with at least one building and
  every building satisfies the placement rules.
- **SC-002**: The monthly update of 500 towns completes in under 50 ms in total on the reference
  machine.
- **SC-003**: Over ten in-game years without service, total town population does not grow; with the
  debug service option at 60 %, it grows by at least 5 % per year, and at 100 % at least 1.5 times as
  fast as at 60 %.
- **SC-004**: The largest map with all its towns drawn keeps 60 frames per second on the reference
  machine.
- **SC-005**: All town names on a map are unique and come from the game's own name lists, in English
  and Spanish styles.
- **SC-006**: Replaying a recorded session gives the same towns, buildings, populations and ratings as
  the original game, on every CI platform.
- **SC-007**: In a hand test, the developer sees towns with labels, opens a town window, demolishes a
  town building and sees the rating fall, and after several in-game years sees towns larger than at the
  start.

## Assumptions

- Towns do not terraform and do not build bridges or tunnels in this feature.
- The player cannot found or delete towns in this feature (a scenario editor is a later feature).
- One company exists (the player); ratings are stored per company so later companies (AI, multiplayer)
  fit without changes.
- Before feature 010 the service level is zero for every town, so towns do not grow unless the debug
  service option is set; tests simulate service through the same entry point feature 010 will use.
- Town buildings never produce money in this feature.

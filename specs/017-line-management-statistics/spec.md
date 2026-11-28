# Feature Specification: Line management and statistics

**Feature branch** `017-line-management-statistics`
**Created** 2026-11-28
**Status** Clarified

## Clarifications

### Session 2026-11-28

- Q: How is line load factor measured? A: Weight occupancy by distance travelled.
- Q: Which departures share the interval on a mixed line? A: Apply it separately to road vehicles and trains.
- Q: How much company history can players inspect? A: Keep 24 months including the current month, plus one summary for every game year.
- Q: What does the traffic overlay measure? A: Show a usage heatmap of actual vehicle passages per segment during the last complete calendar month.
- Q: How should the line route overlay connect stops? A: Follow the roads and railways between ordered stops and identify disconnected legs.

## User scenarios and testing

### US1 — Compare lines (P1)

A player deciding where to add capacity opens the company line list. Given a bus line, a freight train line and an empty line, sorting by current profit orders all three and filtering by mode narrows the results. Opening a row shows the corresponding line, its ordered stops and its assigned vehicles. Reassigning a vehicle preserves the financial results already earned by its former line.

### US2 — Find a vehicle (P1)

A player investigating service interruptions opens the fleet list. Given a running bus, a loading train and a broken vehicle, filtering by breakdown isolates the broken vehicle. Selecting it locates it on the map. An unprofitable vehicle can also be running or too old. Each train occupies one row regardless of consist length.

### US3 — Space departures (P2)

A player sets a target departure interval in the line window. Given two ready vehicles of the same mode at the first stop, the following vehicle waits until the interval has elapsed. A road departure leaves the rail departure time unchanged, and vice versa. A vehicle that arrives late can leave when ready. Disabled control preserves ordinary dispatch. Full-load orders, railway signals and reservations still govern safe departures.

### US4 — Review trends (P2)

A player compares company income, expenses, delivered quantities by cargo and company value over time. After two month ends with different volumes, the station and town windows show two dated observations beside the existing gauges. Once thirteen months have closed, the station and town charts show the latest twelve.

### US5 — Inspect the network on the map (P3)

A player toggles line routes, station catchments and traffic density independently. Given corridors with one and twenty passages last month, the second appears in a higher usage band even when both are currently empty. Before the first complete month the overlay reports that history is unavailable. Toggling overlays preserves the ongoing simulation and camera position. If routes or used segments lie outside the viewport, the panel explains this and offers an explicit action to centre a connected route or the busiest segment. A complete month with no passages is distinguishable from missing history.

## Requirements

- FR-001. Show each company line, including empty lines, with assigned count, profit this year and last year, passenger and freight volumes, and load factor. Across all service movements in the reporting period, divide total occupied unit-distance by total offered capacity-distance. A full nine-tile leg followed by an empty one-tile leg at the same capacity gives 90%. A period with zero capacity-distance has no defined load factor. Support sorting on displayed metrics and filtering by mode and profitability.
- FR-002. Attribute operating results to the line served when earned or incurred. Preserve existing transfer credits per leg. Count completed deliveries once in company totals.
- FR-003. List road vehicles and trains with operating status, loss and age indicators, and map navigation. The fleet list belongs to 017. Individual vehicle inspection, catalog, model availability and replacement policy belong to 016 or their existing windows.
- FR-004. Expose optional first-stop interval keeping for road and rail services. Record changes as replayable player actions.
- FR-005. Show company income, expenses, final deliveries by cargo and company value for the latest 24 calendar months including the current month, and for every game year since founding. Sum flows within each period. Show company value at period end and the latest value for the open period. For every station and cargo, show waiting stock, pickups, final deliveries and rating. For each town, show passengers and mail generated and transported, and rating.
- FR-006. Draw line routes along traversable road and rail infrastructure for each leg including the return to the first stop. Mark disconnected legs as interrupted. Show station coverage using the simulation catchment. Expose the three map overlays in English and Spanish. Colour road and rail segments by vehicle passages in the last complete calendar month, counting one passage per vehicle or train traversal regardless of dwell time or train length. Label the month and the passage-count ranges in the legend. All management windows and charts must also work in both languages.
- FR-007. Define the 014 save contract for the dated 24-month company series, all annual company summaries, 12 completed months per station and town, dated segment passage counts for the current and last complete month, in-progress counters, annual line results, occupied unit-distance and offered capacity-distance, the target interval for each line and its last departure for each mode. A mid-period save must permit the same next departure and next period close as uninterrupted play.

## Key entities and edge cases

A line owns its operating results across changes of vehicles. A service vehicle is identified by mode and stable ID. A reporting period belongs to a company, line, station or town and has a calendar identity. Blank history before founding is distinguishable from a measured zero. Empty lines remain selectable. Missing capacity has no defined load factor. Selling a vehicle while its list is open must never navigate to a different vehicle.

## Success criteria

- SC-001. With 50,000 vehicles, open and scroll the fleet list and reach the last vehicle. Steady scrolling meets the reference frame budget of 16.6 ms.
- SC-002. An independently tallied transfer journey reconciles line activity and company final deliveries. Monthly and annual boundaries include each event exactly once.
- SC-003. Identical commands yield identical departures, histories and fingerprints under sequential and parallel simulation.
- SC-004. The reference simulation meets the 4 ms average and 8 ms worst-case tick budgets. Steady tick and frame paths allocate zero managed bytes. Record statistics collection costs and investigate any reference regression over 10%.

## Assumptions

Historical line measures describe the service operated during the reporting period. This feature supplies the persistence contract to feature 014.

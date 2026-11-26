# Research: Destination demand model

## R1 — Destination weights in integers

- **Decision**: weight(T) = pop(T)^a × 2^32 / max(1, d)^k with d the Manhattan distance between town centres in
  tiles, a ∈ {0, 1, 2}, k ∈ {0, 1, 2, 3} per cargo (data), computed in `long`/`UInt128`-safe order
  (pop ≤ 2^20, d^3 ≤ 2^39). Shares are in parts per million (`ShareScale = 1 000 000`): own-town share from data
  (500 000), the rest split over the top N weights by exact apportionment of the cumulative weight sums. Defaults:
  passengers a = 1, k = 1; mail a = 2, k = 2 (large towns receive more mail, distance matters as much); goods a = 1,
  k = 1. With a = 0, k = 1 the developer's example (10/8/5/2 tiles) gives 10.8/13.5/21.6/54.0 % of the other half.
- **Rationale**: integer and platform-independent; exponents limited to small integers keep it exact and readable.
- **Alternatives**: fixed-point fractional exponents (needs log/exp tables, little gameplay value); true gravity
  pop×pop (the source's population only scales the amount, which buildings' production already does).

## R2 — Table cost and cadence

- **Decision**: every town keeps its top N = 32 other towns within `rangeTiles` (default 1 500); goods industries keep
  their top 32 accepting towns. Tables are recomputed for `ceil(count / 30)` sources per day (all within a month), in
  town id order, on the main thread at the day boundary; each recompute scans all towns once (O(T) with a partial
  selection of the top N), so a month costs O(T²) spread over 30 days (1 000 towns: ~33 000 distance evaluations per
  day). Tables are built at game start once in full so the first day already has them.
- **Rationale**: towns change slowly; the cost is tiny and deterministic.
- **Alternatives**: full O(T²) matrix (memory and time grow quadratically); spatial grid search (not needed at this
  size; can be added later behind the same API).

## R3 — Link graph from lines, mode-agnostic

- **Decision**: `ILinkSource.Collect(LinkCollector)` reports, per line: company, cargo, the stop station ids in order
  with whether each stop allows loading/unloading, the monthly capacity of the vehicles assigned to it and their speed.
  The collector maps stations to group keys, skips stops that neither load nor unload, and adds directed edges from
  each loading stop to the next unloading stop (cyclic). Edge travel time (ticks) = Manhattan tiles between station
  centres × ticks per tile at the line's slowest vehicle speed + a dwell constant. Capacity per month = Σ vehicle
  capacity × ticks per month / round trip ticks. Parallel edges (several lines) merge: capacities add, time is the
  minimum. `RoadVehicleSystem` implements `ILinkSource`; feature 015 registers its rail system the same way.
  Graph staleness is a combined version (sources' versions + station registry version + vehicles count version).
- **Rationale**: lines are the shared abstraction of every mode (FR-010); estimates are deterministic and available
  before a vehicle completes a round.
- **Alternatives**: measured travel times (noisy, need history; can refine later); road-graph distances (mode-specific).

## R4 — Destinations are towns; serving groups

- **Decision**: a group serves town T for cargo c when the group accepts c (feature 010 acceptance) and one of its
  members' catchment covers a building of T that accepts c. Computed in the snapshot from `CoveredBuildings`. Deliveries
  of destination cargo go only to buildings of T.
- **Rationale**: matches the spec assumption; reuses 010 coverage.

## R5 — Assignment algorithm

- **Decision**: per company, cargo and destination town T (only towns in some origin's table and served in the
  company graph): multi-source Dijkstra on the reversed graph from T's serving groups, each node keeping its best two
  labels (cost, serving source) with distinct sources; cost = travel ticks + transfer penalty (data) +
  congestion term. The next hop of node g is the out-edge (g→h) minimising w + label(h) where the label's source ≠ g
  when g itself serves T (own-town cargo leaving its group; arrived cargo at a serving group is delivered and never
  looks up a route). Search is pruned at `maxRouteTicks` and at nodes farther than the destination's relevance radius
  (largest distance of a town listing T, plus `pruneMarginTiles`). Two iterations (MSA): iteration 1 with free-flow
  costs, assign the estimated monthly flows (R7) along the hops, then edge cost × (1 + penalty × max(0, load/capacity −
  1)) and iteration 2; final hops = the two iterations' hops with share 500/500 when they differ (1000 when equal).
  Edges with zero capacity cost `zeroCapacityPenalty` extra (used only if nothing else exists).
- **Rationale**: standard shortest-path assignment, capacity-aware with a deterministic two-step averaging; loop-free
  (shortest paths with positive weights).
- **Alternatives**: full equilibrium (many iterations, little gain), k-shortest paths (costlier).

## R6 — Determinism with a background job

- **Decision**: `CargoRouter` at tick t0 (every `updateIntervalTicks`, or earlier when the graph version changed and no
  job runs) builds the snapshot on the main thread into reused buffers, then either queues the pure computation on the
  thread pool (`ThreadPool.UnsafeQueueUserWorkItem` with a preallocated work item and a `ManualResetEventSlim`) when
  the tick is parallel, or runs it inline. At tick t0 + `applyDelayTicks` the main thread waits for completion and
  swaps the result in. The schedule (t0 and apply tick) is state and hashed; the result depends only on the snapshot.
- **Implementation note**: the main-thread build is spread over four consecutive ticks (collect; nodes and edges;
  served towns and flows; seal and start), the assignment searches destinations on several threads (same table for
  any thread count), and the apply delay is 2 days (data) so a dense network's job (~170 ms) finishes within the
  window at ×20. Split carries are not inherited between route sets (it would cost a search per entry on the main
  thread).
- **Rationale**: identical results for parallel/sequential ticks and any thread timing (FR-011, SC-006).
- **Alternatives**: `Task.Run` (allocates per job; acceptable but avoidable); main-thread slicing (rejected in plan).

## R7 — Flow estimate for congestion

- **Decision**: per origin group, monthly production of each destination cargo in its catchment per source town
  (from the station producer table, buildings' monthly passengers/mail; goods from industries' last-month production)
  × the source's destination shares → estimated flows used only to load edges in iteration 1.
- **Rationale**: needs no history; good enough to detect saturation.

## R8 — Route table storage

- **Decision**: per company and cargo, a table sorted by (group key, town) with hop1, hop2 (group keys or −1),
  share2 (per mille) and a split carry (int, state). Lookups by binary search. A route entry exists only for reachable
  pairs. Tables are hashed by `CargoRouter`.

## R9 — Destination stock storage

- **Decision**: buckets keyed by (group slot, cargo, next hop key) in a dictionary `long → bucket` (pre-sized, reused,
  no allocation after warm-up) with FIFO packet lists in a separate pool (amount, origin, source town, destination,
  pickup day, waiting-since day, credit). `_waiting[cell]` (010) counts directed and undirected cargo together (cap and
  ratings unchanged). Hop −1 = no route (waits for a re-route or expiry). Undirected cargo keeps the 010 lists.
- **Rationale**: O(1) loading by next stop; destination cargo never mixes with raw cargo lists.

## R10 — Supply split

- **Decision**: buildings' daily passengers and mail going to a group (010 `Distribute`) are first gathered per
  (group, source town, cargo, origin station) for the day, then split once over the source town's table with
  cumulative apportionment: a per-(source, cargo) counter C, units for share interval [S_i, S_{i+1}) =
  ⌊(C+A)S_{i+1}/M⌋ − ⌊(C+A)S_i/M⌋ − (⌊C S_{i+1}/M⌋ − ⌊C S_i/M⌋): sums exactly to A, proportions exact over time, no
  randomness. Units for destinations without a route from the group (or, for own-town, no route to another serving
  group) stay unserved; the industry case returns the unrouted part to the industry (TakeProduction returns less).
  Two-hop routes split the units with the route entry's carry.
- Groups that are in no company line (only reachable through the debug service `--station-service`) keep the 010
  behaviour (destination −1, any destination) so debug runs and earlier goldens are unchanged.

## R11 — Loading and unloading

- **Decision**: `Load(station, cargo, max, hold, speed, nextStationId)` takes undirected cargo as before; destination
  cargo from the bucket (group, cargo, groupKey(nextStation)), oldest first. `Unload(station, hold, mode,
  nextStationId)`: a destination part is delivered when the group serves its town and the group is not the origin's
  group; otherwise it stays aboard if the route's hop1 from this group is the next stop's group (and the mode is not
  Transfer), else it is unloaded into this group's bucket of its route's hop (credit added). Cargo picked up at a group
  and unloaded at the same group (no progress) gets no credit.
- **Rationale**: route chosen again at every station (FR-017a); mode-agnostic (FR-016).

## R12 — Transfer credit

- **Decision**: leg credit = max(0, Quote(origin → here, days since pickup) − credit already carried); added to the
  part's credit and to the vehicle's profit (no money). On delivery the vehicle's profit gets paid − carried credit.
  `ITransferValuer` (implemented by `FinanceSystem`, set on the registry like the delivery listener) provides Quote.
- **Rationale**: clarification Q5; feeder lines show their contribution.

## R13 — Expiry and conservation

- **Decision**: each day, every bucket's head packets older than `expiryDays` (by waiting-since day) are removed and
  counted lost (`TotalExpired`). Re-routes append packets in list order, so lists stay nearly ordered; expiry looks only
  at heads (a packet expires at the latest when it reaches the head). Conservation counters: generated (directed),
  unserved, delivered, lost (cap, neglect, expiry, removed), waiting, on board.

## R14 — Client

- **Decision**: station window: a "Destinations" section per destination cargo (top 8 destination towns with amount
  and next station name); vehicle window: cargo by destination town; town window: top destinations of passengers and
  mail with monthly demand and last month's served share, plus goods demand towards the town; overlay toggled from the
  HUD (key `O` data-driven binding "toggle-demand-overlay"): dotted screen-space lines between the projected centres of
  the 40 town pairs with the largest unserved demand, alpha and thickness by amount. All strings localized.

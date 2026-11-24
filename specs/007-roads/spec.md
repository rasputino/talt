# Feature Specification: Roads

**Feature Branch**: `007-roads`

**Created**: 2026-11-23

**Status**: Draft

**Input**: User description: "Roads are the first infrastructure players build: they connect towns and
industries and are used by buses and trucks. P1 build roads by dragging between two points; the game
proposes a path, shows cost, and builds straight segments, curves and junctions automatically. P1
roads follow gentle slopes; steep terrain requires terraforming or is rejected with a reason. P1 build
bridges over water, valleys and other roads, and tunnels through hills. P2 remove road pieces I own;
town roads can only be removed under conditions. P2 choose between road types that become available
over time (affecting speed limit and cost). P3 make a road one-way. Building or removing a road
updates the network used by vehicles within the same tick. A route search across a fully connected
4096 x 4096 road network finds a path in under 2 ms on average. Out of scope: road vehicles (012),
stations (010)."

## Context

Today a game on a generated map (feature 004) can be reshaped with the construction tools of feature
006 (raise, lower, level, clear, undo, with a ghost preview, a cost and the company's money), and has
cargo-producing industries on reserved sites (feature 009). The only roads are a technical prototype:
a synthetic square grid laid over the map regardless of water and hills, painted into the terrain
colour, owned by "town 0" and impossible to change. The prototype vehicles drive along it.

This feature makes roads real infrastructure: the player builds them with the same
choose-tool → preview → confirm → pay interaction as feature 006, they sit on the terrain as their own
pieces (flat, sloped, bridges, tunnels), they can be removed, and the network vehicles route over
changes the moment a road is built or removed. Towns (008), stations (010) and road vehicles (012)
all build on this network, so it must stay correct and fast on the largest maps.

## Clarifications

### Session 2026-11-23

- Q: What road geometry does version 1 have? → A: Grid plus 45° diagonals: a tile connects to any
  of its eight neighbours (four straight, four diagonal); curves are drawn smoothed.
- Q: On games with a map, what happens to the prototype road grid and its synthetic vehicles? → A:
  The grid is replaced by a small street layout on every town site, owned by that town; games with a
  map have no synthetic vehicles until road vehicles arrive (012). Map-less scenarios are unchanged.
- Q: Until towns exist (008), when can the player remove a town road? → A: When the town's roads stay
  connected to each other afterwards, at a higher removal cost; 008 adds the town rating.
- Q: What limits do bridges and tunnels have? → A: Maximum bridge length grows with the era (8 tiles
  from 1850, 16 from 1950, 32 from 1985); tunnels up to 64 tiles; both straight along a grid axis.
- Q: Which road types exist in version 1? → A: Dirt road (from 1850, 50 km/h, cheap), paved road (from
  1920, 80 km/h), fast road (from 1960, 100 km/h, expensive) and motorway (from 1980, no speed limit
  of its own (vehicles run at their maximum), very expensive, two tiles wide). Upgrading pays the cost
  difference; downgrading is refused.
- Q: How does a motorway's "double size" work? → A: Two parallel one-way carriageways, one tile each,
  built together. Motorways join other roads only at their ends or through special motorway
  connectors (exit and entry ramps with acceleration/deceleration lanes), never by level junctions in
  between. Motorways may be elevated: extra-long bridges that pass over roads (and rails, once they
  exist) at an even higher price.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Build a road by dragging (Priority: P1)

The player selects the road tool, presses on a tile and drags to another tile. While dragging, the
game proposes a path between the two tiles that follows the terrain, avoids obstacles and reuses
existing roads where that is cheaper, and shows it as a ghost preview with the total cost (green when
it can be built, red with a reason when it cannot). Releasing builds it: the money is spent, the road
appears on the terrain with straight and diagonal (45°) pieces, corners and junctions formed
automatically where roads meet, and the road can be undone like any other construction action.

**Why this priority**: building roads is the core of the feature; every other story extends it.

**Independent Test**: on a flat generated map, drag between two tiles 20 tiles apart in an L shape;
the preview shows a path and a cost; release; the road is drawn, the money drops by exactly the shown
cost, and a route search between the two ends succeeds in the same tick.

**Acceptance Scenarios**:

1. **Given** a flat, empty area and enough money, **When** the player drags from tile A to tile B,
   **Then** the preview shows a connected path from A to B and its cost, and releasing builds exactly
   that path and charges exactly that cost.
2. **Given** an existing road crossing the proposed path, **When** the road is built, **Then** a
   junction forms at the crossing and both roads are connected in the network.
3. **Given** a tree or a removable object of the player's own on the path, **When** the road is built,
   **Then** the tree is cleared and paid for as part of the same action; objects of others make the
   path go around them or, when no path exists, the preview is red with the reason.
4. **Given** not enough money, **When** the player drags, **Then** the preview is red with
   "not enough money" and releasing builds nothing.
5. **Given** the road was just built, **When** the player undoes within the undo window and nothing
   else changed those tiles, **Then** the road disappears, the network loses it and the cost is
   refunded.
6. **Given** the player presses the cancel key or the right button while dragging, **Then** nothing is
   built and the preview disappears.

---

### User Story 2 - Roads on slopes and steep terrain (Priority: P1)

Roads climb and descend gentle slopes (one height step per tile, the slope rule of feature 004) on
sloped road pieces. Where the terrain is too steep for a road the proposed path goes around; where
there is no way around, the preview is red with the reason ("too steep"), and the player can
terraform first (feature 006) and then build.

**Why this priority**: real maps are hilly; roads that ignore terrain or silently fail are not usable.

**Independent Test**: on a hilly map, drag across a hill whose sides are gentle: the road follows the
slope with sloped pieces; drag up a cliff with no gentle way around within the search limit: the
preview is red with "too steep".

**Acceptance Scenarios**:

1. **Given** a hill whose sides rise one step per tile, **When** a road is built over it, **Then**
   every piece lies on the terrain (flat or sloped) and the road is continuous.
2. **Given** a cliff (more than one step between neighbouring tiles), **When** the only path would
   cross it, **Then** the preview is red with the reason "too steep" and nothing can be built.
3. **Given** a road was built, **When** the player tries to terraform a tile under it, **Then** the
   terraform is refused with "object in the way" (feature 006 rule), so roads never float or sink.

---

### User Story 3 - Bridges and tunnels (Priority: P1)

With the bridge tool the player drags in a straight line from one bank or valley side to the other:
the bridge spans water, lower ground and existing roads between two ramp ends at the same height,
with a cost per tile of span. With the tunnel tool the player clicks a hillside: the tunnel runs
straight into the hill and exits at the first tile on the other side at the same height. Bridges and
tunnels join the road network like any road and can be removed again. The road tool also proposes a
bridge automatically when a short straight crossing of water is the only way to connect two points.

**Why this priority**: without bridges and tunnels, rivers, lakes and hills split the map into
unconnected parts.

**Independent Test**: on a generated map, build a bridge across a river and a tunnel through a hill;
a route search across each succeeds; both are drawn; removing them disconnects the network again.

**Acceptance Scenarios**:

1. **Given** a river with land on both sides at the same height and a span within the maximum bridge
   length, **When** the player builds a bridge across it, **Then** the bridge is drawn above the
   water, both ends connect to roads, and the river is unchanged.
2. **Given** a span longer than the maximum bridge length of the current year, **When** the player
   drags, **Then** the preview is red with "bridge too long".
3. **Given** a road below the bridge's path, **When** the bridge is built over it, **Then** both the
   bridge and the road below keep working and are not connected to each other.
4. **Given** a hillside facing the clicked direction, **When** the player builds a tunnel, **Then** the
   tunnel goes straight through to the first tile at the same height on the other side, within the
   maximum tunnel length, and the hill's surface is unchanged.
5. **Given** no exit within the maximum tunnel length, or the exit blocked by water, the map edge or
   an object, **When** the player aims the tunnel tool, **Then** the preview is red with the reason.

---

### User Story 4 - Remove roads (Priority: P2)

With the road removal tool the player drags along a line of road tiles. Pieces the player's company
owns are removed and paid for (removal cost); bridges and tunnels are removed whole when one of their
ends is chosen. Town roads are shown as not removable unless the removal conditions are met (see
Requirements); other owners' roads are skipped and shown in a separate colour.

**Why this priority**: players make mistakes and re-plan; but the network can be used without it.

**Independent Test**: build a road, remove part of it, check money and network; try to remove a town
road and see it refused with the reason.

**Acceptance Scenarios**:

1. **Given** a road the player built, **When** the player removes three of its tiles, **Then** the
   pieces disappear, the neighbouring pieces lose their connections towards them, the removal cost is
   charged and route searches through the gap fail in the same tick.
2. **Given** a town road, **When** the player tries to remove it without meeting the conditions,
   **Then** that tile is shown as skipped with the reason and is not removed.
3. **Given** a junction where a player road meets a town road, **When** the player removes the player
   road, **Then** the town road stays, with the connection towards the removed piece removed.

---

### User Story 5 - Road types over time (Priority: P2)

The road tool offers the road types available in the current game year, each with its own speed limit,
building cost and look: dirt road (from 1850), paved road (1920), fast road (1960) and motorway (1980).
New types become available as years pass. Building a better type over an existing road of the player's
own upgrades it, paying the cost difference; worse types are refused.

The motorway is special: the motorway tool builds two parallel one-way carriageways (one tile each) in
a straight or 45° line; consecutive runs continue each carriageway. Motorways connect to other roads
only at their ends or through motorway connectors: an exit ramp leaves a carriageway on its outer side
and an entry ramp joins it, each drawn with a short acceleration or deceleration lane. Across valleys,
water, roads and (later) rails the motorway can be elevated: two parallel bridges up to a much longer
maximum span, at a higher price.

**Why this priority**: gives a cost/speed choice and a sense of progress; vehicles (012) will use the
speed limit.

**Independent Test**: start a game in 1950 and see the types available; start one in 2000 and see more;
build one type and read its speed limit in the road's information window.

**Acceptance Scenarios**:

1. **Given** the game year is before a road type's introduction year, **When** the player opens the
   road type choice, **Then** that type is not offered.
2. **Given** two road types with different costs, **When** the player previews the same path with each,
   **Then** the costs differ according to the data.
3. **Given** a road of the player's own, **When** it is rebuilt with a newer type, **Then** its type and
   speed limit change, the cost difference is charged and the network uses the new speed limit for the
   affected pieces; rebuilding with a worse type is refused with the reason.
4. **Given** the year is 1980 or later, **When** the player drags with the motorway tool along a
   straight line, **Then** two parallel carriageways are built, each one-way in opposite directions, and
   the network allows travel along each only in its direction.
5. **Given** a motorway carriageway and a normal road beside it, **When** the player tries to join them
   with the road tool in the middle of the motorway, **Then** it is refused ("motorways join only at
   ends or connectors"); **When** the player places an exit or entry connector there, **Then** a one-way
   ramp links the carriageway's outer side to the road.
6. **Given** a valley crossed by roads, **When** the player builds an elevated motorway over it, **Then**
   two parallel bridges up to the elevated maximum span are built over the roads below, at the elevated
   price, and the roads below keep working.

---

### User Story 6 - One-way roads (Priority: P3)

The player can set a road piece of their own to one-way in either direction, and back to two-way, with
the one-way tool. The network then allows travel only in that direction along the piece.

**Why this priority**: useful for traffic management once vehicles exist (012); low value before.

**Independent Test**: make a straight road one-way; a route search with the direction succeeds and
against it fails.

**Acceptance Scenarios**:

1. **Given** a straight two-way road of the player's own, **When** the player clicks one piece with the
   one-way tool, **Then** it becomes one-way, a direction marker is drawn, and the network allows travel
   only in that direction.
2. **Given** a one-way piece, **When** the player clicks it again, **Then** it becomes one-way in the
   other direction; clicking a third time makes it two-way.
3. **Given** a junction piece, **When** the player tries to make it one-way, **Then** it is refused with
   the reason (only straight pieces can be one-way).

---

### Edge Cases

- A drag whose start or end is under water, on an industry, off the map or on a tile that cannot hold a
  road: the preview is red with the reason, or the path starts at the nearest allowed tile only when the
  player started on an existing road.
- No path exists within the search limit (blocked by water, cliffs or others' objects): red preview with
  "no path".
- A path longer than the per-action limit: red with "too large".
- Two roads meeting at a slope: junctions are only allowed on flat pieces; the proposed path avoids
  creating junctions on slopes.
- Diagonal pieces: a diagonal step between two tiles needs both tiles flat at the same height and the
  two tiles it cuts across no higher than the road; two diagonal steps may not cross each other at a
  tile corner.
- A motorway run whose second carriageway would fall on water, a cliff, an object or off the map: the
  whole run is refused with the reason of the blocking tile.
- Bridge ends on slopes facing the bridge direction: the ramp sits on the slope; bridges must have
  ends at the same height.
- A bridge whose span crosses an industry, a tall hill (terrain higher than the bridge deck) or the map
  edge: refused with the reason.
- Bridges or tunnels crossing each other: a bridge may pass over a tunnel (the tunnel is underground);
  two bridges may not cross at the same height; tunnels may not cross other tunnels.
- Terraforming next to bridge ends, tunnel entrances, under a bridge span or above a tunnel: those tiles
  are blocked in version 1 ("object in the way"), as are tiles under any road piece.
- Removing a road piece that is part of a bridge or tunnel removes the whole bridge or tunnel.
- Undo after the network changed elsewhere (another road joined the built one): undo is refused with
  "changed since" (feature 006 rule).
- Map-less scenarios (benchmarks, determinism goldens) keep working unchanged.
- Replays of recorded sessions with road commands reproduce the same network and fingerprints.

## Requirements *(mandatory)*

### Functional Requirements

**Road data and network**

- **FR-001**: The game MUST store roads per tile: which of the eight neighbours (four straight, four
  diagonal) the piece connects to, its road type, owner, one-way setting, and whether it is a flat,
  sloped, bridge, tunnel or motorway connector piece.
- **FR-002**: The network that route searches use MUST be derived from the tile roads and MUST reflect
  every built or removed road piece within the same tick the action is applied.
- **FR-003**: Updating the network after a change MUST cost time proportional to the size of the
  change (the area around the changed tiles), not to the size of the map or the whole network.
- **FR-004**: After any sequence of changes, the network MUST be identical in its connectivity and
  route results to a network built from scratch from the same tile roads.
- **FR-005**: Route searches MUST return a shortest route (by travel distance) whenever one exists, and
  report failure when none does; repeated searches on an unchanged network MUST give the same route.
- **FR-006**: Long route searches MUST use a faster method than a plain search over every tile so that
  the average stays within SC-002; cached results MUST be invalidated by network changes that affect
  them.
- **FR-007**: All road state MUST be part of the game's deterministic state: the same commands produce
  the same roads and fingerprints on every platform, recorded sessions replay them, and map-less
  scenarios keep their current fingerprints.

**Building**

- **FR-010**: The road tool MUST propose a path between the press tile and the current tile on every
  change of the current tile, preferring straight paths, existing roads and flat or gently sloped
  ground, and avoiding water, cliffs, industries and others' objects.
- **FR-011**: The preview MUST show the proposed tiles in green when the action can be built and in red
  with a reason when it cannot, plus the total cost, using the same rules the build itself applies
  (feature 006 FR-002).
- **FR-012**: Building MUST join the new pieces to existing roads they touch, forming corners and
  junctions automatically, and MUST NOT change existing roads of other owners beyond adding the
  connection towards the new piece.
- **FR-013**: Roads MUST only be built on land tiles whose slope the road can follow: flat tiles, and
  tiles that rise exactly one step along a straight (non-diagonal) road direction; junctions, corners
  and diagonal steps only on flat tiles at the same height, and a diagonal step only where the two tiles
  it cuts across are no higher than the road and no other diagonal crosses it. Other tiles are "too
  steep".
- **FR-014**: Building MUST clear trees on the path and charge for them; it MUST be refused where
  objects of others stand (except crossing their roads where allowed) or industries stand.
- **FR-015**: Costs MUST come from data: per piece by road type, extra per sloped piece, per bridge
  tile of span, per tunnel tile, per removal, with the tree clearing cost of feature 006.
- **FR-016**: Each road, bridge, tunnel, removal and one-way action MUST be one construction action: it
  appears in recorded sessions, spends money, is refused as a whole when anything blocks it, and can be
  undone with feature 006's undo rules.

**Bridges and tunnels**

- **FR-020**: Bridges MUST be straight, along one grid axis, with both ends on land at the same deck
  height, and may span water, lower land, roads and other low objects; terrain under the span must stay
  below the deck.
- **FR-021**: The maximum bridge length MUST grow with the game year, from data (8 tiles of span from
  1850, 16 from 1950, 32 from 1985); elevated motorways have their own, longer maximum (from data) and
  a higher price per tile.
- **FR-022**: Tunnels MUST be straight, along one grid axis, start on a tile sloping up into the hill
  in the tunnel's direction, and end at the first tile on the far side at the same height that slopes
  down out of the hill; the maximum tunnel length comes from data (64 tiles).
- **FR-023**: Bridges and tunnels MUST connect to the network only at their two ends.

**Removal**

- **FR-030**: The removal tool MUST remove road pieces owned by the player's company along the dragged
  line, charging the removal cost, and MUST skip others' pieces, showing them as skipped with the
  reason.
- **FR-031**: Town-owned road pieces MUST only be removable when the town's roads stay connected to
  each other afterwards (all of that town's remaining pieces reachable from one another), at the town
  road removal cost from data; otherwise the piece is skipped with "town needs this road". Feature 008
  adds the town rating condition.
- **FR-032**: Removing any piece of a bridge or tunnel MUST remove all of it.

**Road types and one-way**

- **FR-040**: Road types MUST be defined in a data file (name key, introduction year, speed limit, costs,
  look) validated at load with clear errors; the tool MUST only offer types available in the game year.
- **FR-041**: Building a road type over an existing road of the player's own MUST change its type and
  charge the difference of the two types' costs; downgrades are refused. Version 1 types: dirt road
  (1850, 50 km/h), paved road (1920, 80 km/h), fast road (1960, 100 km/h), motorway (1980, no limit of
  its own). Motorway pieces are never upgraded into or from other types.
- **FR-042**: Each network segment MUST carry the speed limit of its road type for later vehicle use.
- **FR-043**: A straight road piece of the player's own MUST be switchable between two-way, one-way in
  one direction and one-way in the other; the network MUST honour the direction. Motorway carriageways
  are always one-way and cannot be switched.

**Motorways**

- **FR-045**: The motorway tool MUST build two parallel one-way carriageways (adjacent tiles, opposite
  directions, right-hand traffic) along a straight or 45° line, as one action; a run starting at the end
  of an existing motorway MUST continue each carriageway into the matching one.
- **FR-046**: Motorway carriageways MUST NOT get level junctions with other roads except at their ends
  and at motorway connectors; the road tool MUST refuse such joins with the reason.
- **FR-047**: The connector tool MUST place an exit (carriageway → ramp) or an entry (ramp →
  carriageway) on the outer side of a straight, flat carriageway tile; the ramp is a one-way road piece
  that the road tool can extend; the connector is drawn with an acceleration or deceleration lane.
- **FR-048**: The motorway tool MUST build elevated sections (two parallel bridges) where the line
  crosses water, valleys, roads or other low objects, up to the elevated maximum span.

**Display and interaction**

- **FR-050**: Roads MUST be drawn as their own pieces on top of the terrain (flat, sloped, junction,
  bridge with supports, tunnel entrance), at every zoom level, and stay at 60 FPS on the largest map.
- **FR-051**: The road, bridge, tunnel, removal and one-way tools MUST be on the construction toolbar
  with key bindings, tooltips and localized texts in English and Spanish, like feature 006's tools.
- **FR-052**: Clicking a road MUST open its information window with its type, speed limit, owner and
  one-way setting.
- **FR-053**: Newly built or removed roads MUST be visible within the time feature 006 gives for edits.

**Starting roads**

- **FR-060**: A new game on a generated map MUST start with town roads on the town sites reserved by
  the map (a small street layout per town, owned by that town), replacing the prototype grid, so the
  player has something to connect to before towns exist (feature 008 grows them).
- **FR-061**: Games with a map MUST have no synthetic prototype vehicles (the vehicle option of a new
  game no longer applies to them until feature 012); map-less scenarios keep the prototype grid and its
  synthetic vehicles exactly as before.

### Key Entities *(include if feature involves data)*

- **Road piece**: what one tile holds: connections to its eight neighbours, road type, owner, one-way
  direction, kind (flat, slope, bridge end, bridge span, tunnel entrance, tunnel inside, motorway
  connector).
- **Bridge / tunnel**: a straight run of tiles between two ends; owner, road type, length; built and
  removed as a whole.
- **Road type**: data-defined: id, introduction year, speed limit (or none), cost per piece, width
  (one carriageway or motorway), look.
- **Motorway**: two parallel one-way carriageways of the motorway type, with connectors (exits and
  entries) and elevated sections.
- **Road network**: junctions and dead ends as nodes, the road between them as segments with length,
  speed limit and allowed directions; updated from the road pieces as they change.
- **Route**: an ordered list of segments from one point of the network to another, with its length.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Building or removing a road, bridge or tunnel changes what route searches return in the
  same tick (checked by automated tests on every kind of action).
- **SC-002**: On a fully connected road network covering a 4096 × 4096 map, route searches between
  random far-apart points take under 2 ms on average on the reference machine.
- **SC-003**: Updating the network after a typical action (a 100-tile road) takes under 1 ms on the
  reference machine, regardless of map size.
- **SC-004**: The road preview of a 200-tile drag updates within one frame (under 2 ms of planning) and
  allocates nothing per frame.
- **SC-005**: The 4096 × 4096 map with a dense road network keeps 60 FPS at normal speed.
- **SC-006**: A recorded session with road, bridge, tunnel, removal and one-way actions replays to
  identical fingerprints on Windows, Linux and macOS.
- **SC-007**: In a hand test, the developer builds a road between two towns across a river and a hill
  (using a bridge and a tunnel) in under two minutes without reading instructions.

## Assumptions

- Traffic drives on the right; motorway carriageways are placed accordingly.
- One road per tile; a tile with a road holds nothing else except a bridge passing above it.
- Bridges have a single look per era in version 1; final art comes with feature 018.
- Road vehicles, stations, depots, level crossings with rail and traffic rules are out of scope
  (features 010, 012, 015); the network exposes what they need (segments, lengths, speed limits,
  directions).
- Town roads are owned by the town whose site they stand on; town ratings (008) do not exist yet.
- Rails (015) do not exist yet; "over rails" for elevated motorways means over any low object, so rails
  will be covered when they arrive.
- Map-less scenarios (benchmarks, CI goldens) keep the prototype grid unchanged.
- Money units, undo window and per-action limits are those of feature 006.

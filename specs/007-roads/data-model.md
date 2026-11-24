# Data Model: Roads (feature 007)

All simulation values are integers (constitution III). New simulation types are registered in
`NoFloatingPointStateTests`.

## Dir8 (Talt.Core.Roads)

`0 N (0,−1), 1 NE (+1,−1), 2 E (+1,0), 3 SE (+1,+1), 4 S (0,+1), 5 SW (−1,+1), 6 W (−1,0), 7 NW (−1,−1)`.
Opposite = (d + 4) & 7; axis directions are even. Step length: axis 65 536 units, diagonal 92 682.

## RoadLayer (owned by ConstructionState, hashed)

Per tile (index `x + z·width`): `Links` byte, `Exits` byte (⊆ Links), `Type` byte (0 none, else type
index + 1), `Flags` byte (`RoadFeatures`: BridgeEnd 1, TunnelPortal 2, Connector 4, OverBridgeX 8,
OverBridgeZ 16, UnderTunnelX 32, UnderTunnelZ 64), `Span` byte (long-link length in tiles on ends: the distance
between the two ends), `LongDir` byte (long-link direction + 1, 0 none).
Invariants (checked by `RoadLayer.Validate` in tests): a link bit implies the neighbour's opposite link
bit; `Exits ⊆ Links`; `Links ≠ 0` ⇒ `Type ≠ 0`; `Type ≠ 0` ⇔ `TileObjects.Kind == Road` (a piece without
links is allowed: a single tile, or what removal leaves); ends of a long link point at each other with equal `Span`. Changed tiles are queued for the client (`TryTakeChangedTiles`, not hashed).

`RoadPiece` (readonly record struct): `Links, Exits, Type, Flags, Span, LongDir` — the value a `TileChange` and a
`TileSnapshot` carry.

## TileObjects / TileChange / TileSnapshot (feature 006, extended)

- `TileObjectKind.PublicRoad` renamed `Road` (value 1); `construction.json` key `road`.
- `TileChange` gains `RoadPiece OldRoad, NewRoad` and `bool RoadChanged` (default false: terraform and
  clear changes leave roads alone).
- `TileSnapshot` gains `RoadPiece Road`, so undo restores road pieces exactly.

## RoadType (roads.json, validated at load)

`id` (string, `road.type.<id>` is the name key), `introYear`, `speedKmh` (0 = no limit of its own),
`buildCost` (per tile and lane), `motorway` (bool), `look` (`surface`, `marking` sRGB hex, `markings`:
none/centre/edges).

Version 1: dirt (1850, 50, 150), paved (1920, 80, 400), fast (1960, 100, 900), motorway (1980, 0, 2 500,
motorway).

## RoadRules (roads.json, validated at load)

`types[]`, `slopeCost` 200, `removeCost` 100, `townRoadRemoveCost` 2 000, `oneWayCost` 50,
`connectorCost` 1 500, `bridge { costPerTile 2 000, eras [{fromYear 1850, maxSpan 8}, {1950, 16},
{1985, 32}], elevatedCostPerTile 6 000, elevatedMaxSpan 64 }`, `tunnel { costPerTile 3 000, maxLength 64 }`,
`planner { maxSearchNodes 60 000, searchMargin 48, maxPathTiles 1 024 }`. Trees use 006's `clearTreeCost`.

## RoadNetwork (Talt.Core.Network; derived, not hashed)

- Node: stable id (free list); `NodeTile[id]`, `NodeRegion[id]`; out-edges in its region's CSR block.
- Region (64 × 64 tiles): `Nodes` (ids sorted by tile), `FirstEdge[slot]`, `EdgeTo[]` (node id),
  `EdgeLength[]` (units, int), `EdgeSpeed[]` (km/h, 0 none), `EdgeFirstDir[]` (byte).
- `Version` (int) bumped by every update; `NodeAt(tile)`; `OutEdges(node)`; `Canonical()` (tests).
- Lifecycle: `Build(layer)` once at game creation, `Update(changedTiles)` after each commit/undo.

## Route (RoadRouter result)

`RouteStep(FromTile, FirstDir, ToTile, Length, SpeedKmh)` list + total length (long). Valid until the
next network change.

## Commands (all `ConstructionCommand`s, see contracts/commands.md)

`BuildRoadCommand`, `BuildBridgeCommand`, `BuildTunnelCommand`, `RemoveRoadCommand`, `SetOneWayCommand`,
`BuildMotorwayCommand`, `BuildMotorwayConnectorCommand`.

## ConstructionState additions

`Roads` (RoadLayer), `Network` (RoadNetwork), `RoadRules`, `Year` (set from the date, not hashed),
`Industries` (IndustryFleet, read-only, for footprint checks). `AppendTo` appends `Roads` after `Undo`.

# Contract: Core API used by the client, headless tool and tests

`GameSimulation.RoadVehicles` (`RoadVehicleSystem?`, null map-less), `.Lines` (`LineRegistry`), `.Depots`
(`DepotRegistry`), `.Rules` (`VehicleRules`).

Queries (read-only, safe between ticks):

- `Count`, `IdAt(slot)`, `SlotOf(id)`, `ModelAt(slot)`, `OwnerAt`, `CargoAt`, `LineAt`, `StopIndexAt`, `StateAt(slot)`
  (`RoadVehicleStatus`), `HoldAt(slot)` (read-only `CargoHold`), `SpeedKmhAt`, `AgeDaysAt`, `ReliabilityAt`,
  `ValueAt`, `ProfitThisYearAt`, `ProfitLastYearAt`, `DepotAt` (inside, or −1), `ServiceIntervalAt`, `TargetAt`.
- `CopyPoses(Span<RoadVehiclePose>)` / parallel overload: `(Vector2 position tiles, Vector2 direction, float height
  hint, byte flags)` rendering only; `CopyIds`.
- `VehiclesInDepot(depotId, List<int>)`, `VehiclesOnLine(lineId, List<int>)`.
- `Notices` (`VehicleNoticeFeed`), `CheckInvariants()` → null or the first broken invariant.
- `IsTileOccupied(tile)` (construction occupancy, R12).

Ticking: `Tick(bool parallel)` (from `GameSimulation.Tick`), `AdvanceDay(GameDate)`, `AdvanceMonth(GameDate)`,
`AdvanceYear(GameDate)`.

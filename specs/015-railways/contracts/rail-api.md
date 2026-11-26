# Rail API (015, Core → client and tests)

- `GameSimulation.Trains` (`TrainSystem?`, null map-less); `ConstructionState.Rails` (`RailLayer`).
- `TrainSystem`: `Count`, `IdAt(slot)`, `SlotOf(id)`, `PartCount(slot)`, `PartModel(slot, i)`, `Status(slot)`,
  `SpeedKmhAt`, `LineAt`, `DepotAt`, `CopyPartPoses(Span<TrainPartPose>)` (render floats only), `OwnerOfTile(tile)`,
  `BlockAt(tile)`, `CheckInvariants()` (null or the first broken invariant), `Depots`, `Rules`, `Notices`.
- `RailRouter.FindRoute(...)` for tests and benchmarks.
- `ILevelCrossingGuard.IsClosed(tile)` consumed by road vehicles.

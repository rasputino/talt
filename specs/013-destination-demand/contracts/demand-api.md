# Contract: demand and routing API (Talt.Core)

## Mode-agnostic line source (for 012 road vehicles and 015 rail)

```csharp
namespace Talt.Core.Demand;

public interface ILinkSource
{
    /// Bumped whenever lines, stops, rules or vehicles on lines change.
    int LinkVersion { get; }

    /// Reports every line: collector.BeginLine(company, cargo, speedKmh, capacityPerMonth);
    /// collector.AddStop(stationId, loads, unloads) for each stop in order; collector.EndLine().
    void CollectLinks(LinkCollector collector);
}

// GameSimulation.Router.AddSource(ILinkSource) — rail adds its system the same way.
```

## Station operations (vehicles of any mode)

```csharp
// nextStationId: the station of the vehicle's next stop on its line (−1 unknown: destination cargo is not loaded and
// is unloaded as a transfer).
int StationRegistry.Load(int stationId, int cargo, int maxAmount, CargoHold hold, int speedKmh, int nextStationId);
UnloadResult StationRegistry.Unload(int stationId, CargoHold hold, UnloadMode mode, int nextStationId);

// UnloadResult adds: long CreditEarned (transfer credit recorded at this stop), long CreditDelivered (credit carried
// by the delivered parts, to subtract from the payment in vehicle profit).
```

The old overloads without `nextStationId` remain for tests and treat it as −1.

## Finance

```csharp
public interface ITransferValuer { long Estimate(int cargo, int amount, int distanceTiles, int days); }
StationRegistry.TransferValuer { get; set; }   // FinanceSystem implements it (Quote, no booking)
```

## Queries (client)

```csharp
DestinationTables.TryGetTable(int town, int cargo, out DestinationView view);   // own share, dests, shares
DestinationTables.Statistics(int town, int cargo, int index) → (MovedLast, UnservedLast, MovedThis, UnservedThis)
DestinationTables.MonthlyDemand(int town, int cargo, int index) → units per month
StationRegistry.DestinationWaiting(int stationId, int cargo, List<DestinationAmount> into) // (town, nextStationKey, amount)
CargoRouter.TryNextHop(int company, int cargo, int groupKey, int town, out int hop1, out int hop2, out int share2)
CargoRouter.Version   // bumped at each apply (windows refresh)
```

No new player commands: the feature reacts to existing line, vehicle and station commands.

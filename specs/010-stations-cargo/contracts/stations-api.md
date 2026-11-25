# Contract: Station API (Talt.Core.Stations)

Consumed by the client (tools, window, rendering, picking), the headless tool, benchmarks, tests and
features 011 (payments) and 012 (vehicles).

```csharp
public sealed class StationRegistry : IStateHashable
{
    public StationRules Rules { get; }
    public int Count { get; }                         // stations (road stations and warehouses)
    public int PieceCount { get; }
    public int GroupCount { get; }
    public int Version { get; }                       // bumped on construction/rename (client caches)

    // Queries
    public bool TryGetStationAt(int x, int z, out int stationId);
    public int SlotOf(int stationId);
    public int IdAt(int slot);
    public StationKind KindAt(int slot);
    public bool TryGetStation(int stationId, out StationInfo info);   // window snapshot (allocates; UI only)
    public int GroupOf(int stationId);                 // group slot
    public int WaitingAt(int stationId, int cargo);    // group total
    public int RatingAt(int stationId, int cargo);     // 0–1000, −1 when not served
    public bool Accepts(int stationId, int cargo);     // group acceptance
    public bool IsServed(int stationId, int cargo);
    public IEnumerable<int> StationsInGroup(int groupSlot);

    // Placement preview (allocation-free once warmed up)
    public void PreviewCatchment(int pieceTypeOrWarehouse, bool warehouse, int x, int z, int joinStationId,
                                 CatchmentPreview into);   // tiles, receives mask, accepts mask

    // Vehicle operations (feature 012) — conserve cargo
    public void MarkServed(int stationId, int cargo);
    public int Load(int stationId, int cargo, int maxAmount, CargoHold hold, int speedKmh);
    public UnloadResult Unload(int stationId, CargoHold hold, UnloadMode mode);

    // Payments (feature 011)
    public IDeliveryListener? DeliveryListener { get; set; }   // not state

    // Simulation (called by GameSimulation)
    public void AdvanceDay(GameDate date, bool parallel);
    public void AdvanceMonth(GameDate date);
    public void AppendTo(StateHasher hasher);                  // nothing when Count == 0
}

public enum UnloadMode { Normal, Transfer }

public readonly record struct UnloadResult(int Delivered, int Transferred, int Kept);

public interface IDeliveryListener { void OnDelivered(in CargoDelivery delivery); }

public readonly record struct CargoDelivery(int CompanyId, int Cargo, int Amount, int OriginStation,
    int DestinationStation, int SourceTown, int PickupDay, int DeliveryDay,
    DeliveryConsumer ConsumerKind, int ConsumerId);

public sealed class CargoHold        // vehicles own one each (012); tests use it directly
{
    public int Count { get; }        // packets held
    public HeldCargo this[int index] { get; }
    public int AmountOf(int cargo);
    public int Total { get; }
    public void Clear();
}

/// IndustryFleet (feature 009) gains:
public interface IProductionSink { int TakeProduction(int industryId, int cargo, int amount); }
public void AdvanceDay(GameDate date, IProductionSink? sink = null);
```

Rules (tested):
- `Load` takes oldest packets first, never more than waiting or `maxAmount`; marks served; sets pickup
  day for fresh packets; records pickup day and speed; reports passengers/mail as transported for
  their source town.
- `Unload`: cargo accepted by the group (and not originating in it) goes to consumers and produces one
  `CargoDelivery` per consumer and packet part; otherwise kept or, with `Transfer`, waits at the group
  with its origin and pickup day.
- Sum of producer output = industry stock + waiting + holds + delivered + lost, exactly.

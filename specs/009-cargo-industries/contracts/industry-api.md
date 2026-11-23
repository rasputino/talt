# Contract: Industry and content API (`Talt.Core`)

Public surface consumed by the client, the headless tool, benchmarks, tests and later features (010
stations, 011 money, 006 construction). All types are in `Talt.Core.Content` /
`Talt.Core.Industries` unless noted. Amounts are whole numbers; nothing here is floating point.
`SiteKind` is the existing plain enum from feature 004; an industry kind lists the site kinds it
accepts, and `SiteKind.Town` is rejected by validation.

## Content

```csharp
public enum CargoCategory { RawMaterial, ProcessedGood, Passenger, Mail, Goods }
[Flags] public enum VehicleKind { None = 0, Road = 1, Rail = 2, Ship = 4, Air = 8 }

public readonly record struct CargoDefinition(
    string Id, string NameKey, CargoCategory Category, string UnitKey,
    bool DestinationRequired, int ValueDecayPerDayPerMille, int MinimumValuePercent,
    uint Color, VehicleKind VehicleKinds);

public readonly record struct ProductionEntry(string CargoId, int PerMonth);
public readonly record struct RecipeDefinition(string InputCargoId, int InputAmount, string OutputCargoId, int OutputAmount);
public readonly record struct StockDefinition(string CargoId, int Capacity);
public readonly record struct ModelBox(int X, int Y, int Z, int Width, int Depth, int Height, uint Color);

public readonly record struct IndustryKindDefinition(
    string Id, string NameKey, int FootprintWidth, int FootprintDepth,
    IReadOnlyList<ProductionEntry> Production, IReadOnlyList<string> Accepts,
    IReadOnlyList<RecipeDefinition> Recipes, IReadOnlyList<StockDefinition> Stock,
    IReadOnlyList<ModelBox> Model, IReadOnlyList<SiteKind> SiteKinds,
    int EraStart, int EraEnd, int Weight);

public sealed record EconomyRules(
    int InitialOccupancyPercent = 75, int DriftPerMille = 15, int GrowthPerMille = 25,
    int DeclineAfterMonths = 24, int DeclinePerMille = 20, int CloseAfterMonths = 60,
    int CloseChancePerMille = 8, int CloseAtRatePerMille = 700,
    int OpeningChancePerMillePerSite = 4, int MaxOpeningsPerMonth = 3,
    int MinRatePerMille = 300, int MaxRatePerMille = 1500);

public sealed class GameContent
{
    public static GameContent Default { get; }          // lazy LoadDefault(); throws InvalidDataException
    public static GameContent LoadDefault();
    public static GameContent FromJson(string cargoJson, string industriesJson,
                                       string cargoFileName = "cargo.json",
                                       string industriesFileName = "industries.json");

    public int CargoCount { get; }
    public int IndustryKindCount { get; }
    public EconomyRules Economy { get; }
    public IReadOnlyList<CargoDefinition> AllCargo { get; }
    public IReadOnlyList<IndustryKindDefinition> AllIndustryKinds { get; }

    public CargoDefinition Cargo(int index);              // ArgumentOutOfRangeException
    public int CargoIndex(string id);                     // -1 when unknown
    public IndustryKindDefinition IndustryKind(int index);
    public int IndustryKindIndex(string id);              // -1 when unknown
    public ulong Fingerprint();
}
```

Guarantees:

- Validation follows `contracts/content-schema.md`; errors are `InvalidDataException` with the file
  name and the offending id/field. A content object is immutable and safe to share.
- `Default` loads the embedded resources once per process; a broken data file throws on first use, so
  the client stops before creating a world.

## Simulation integration

```csharp
public sealed class GameSimulation
{
    public GameContent Content { get; }
    public IndustryFleet Industries { get; }

    public static GameSimulation Create(ScenarioSettings settings,
                                        MapData? map = null,
                                        GameContent? content = null);
}
```

`Create` behaviour:

1. Validates settings; `content ??= GameContent.Default`.
2. `map ??=` `MapGenerator.Generate(settings.Map)` when `settings.Map` is not null; otherwise no map.
3. Builds the network and fleet exactly as before, then creates the industry fleet:
   `IndustryFleet.Create(content, map, settings.StartYear, Random.Fork(0x1D05), initialOccupancyPerMille: null)`
   or `IndustryFleet.CreateEmpty(content, settings.MapSizeTiles, settings.MapSizeTiles)`.
4. Registers `AdvanceDay`/`AdvanceMonth` on `Events` (day handler first, as the event dispatcher
   already guarantees).
5. `ComputeFingerprint` appends `Industries` after vehicles; an empty fleet appends nothing, so
   existing map-less goldens are unchanged.

`ScenarioSettings` gains `MapSettings? Map = null`; equality includes it, and recorded sessions write
it as `"map"` (see `contracts/cli.md`).

## IndustryFleet

```csharp
public readonly record struct IndustryInfo(
    int Id, int X, int Z, string KindId, string NameKey, int RatePerMille, int MonthsNeglected,
    IReadOnlyList<ProductionEntry> Production, IReadOnlyList<string> Accepts,
    IReadOnlyList<RecipeDefinition> Recipes,
    IReadOnlyList<(string CargoId, int Available, int PendingInput, int Capacity)> Cargo,
    IReadOnlyList<(string CargoId, int Accepted, int Shipped)> LastMonth);

// Built from the live arrays by IndustryFleet.TryGetIndustry; implemented in
// src/Talt.Core/Industries/IndustryInfo.cs.
public enum DeliveryOutcome { Refused, Accepted, PartiallyAccepted }
public readonly record struct IndustryDeliveryResult(
    DeliveryOutcome Outcome, int Accepted, int Refused, int Consumed,
    string? ConvertedCargoId = null, int ConvertedAmount = 0);

public sealed class IndustryFleet : IStateHashable
{
    public static IndustryFleet Create(GameContent content, MapData map, int startYear,
                                       DeterministicRandom random,
                                       int? initialOccupancyPerMille = null);
    public static IndustryFleet CreateEmpty(GameContent content, int mapWidth, int mapDepth);

    public GameContent Content { get; }
    public int Count { get; }                    // open industries; slots are 0..Count-1
    public int SiteCount { get; }
    public int FreeSiteCount { get; }
    public int Version { get; }                  // bumps on placement/open/close (client cache)
    public int MapWidth { get; }
    public int MapDepth { get; }

    public int IdAt(int slot);
    public int KindIndexAt(int slot);
    public string KindIdAt(int slot);
    public int XAt(int slot);
    public int ZAt(int slot);
    public int RatePerMilleAt(int slot);
    public int MonthsNeglectedAt(int slot);
    public int AvailableAt(int slot, int cargoIndex);
    public int PendingInputAt(int slot, int cargoIndex);
    public int AcceptedLastMonthAt(int slot, int cargoIndex);
    public int ShippedLastMonthAt(int slot, int cargoIndex);
    public int StockCapacityAt(int slot, int cargoIndex);
    public int EffectiveProductionPerMonth(int slot, int cargoIndex); // base * rate / 1000
    public bool FootprintBounds(int slot, out int minX, out int minZ, out int maxX, out int maxZ);

    public bool TryGetIndustry(int industryId, out IndustryInfo info);
    public bool TryGetIndustryAt(int x, int z, out int industryId);
    public bool IsOccupied(int x, int z) => TryGetIndustryAt(x, z, out _);

    public void AdvanceDay(GameDate date);       // production accumulation (simulation event)
    public void AdvanceMonth(GameDate date);     // statistics, lifecycle, openings (simulation event)
    public IndustryDeliveryResult Deliver(int industryId, int cargoIndex, int amount);
    public int Take(int industryId, int cargoIndex, int amount); // feature 010 loads cargo
    public void AppendTo(StateHasher hasher);
}
```

Guarantees:

- `Create` places about `InitialOccupancyPercent` of `map.IndustrySites` (or the explicit per-mille
  override), never outside the map, never overlapping towns (sites are spaced) or other industries.
  `map` is referenced, not copied.
- `AdvanceDay`/`AdvanceMonth` are the only methods that change production state; they allocate
  nothing and are deterministic given the fleet's random stream.
- `Deliver` is idempotent in the sense that repeated calls behave as separate deliveries. It refuses
  unknown industries/cargo, non-positive amounts and cargo the kind does not accept. Recipe cargo is
  processed in whole batches; a partial batch stays as pending input.
- `Take` never removes more than is available and increments the shipped counters.
- `TryGetIndustryAt` covers every tile of every open industry and is O(1); construction (006) and
  picking use it. Closed industries free their tiles.
- `AppendTo` writes nothing when `Count == 0`; otherwise it writes count, all scalar arrays, all
  per-cargo tables, the random state and the content fingerprint, in fixed order.
- Not thread safe: it runs on the simulation thread.

## Client-facing behaviour (not part of Core)

- `GameSession.IndustryInstances` (one `IndustryInstance` per model box, rebuilt when
  `Industries.Version` changes) and `WebGpuRenderer.UpdateIndustries`.
- `PickKind.Industry` with `PickResult.IndustryId`; `IndustryPickLayer` priority 75.
- `ObjectInfoWindow` industry branch: kind name, production per month, accepted cargo and recipes,
  available and pending cargo, accepted/shipped last month; a "gone" message when the industry closes.
- Localization keys: `industry.<id>`, `cargo.<id>`, `cargo.category.<category>`, `unit.<unit>`,
  `info.industry.*` in both `Strings.en.json` and `Strings.es.json`.

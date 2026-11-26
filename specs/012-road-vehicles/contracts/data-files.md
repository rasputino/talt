# Contract: `src/Talt.Core/Vehicles/Data/vehicles.json`

JSON with comments, embedded, validated by `VehicleRules.FromJson` (errors name the file, the model id and the field).
Fingerprinted into the vehicle state hash.

```jsonc
{
  "kmhPerTileSecond": 40,          // 40 km/h = 1 tile per second
  "physics": { "brakingTilesPerSecond2": 2.5 as 2500 (milli), "minGapMilliTiles": 50,
               "junctionRadiusMilliTiles": 220, "deadEndExtentMilliTiles": 490, "lookAheadMilliTiles": 300,
               "curveSpeedPercent": 60, "rampSpeedPercent": 80 },
  "stops": { "bayPlaces": 2, "minDwellTicks": 40, "loadIntervalTicks": 10 },
  "routing": { "searchNodesPerTick": 40000 },
  "stuck": { "noticeDays": 2 },      // turnaround after twice this
  "reliability": { "decayPerDay": 1, "oldAgeFactor": 3, "minimum": 150, "breakdownChanceScale": 25,
                   "breakdownDays": 6, "breakdownPulseTicks": 40, "recoverPerMille": 150 },
  "service": { "defaultIntervalDays": 150 },
  "value": { "minValuePercent": 10 },
  "depot": { "cost": 3500, "removeCost": 600, "maintenancePerYear": 400, "model": { "base": "#RRGGBB", "roof": "#RRGGBB" } },
  "cargoCategories": { "passengers": ["passengers"], "bulk": ["coal","iron-ore","grain","wood"],
                       "tanker": ["oil","fuel"], "box": ["mail","goods","food","steel","lumber","livestock"] },
  "models": [ { "id": "...", ... } ]   // see data-model.md VehicleModel
}
```

Validation: every cargo of `cargo.json` except passengers appears in exactly one truck category; at least one bus and
one truck of every category is available in every year from 1850 to 2100; lengths ≤ (1 − 2·junctionRadius − minGap).

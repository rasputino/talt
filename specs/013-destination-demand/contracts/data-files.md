# Contract: `src/Talt.Core/Demand/Data/demand.json`

JSON with comments, embedded resource, validated at load (`DemandRules.FromJson`); errors name the file and field.

```jsonc
{
  "cargo": [
    { "id": "passengers", "source": "buildings",  "ownTownPerMille": 500, "populationExponent": 1, "distanceExponent": 1 },
    { "id": "mail",       "source": "buildings",  "ownTownPerMille": 500, "populationExponent": 2, "distanceExponent": 2 },
    { "id": "goods",      "source": "industries", "ownTownPerMille": 0,   "populationExponent": 1, "distanceExponent": 1 }
  ],
  "maxDestinations": 32,
  "rangeTiles": 1500,
  "expiryDays": 60,
  "routing": {
    "updateIntervalDays": 4, "applyDelayDays": 2, "iterations": 2,
    "dwellTicks": 60, "transferPenaltyTicks": 400, "congestionPenaltyPerMille": 1000,
    "zeroCapacityPenaltyTicks": 20000, "maxRouteDays": 90, "pruneMarginTiles": 400
  },
  "overlay": { "pairs": 40 }
}
```

Rules: every `id` is a cargo with `destinationRequired: true` in `cargo.json` and every such cargo is listed;
`source` is `buildings` only for passengers and mail categories; `ownTownPerMille` 0–1000 (0 for industries);
exponents a 0–2, k 0–3; `maxDestinations` 1–64; positive ranges and intervals; `applyDelayDays` < `updateIntervalDays`;
`iterations` 1–2.

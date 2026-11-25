# Contract: `Finance/Data/finance.json`

JSON with comments and trailing commas. Unknown fields are errors; every error names the file and
field (`finance.json: 'loan.step' must be 1 or more (was 0).`). Every value is original to TALT.

```jsonc
{
  "loan": { "start": 0, "max": 500000, "step": 10000, "interestPerMille": 50 },
  // Money per 1 000 units per tile; full value for freeDays, then cargo.json decay.
  "payments": {
    "passengers": { "rate": 3400, "freeDays": 4 },
    "mail": { "rate": 4600, "freeDays": 3 },
    "coal": { "rate": 2400, "freeDays": 20 }
    // … one entry per cargo of cargo.json
  },
  "minDistance": 1,
  "maintenance": {
    "roads": { "dirt": 30, "paved": 60, "fast": 110, "motorway": 260 },
    "stationPieces": { "bus-bay": 300, "bus-stop": 250, "truck-bay": 400, "truck-stop": 320 },
    "warehouses": { "small": 1800, "medium": 4200, "large": 10000 }
  },
  "propertyValuePercent": 50,
  "bankruptcy": { "warningMonths": 3, "gameOverMonths": 6 },
  "inflation": { "costBasisPoints": 200, "paymentBasisPoints": 150, "years": 100 },
  "debug": { "serviceDistance": 40 }
}
```

Cross-checks at simulation creation: every cargo id of `cargo.json` has a payment and no unknown id is
listed; the same for road types (`roads.json`), station pieces and warehouse sizes (`stations.json`).

Recorded sessions: `settings.finance = { "sandbox": bool, "inflation": bool, "servicePays": bool, "infiniteMoney": bool }`
(`infiniteMoney` optional when reading),
written only when not all false.

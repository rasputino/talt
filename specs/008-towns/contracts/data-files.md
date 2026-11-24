# Contract: Town data files

Embedded in `Talt.Core` as `Talt.Core.Towns.Data.<file>`; JSON with comments and trailing commas
allowed; unknown keys are errors; every error names the file, the entry id and the field
(`InvalidDataException`). Changing a value changes what recorded sessions with maps replay to.

## town-buildings.json

```jsonc
{
  "buildings": [
    {
      "id": "cottage",              // unique, [a-z0-9-]+
      "category": "house",          // house | apartment | shop | office | civic
      "width": 1, "depth": 1,       // 1–2
      "height": 2,                  // 1–32 storeys
      "residents": 4,
      "passengersPerMonth": 3, "mailPerMonth": 1,
      "goodsAcceptance": 0, "foodAcceptance": 1,   // eighths 0–8
      "era": [1800, 9000],
      "minTownPopulation": 0,
      "zones": ["inner", "outer"],  // centre | inner | outer
      "weight": 100,                // 1–1000
      "level": 1,                   // 1–9
      "model": { "wall": "#D8C8A8", "roof": "#8A3B2E", "roofStyle": "pitched", "windows": "rows" }
    }
  ]
}
```

Rules: at least one type for each zone available from the earliest era; ids unique.

## towns.json

```jsonc
{
  "growth": { "radiusBase": 5, "radiusPerSqrtPopulation": 45, "centreZonePercent": 30,
              "innerZonePercent": 65, "attemptsPerAction": 12, "failuresBeforeStreet": 2,
              "growthPerMilleOfBuildings": 25, "replaceChancePercent": 25, "replaceMinPopulation": 800,
              "gridSpacing": 4, "gridPatternPercent": 60, "streetSegmentMin": 3, "streetSegmentMax": 7,
              "foundingAttemptBudget": 4000 },
  "sizes": [ { "weight": 70, "minPopulation": 100, "maxPopulation": 500 }, ... ],
  "sizeScalePercent": { "small": 50, "normal": 100, "large": 200 },
  "ratings": { "initial": 500, "hostile": 200, "poor": 400, "neutral": 600, "good": 800,
               "demolishMinimum": 400, "stationMinimum": 200, "buildingPenalty": 60, "roadPenalty": 25,
               "treePenalty": 8, "maxPenaltyPerAction": 250, "zoneMargin": 6, "recoveryPerMonth": 20,
               "serviceTargetSpan": 400 }
}
```

Rules: thresholds strictly increasing within 0–1 000; min ≤ max; weights > 0; percents 1–1000.

## town-names.json

```jsonc
{
  "english": { "starts": [...], "middles": [...], "ends": [...], "prefixWords": [...], "suffixWords": [...],
               "patterns": { "startEnd": 60, "startMiddleEnd": 20, "prefixName": 10, "nameSuffix": 10 } },
  "spanish": { ... same shape ... }
}
```

Rules: lists non-empty (prefix/suffix lists may be empty only if their pattern weight is 0), entries
unique, letters (including accented), spaces, hyphens and apostrophes only. All parts are original to
TALT.

## construction.json (changed)

`demolishCosts.townBuilding`: cost per footprint tile of a demolished town building.

# Contract: Content data files (`Talt.Core/Content/Data`)

Two JSON files are embedded in `Talt.Core` and validated by `GameContent`:

- `cargo.json` — every cargo type in version 1.
- `industries.json` — the economy rules and every industry kind.

Both are UTF-8 with comments and trailing commas allowed; ids are stable `[a-z0-9-]+` strings.
Values are integers; colours are `#RRGGBB` strings. Unknown properties are ignored (forward compatible);
missing required properties or invalid values fail startup with `InvalidDataException` naming the file
and the id/field.

## cargo.json

```json
{
  "cargo": [
    {
      "id": "coal",
      "nameKey": "cargo.coal",
      "category": "raw",
      "unitKey": "unit.tonnes",
      "destinationRequired": false,
      "valueDecayPerDayPerMille": 4,
      "minimumValuePercent": 35,
      "color": "#3B3B3B",
      "vehicleKinds": ["road", "rail", "ship"]
    }
  ]
}
```

`category`: `raw`, `processed`, `passenger`, `mail`, `goods`.
`vehicleKinds`: any of `road`, `rail`, `ship`, `air`.

Validation:

| Rule | Error names |
|------|-------------|
| `cargo` non-empty; unique ids matching `[a-z0-9-]+`; non-empty `nameKey`/`unitKey` | the id and field |
| `passenger`, `mail`, `goods` require `destinationRequired: true`; `raw`, `processed` require `false` | the cargo id |
| `valueDecayPerDayPerMille` 0–1000; `minimumValuePercent` 0–100; `color` `#RRGGBB` | the field |
| `vehicleKinds` non-empty and known | the cargo id |

Default cargo (all cargo amounts are whole numbers in the stated unit):

| Id | Category | Unit key | Destination | Decay / day (‰) | Floor % | Vehicles | Colour |
|----|----------|----------|-------------|-----------------|---------|----------|--------|
| `passengers` | passenger | `unit.passengers` | yes | 60 | 25 | road rail ship air | `#2E77C8` |
| `mail` | mail | `unit.bags` | yes | 40 | 30 | road rail ship air | `#E8D34A` |
| `coal` | raw | `unit.tonnes` | no | 4 | 35 | road rail ship | `#3B3B3B` |
| `wood` | raw | `unit.tonnes` | no | 4 | 35 | road rail ship | `#7A5230` |
| `iron-ore` | raw | `unit.tonnes` | no | 3 | 40 | road rail ship | `#8C6A55` |
| `grain` | raw | `unit.tonnes` | no | 6 | 30 | road rail ship | `#D9B84A` |
| `livestock` | raw | `unit.heads` | no | 10 | 25 | road | `#B98A6B` |
| `oil` | raw | `unit.tonnes` | no | 8 | 30 | road rail ship | `#1F2326` |
| `steel` | processed | `unit.tonnes` | no | 4 | 50 | road rail ship | `#8A9199` |
| `lumber` | processed | `unit.tonnes` | no | 4 | 50 | road rail ship | `#A97C50` |
| `fuel` | processed | `unit.tonnes` | no | 12 | 30 | road rail ship | `#C46A2A` |
| `food` | processed | `unit.tonnes` | no | 15 | 25 | road rail ship | `#5FA84F` |
| `goods` | goods | `unit.crates` | yes | 10 | 20 | road rail | `#B45FA8` |

## industries.json

```json
{
  "economy": {
    "initialOccupancyPercent": 75,
    "driftPerMille": 15,
    "growthPerMille": 25,
    "declineAfterMonths": 24,
    "declinePerMille": 20,
    "closeAfterMonths": 60,
    "closeChancePerMille": 8,
    "closeAtRatePerMille": 700,
    "openingChancePerMillePerSite": 4,
    "maxOpeningsPerMonth": 3,
    "minRatePerMille": 300,
    "maxRatePerMille": 1500
  },
  "industries": [
    {
      "id": "sawmill",
      "nameKey": "industry.sawmill",
      "footprint": [4, 3],
      "siteKinds": ["plain", "forest"],
      "era": [1800, 9999],
      "weight": 9,
      "production": [],
      "accepts": ["wood"],
      "recipes": [
        { "input": "wood", "inputAmount": 4, "output": "lumber", "outputAmount": 3 }
      ],
      "stock": [
        { "cargo": "wood", "capacity": 400 },
        { "cargo": "lumber", "capacity": 600 }
      ],
      "model": [
        { "x": 0, "y": 0, "z": 0, "width": 4, "depth": 3, "height": 2, "color": "#8A6A45" },
        { "x": 0, "y": 2, "z": 0, "width": 4, "depth": 3, "height": 1, "color": "#5B4630" },
        { "x": -1, "y": 0, "z": 1, "width": 2, "depth": 1, "height": 1, "color": "#C0A070" }
      ]
    }
  ]
}
```

Validation:

| Rule | Error names |
|------|-------------|
| exactly one cargo file entry per id used below | the kind and cargo |
| kinds non-empty; unique ids; non-empty `nameKey` | the id |
| `footprint` 1–5 each side; both within the 5×5 reserved square | the kind |
| `siteKinds` non-empty, known (`plain`, `forest`, `hills`, `coast`) | the kind |
| `era` 1800–9999, start ≤ end | the kind |
| `weight` 1–1000 | the kind |
| `production` unique cargo, `perMonth` 0–1 000 000 | the kind/cargo |
| `accepts` unique known cargo | the kind/cargo |
| recipes unique input cargo, amounts 1–10 000, input ≠ output, known cargo | the kind |
| `stock` unique cargo, capacity > 0; covers every produced/output and recipe-input cargo | the kind/cargo |
| `model` non-empty, height 1–12, boxes inside the footprint and above y 0 | the kind |
| economy values within the ranges of data-model.md and `minRate < maxRate` | the economy field |

Default industry kinds:

| Id | Sites | Footprint | Era | Weight | Produces / month | Accepts | Recipes | Stock caps |
|----|-------|-----------|-----|--------|------------------|---------|---------|------------|
| `coal-mine` | hills | 4×3 | 1800–9999 | 14 | 180 coal | — | — | coal 900 |
| `iron-mine` | hills | 4×3 | 1800–9999 | 10 | 150 iron-ore | — | — | iron-ore 750 |
| `forest` | forest | 4×4 | 1800–9999 | 16 | 160 wood | — | — | wood 800 |
| `farm` | plain | 4×4 | 1800–9999 | 18 | 120 grain, 24 livestock | — | — | grain 600, livestock 120 |
| `oil-well` | coast | 3×3 | 1900–9999 | 10 | 150 oil | — | — | oil 750 |
| `power-station` | plain | 4×4 | 1900–9999 | 8 | — | coal | — (coal is consumed) | — |
| `sawmill` | plain, forest | 4×3 | 1800–9999 | 9 | — | wood | 4 wood → 3 lumber | wood 400, lumber 600 |
| `steel-mill` | plain | 4×4 | 1850–9999 | 7 | — | iron-ore, coal | 3 iron-ore → 2 steel; coal consumed | iron-ore 450, steel 600 |
| `refinery` | coast, plain | 4×4 | 1920–9999 | 7 | — | oil | 3 oil → 2 fuel | oil 450, fuel 600 |
| `factory` | plain | 4×4 | 1930–9999 | 8 | — | steel | 3 steel → 2 goods | steel 450, goods 600 |
| `food-plant` | plain | 4×3 | 1900–9999 | 8 | — | grain, livestock | 4 grain → 3 food; 3 livestock → 2 food | grain 400, livestock 120, food 600 |

All kinds are placed only on the site kinds listed in `siteKinds` (JSON `plain`, `forest`, `hills`,
`coast` map to `SiteKind.IndustryPlain`, `IndustryForest`, `IndustryHills`, `IndustryCoast`; `town` is
rejected). Their models are three or four coloured boxes each (base, roof/chimney/silo), defined in
the data file; feature 018 replaces them with final art without touching code.

## Content fingerprint

`GameContent.Fingerprint()` hashes the ids and values in file order, so a data change is visible in the
simulation fingerprint whenever industries exist. It excludes JSON whitespace and property order inside
objects, so reformatting a file does not change a game.

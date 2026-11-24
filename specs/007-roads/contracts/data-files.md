# Contract: data files

`src/Talt.Core/Roads/Data/roads.json` (embedded, JSON with comments). Fields and version 1 values in
data-model.md (RoadType, RoadRules). Validation errors name the file and field, for example
`roads.json: 'types[2].speedKmh' must be 0 or more (was -5).`, `roads.json: 'bridge.eras' must be
sorted by fromYear`, unknown fields are errors, at least one non-motorway type must exist from the
earliest year, type ids unique.

`construction.json`: `demolishCosts.publicRoad` renamed `road`.

Localization: `road.type.<id>`, `tool.<id>.tooltip` for road tools, `construction.reason.<code>`, road
info window labels, in `Strings.en.json` and `Strings.es.json`.

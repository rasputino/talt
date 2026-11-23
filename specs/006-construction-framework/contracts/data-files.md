# Contract: Data Files

## `src/Talt.Core/Construction/Data/construction.json` (embedded resource)

```jsonc
{
  // Construction balancing (feature 006). Internal money unit: 1 = one US dollar.
  "startingBalance": 500000,
  "terraformStepCost": 250,
  "clearTreeCost": 40,
  "demolishCosts": { "publicRoad": 0, "test": 100 },
  "undoWindowTicks": 200,
  "maxHeight": 120,
  "maxTilesPerAction": 16384,
  "maxActionCost": 1000000000000
}
```

Validation errors throw `InvalidDataException` naming the file and field, e.g.
`construction.json: 'terraformStepCost' must be 0 or more (was -1).` Unknown fields are errors.

## `src/Talt.Client/Input/Data/actions.json`

New `tools` category: `toolRaise` (PageUp), `toolLower` (PageDown), `toolLevel` (L), `toolClear`
(X), `undoConstruction` (Backspace). Removed: `raiseTerrain`, `lowerTerrain`.

## Strings (`Strings.en.json`, `Strings.es.json`)

`tool.<id>.tooltip`, `action.tool*`, `action.undoConstruction`, `construction.cost`,
`construction.reason.<code>` (all codes of contracts/commands.md), `command.terraform`,
`command.clearArea`, `command.undoConstruction`, `command.rejected.<code>`.

## Recorded session (`.talt-session.json`, format 1)

`settings.map` (optional string): `MapSettings.ToText()` of the scenario's map. Absent = no map.

## Removed

`src/Talt.Client/Ui/Data/company.json` (`startingCapital`) — replaced by `startingBalance` above.

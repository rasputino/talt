# Contract: data files

All files are embedded resources validated at startup; an invalid file stops the game with a clear
message naming the file and the problem (constitution VII). Player settings are the exception: they
fall back to defaults (FR-026).

## `Localization/Strings.{en,es}.json`

Flat object `key → text`. Placeholders `{name}`. Plurals: `key.one` and `key.other`. Both files must
have the same keys (test).

## `Localization/Data/currencies.json`

```json
{ "currencies": [ { "code": "USD", "symbol": "$", "rate": 1.0, "decimals": 0 }, ... ] }
```

The internal unit equals one US dollar. Rates are fixed game data (approximate), display only. Names
are localized as `currency.<CODE>`.

## `Ui/Data/theme.json`

```json
{ "colors": { "panel": "#2B3440F0", ... }, "cornerRadius": 6, "borderWidth": 1, "spacing": 6,
  "padding": 8, "fontSizes": { "small": 13, "body": 16, "title": 18, "heading": 30 },
  "tooltipDelaySeconds": 0.5, "topBarHeight": 36, "toolbarWidth": 44 }
```

Colors are `#RRGGBB` or `#RRGGBBAA` in sRGB.

## `Ui/Data/icons.json`

```json
{ "size": 16, "icons": { "pause": { "palette": { "#": "#F2F2F2" }, "rows": ["................", ...] } } }
```

Each row has `size` characters; `.` is transparent; other characters map through the palette.

## `Ui/Data/company.json`

`{ "startingCapital": 500000 }` — internal units; replaced by the finances feature.

## `Input/Data/actions.json`

```json
{ "actions": [ { "id": "pause", "label": "action.pause", "category": "game", "kind": "press", "keys": ["Space"] }, ... ] }
```

Key names are `Silk.NET.Input.Key` names, optionally prefixed with `Shift+`.

## `settings.json` (user profile)

See data-model.md PlayerSettings; `keyBindings` is `{ "pause": ["P"] }`.

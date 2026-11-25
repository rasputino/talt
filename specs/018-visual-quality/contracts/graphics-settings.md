# Contract: graphics settings, presets, options and statistics

## Player settings (`settings.json`, version 4)

```jsonc
"graphics": {
  "preset": "medium",            // low | medium | high | custom
  "shadows": true,
  "shadowQuality": "medium",     // low | medium | high
  "antiAliasing": "msaa4",       // off | msaa4
  "waterEffects": true,
  "sceneryDensity": "medium",    // off | low | medium | high
  "modelDetail": "medium",       // low | medium | high
  "ambientOcclusion": "full",    // off | soft | full
  "dayNight": false
}
```

Missing object → Medium; each invalid property → its Medium value, reported once in the load
problems. With a named preset the effect values are taken from the presets file (so a balance change
reaches the player); with `custom` the saved values are used.

## Presets file (`Rendering/Data/graphics-presets.json`)

```jsonc
{
  "presets": {
    "low":    { "shadows": false, "shadowQuality": "low", "antiAliasing": "off", "waterEffects": true,
                "sceneryDensity": "low", "modelDetail": "low", "ambientOcclusion": "soft" },
    "medium": { },
    "high":   { }
  },
  "shadowQuality": { "low": { "cascades": 1, "size": 1024, "distance": 160, "taps": 1 } },
  "sceneryDensity": { "off": 0, "low": 0.35, "medium": 0.7, "high": 1 },
  "modelDetail": { "low": [40, 120], "medium": [70, 220], "high": [110, 360] },
  "ambientOcclusion": { "off": 0, "soft": 0.6, "full": 1 }
}
```

(Values shown are examples; the shipped file is the reference.)

## Command-line options (client)

| Option | Effect |
|---|---|
| `--graphics low\|medium\|high` | Uses that preset for this run, without saving it |
| `--measure-effects [seconds]` | Enters a game, runs the A/B measurement (vertical sync off, camera still), prints a table, exits; settings file untouched. Default 4 s per configuration after a 3 s warm-up |
| `--day-night` | Turns the cycle on for this run (screenshots) |
| `--time-of-day <0-1>` | Starts the cycle at this phase (0.5 = noon, 0.9 ≈ night) |

## Statistics (F3, `--stats`)

Adds: preset and effect values, shadow cascades × size, shadow draws, model draws, model instances,
decoration instances, CPU ms of shadow preparation and of model batching.

## Settings screen

Graphics section: preset choice (Low / Medium / High, Custom shown when values differ), then Shadows,
Shadow quality, Anti-aliasing, Water effects, Scenery density, Model detail, Ambient occlusion,
Day/night cycle. Changes apply immediately and are saved.

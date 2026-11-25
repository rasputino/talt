# Contract: stations.json (embedded `Talt.Core.Stations.Data.stations.json`)

JSON with comments, validated at load (`StationRules.Load`); errors name the file and field.

```jsonc
{
  "pieces": [
    { "id": "bus-bay",     "nameKey": "station.piece.bus-bay",     "kind": "bus",   "form": "bay",
      "cost": 1800, "removeCost": 400, "catchmentRadius": 3, "categories": ["passenger"],
      "model": { "base": "#8E9AA6", "roof": "#2E77C8" } },
    { "id": "bus-stop",    "kind": "bus",   "form": "driveThrough", "catchmentRadius": 3, ... },
    { "id": "truck-bay",   "kind": "truck", "form": "bay",          "catchmentRadius": 4,
      "categories": ["mail", "raw", "processed", "goods"], ... },
    { "id": "truck-stop",  "kind": "truck", "form": "driveThrough", "catchmentRadius": 4, ... }
  ],
  "warehouses": [
    { "id": "small",  "width": 2, "depth": 2, "cost": 12000, "removeCost": 2000, "influenceRadius": 8 },
    { "id": "medium", "width": 3, "depth": 3, "cost": 30000, "removeCost": 5000, "influenceRadius": 16 },
    { "id": "large",  "width": 4, "depth": 4, "cost": 80000, "removeCost": 12000, "influenceRadius": 32 }
  ],
  "maxSpread": 12,
  "waitingCap": 4000,
  "maxStationsPerProducer": 2,
  "acceptance": { "passengersFullAt": 24, "mailFullAt": 8 },
  "rating": {
    "initial": 550, "base": 170, "maxStepPerDay": 10, "overflowPenalty": 40,
    "pickupDays": [[3, 300], [7, 230], [14, 160], [30, 90], [60, 30]],
    "speedKmh":   [[100, 200], [80, 160], [60, 110], [40, 60], [20, 20]],
    "waiting":    [[100, 150], [300, 80], [700, 20], [1500, -40], [3000, -100]]
  },
  "loss": { "belowRating": 350, "aboveWaiting": 200, "perMilleAtZero": 40 },
  "debug": { "intervalDays": 5, "speedKmh": 60 }
}
```

Tables: `pickupDays` and `speedKmh` give the points of the first row whose threshold the value is at
most (days) / at least (speed); `waiting` the first row whose threshold the waiting amount is at most;
values beyond the last row give 0 (pickup, speed) or −160 (waiting, `waitingBeyond`).

`construction.json` gains `"station": 0` in `demolishCosts` (station pieces are removed with their own
tool and costs; the entry exists so the table names every kind).

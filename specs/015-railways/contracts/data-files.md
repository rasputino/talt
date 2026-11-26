# Data files (015)

`src/Talt.Core/Rails/Data/rail.json` (JSON with comments), validated by `RailRules.FromJson`; errors name the file and
field. Sections: `trackTypes` (id, nameKey, buildCost, removeCost, speedKmh (0 = no limit), electrified, color),
`signals` (block/path/oneWayPath costs), `bridge` (maxSpan, costPerTile), `tunnel` (maxLength, costPerTile),
`depot` (cost, removeCost, maintenancePerYear), `station` (maxPlatforms, maxLength), `physics` (gravityMilli,
rampGradePermille, rollingResistancePermille, brakingMilliTilesPerSecond2, curveSpeedPercent, accelerationScale,
milliTilesPerMetre), `stops` (minDwellTicks, loadIntervalTicks, pullForwardTicks), `routing` (searchNodesPerTick,
replanTicks, occupiedPenalty), `maxTrainLengthMilliTiles`, `maxParts`, `rollingStock` (see data-model.md).

`stations.json` gains piece `rail-platform` (kind `rail`, form `platform`); `finance.json` its maintenance and
`rail` maintenance per track type.

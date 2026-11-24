# Quickstart: verify roads (feature 007)

1. `dotnet build` and `dotnet test --filter "Category!=Gpu"` — all pass; `pwsh ci/check-fingerprints.ps1`
   passes unchanged (map-less goldens untouched).
2. Benchmarks: `dotnet run --project benchmarks/Talt.Benchmarks -c Release -- --filter "*RoadNetwork*" --job short`
   — LongRouteQuery < 2 ms, IncrementalUpdate < 1 ms, PlanRoad200 < 2 ms and 0 B.
3. Client: `dotnet run --project src/Talt.Client -c Release -- --map 1024 --seed 7 --water 35 --stats`.
   Towns show streets. Select the road tool (R), drag between two towns: green path + cost; release.
   Build a bridge (B) across a river and a tunnel (T) through a hill; remove a piece (Shift+R drag);
   toggle one-way (O); change type (Y); in 1980+ games (`--start-year 1990`), motorway (M) and
   connectors. Click a road: information window shows type, speed, owner, one-way.
4. Dense network FPS: `... -- --map 4096 --road-grid 16 --stats` — 60 FPS at normal speed.
5. Replay: record a session with road actions (`--record`), replay with the headless tool: identical.

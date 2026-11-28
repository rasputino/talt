# Verification

Build Release and run non-GPU tests, the fingerprint script, and the statistics benchmarks described in ARCHITECTURE.md. For the client demo open the line directory, compare profits and occupation, select a vehicle in the fleet directory, and verify the camera target. Set a headway on a mixed line and verify two independent departure clocks. After a month end inspect station, town and finance charts and turn on the three overlays. Disconnect a route leg and inspect its interruption. Check both UI languages and the latest complete month's heatmap legend.

The road demonstration uses:

```powershell
dotnet run -c Release --project src/Talt.Client -- 8 0 --seed 2 --seed-vehicles 8 --stats --camera 143,213
```

Press F4 for the line and fleet directory. Open Map layers on the top bar to toggle the route, catchment and traffic layers. A separate rail demonstration uses seed 3 and `--seed-trains 2` in place of the bus seeder. With Line routes enabled, click Show a line on the map to centre on a connected segment. On or after 1 February, enable Traffic heatmap and click Show the busiest segment. With both layers active, the route forms a narrow stripe over the wider heat-coloured band. A month without any passages reports zero activity. A fresh company with no lines shows an empty routes message.
In the 512² seed-4 demonstration, Slatewick is outside the served area. The three bus lines connect stops at (16, 94), (59, 100), (27, 133) and (54, 135). Use this scenario to verify that the off-screen message leads to an actual route. The same map and the same camera location are used before and after toggling layers.

Manual verification on Windows, 2026-11-28: Pablo confirmed the fleet and individual bus windows and supplied a second capture after the off-screen controls were added. On 5 October 1951 the map showed the routed streets, heat ribbons and all four September passage-count bands.

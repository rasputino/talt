# Quickstart: validating the user interface foundation (005)

## Automated

```powershell
dotnet build -c Release
dotnet test -c Release
dotnet run -c Release --project benchmarks/Talt.Benchmarks -- --filter "*Ui*" "*Picking*" --job short
```

Expected: all tests pass (layout, window manager, routing, bindings, localization coverage,
settings, picking, font atlas); `UiFrameBenchmarks` shows the 20-window frame well under 1 ms.

## By hand (the user; synthetic input does not reach the window)

1. `dotnet run -c Release --project src/Talt.Client` — the main menu appears (US1-1). Hover "Load
   game": disabled with a tooltip (US1-6).
2. New game → choose a size → Start: map with top bar and toolbar (US1-2). Click ×3 / press 1: the
   top bar follows (US1-3). Esc: pause menu, simulation paused (US1-5).
3. Hover terrain, a road and a vehicle at near, middle and far zoom: the right object is boxed
   (US2). Click each: an information window per object (US2-3, clarify Q5). Rotate/pan by dragging
   from the map and release over a window: no click (FR-012a).
4. Open many windows, drag, click behind, pin one, "close all" (US3). Tooltips on every top bar and
   toolbar button (US3-5).
5. Settings: switch to Spanish with windows open, change the currency to JPY, scale 75 % → 200 %,
   fullscreen, VSync (US4, US5). Restart: everything kept.
6. Key bindings: rebind pause to P, conflict prompt, restore defaults (US6).
7. UI cost: `dotnet run -c Release --project src/Talt.Client -- 256 50000 --stats` and the same with
   `--ui-test-windows 20`; compare frame time (SC-001) and check 60 FPS (SC-005).

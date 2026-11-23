# Quickstart: Construction and Terraforming Framework

## Automated

```
dotnet build
dotnet test --filter "Category!=Gpu"
dotnet test tests/Talt.Core.Tests --filter "FullyQualifiedName~Construction"
dotnet run --project benchmarks/Talt.Benchmarks -c Release -- --filter "*Construction*" --job short
pwsh ci/check-fingerprints.ps1        # existing goldens unchanged
```

Expected: all tests pass; previews of 500-tile drags < 1 ms and 0 B allocated; fingerprints match.

## Hand test (developer, in the window)

Start: `dotnet run --project src/Talt.Client -c Release -- --map 1024 --seed 11 --roughness 80 --max-height 64 --stats`

1. **Preview (US1)**: choose Raise (toolbar or PageUp) and hover flat land → green tile, cost by the
   cursor. Hover the sea → red, "Water". Confirm with a click → money in the top bar drops by the
   shown cost; the tile rises within a few frames.
2. **Terraform (US2)**: Level (L), drag a 25 × 20 rectangle over hills → the preview shows the
   rectangle plus cascaded neighbours; release → flat area at the start height; slopes around stay
   one step. Lower next to a lake → red "Would flood".
3. **Clear (US3)**: Clear (X) over a forest → trees vanish, cost = trees × clear cost. Over a road of
   the prototype grid → red "Owned by someone else".
4. **Drag and cancel (US4)**: start a drag, press Esc or the right button before releasing → nothing
   built. Drag over 500+ tiles with `--stats` → 60 FPS, 0 B allocated per frame.
5. **Undo (US5)**: right after an action press Backspace → terrain and money back. Wait more than
   10 s at normal speed → "Can no longer be undone".
6. **Paused**: pause (Space), terraform → applied immediately, clock does not move.
7. **Money**: spend until the balance is low, try a large level → red "Not enough money".
8. **Replay**: run with `--record session.json`, do some actions, quit, then
   `dotnet run --project tools/Talt.Headless -c Release -- replay session.json` → every checkpoint OK.

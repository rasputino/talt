# Quickstart: Economy and finance

## Automated checks

```
dotnet build
dotnet test --filter "Category!=Gpu"
dotnet test tests/Talt.Core.Tests --filter "FullyQualifiedName~Finance"
pwsh ci/check-fingerprints.ps1
dotnet run --project benchmarks/Talt.Benchmarks -c Release -- --filter "*Finance*" --job short
```

Expected: all tests pass; map-less goldens unchanged; finance benchmarks within SC-003 and 0 B.

## Headless

```
dotnet run --project tools/Talt.Headless -c Release -- run --map 512x512 --seed 7 --ticks 20000 --seed-stations 40 --station-service 60 --station-service-pays
```

Expected: the `finance:` line shows income > 0 and maintenance expenses; the same command twice gives the
same fingerprint.

## Client (by hand)

```
dotnet run --project src/Talt.Client -c Release -- --map 1024x1024 --seed 7 --seed-stations 150 --station-service 60 --station-service-pays
```

1. HUD money shows 500 000 in the chosen currency; clicking it opens the finance window.
2. Borrow / repay: cash and loan change by 10 000; at 500 000 borrowing is refused with a message.
3. Floating "+$…" texts rise above stations every few days; the window's income rows grow.
4. At a month end: property maintenance and (with a loan) interest appear; the bar chart gains a month.
5. `--sandbox` vs default: with a forced debt (borrow max and build until negative) the warning appears
   after 3 months and the game-over screen after 6 unless sandbox.
6. 4 096 map: 60 FPS with the window open (`--stats`).

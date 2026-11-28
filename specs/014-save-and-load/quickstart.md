# Quickstart: verifying save and load (feature 014)

Prerequisites: .NET 10 SDK; run from the repository root. Contracts: [save-api.md](contracts/save-api.md).

## 1. Tests

```
dotnet build
dotnet test --filter "Category!=Gpu"
dotnet test tests/Talt.Core.Tests --filter "FullyQualifiedName~Saves"
```

Expected: round-trip tests (fingerprint equal at load, identical re-save, identical continuation over
10 000 ticks, parallel = sequential), corruption tests, fixture/migration tests, field manifest test,
allocation tests and all existing golden fingerprint tests pass unchanged.

## 2. Headless round trip

```
dotnet run --project tools/Talt.Headless -c Release -- run --map 256 --seed 7 --ticks 6000 --checkpoint-every 1000 --save a.talt > run.txt
dotnet run --project tools/Talt.Headless -c Release -- run --map 256 --seed 7 --ticks 16000 --checkpoint-every 1000 > long.txt
dotnet run --project tools/Talt.Headless -c Release -- load a.talt --ticks 10000 --checkpoint-every 1000 > load.txt
```

Expected: the `loaded` fingerprint equals the tick-6000 line of `run.txt`, and every checkpoint in
`load.txt` equals the same tick in `long.txt`.

## 3. Cross-platform gate

```
pwsh ci/check-fingerprints.ps1
```

Expected: includes loading `tests/Talt.Core.Tests/Saves/Fixtures/rich-v1.talt` and matching the
committed fingerprints on Windows, Linux and macOS.

## 4. Benchmarks

```
dotnet run --project benchmarks/Talt.Benchmarks -c Release -- --filter "*SaveLoad*"
```

Expected (reference machine): capture ≤ 100 ms, capture + write ≤ 3 s, load ≤ 5 s, capture 0 B
allocated once warm. Numbers recorded in `docs/ARCHITECTURE.md`, "Save and load (feature 014)".

## 5. Hand test in the client (developer)

```
dotnet run --project src/Talt.Client -c Release
```

1. New game; build a road, a station pair, a bus line, a short railway with signals and a train; take a
   loan. Esc → Save → name "Prueba" → saved message.
2. Let it run a little; note date and money. Esc → Load → "Prueba" (confirm unsaved progress) →
   game resumes paused at the saved date with the same money, vehicles and trains in place; camera
   where it was.
3. F5 → "Quick save" message; play; F9 → confirmation → back to the quick save.
4. Settings → autosave 5 minutes; play 5+ minutes unpaused → autosave appears in the load list; no
   visible hitch.
5. Main menu → Load game: rows show name, save time, in-game date, company, money, map size and a map
   thumbnail; delete a save with confirmation.
6. Corrupt a save (truncate the file in the saves folder) → load list marks it damaged; loading it shows
   a clear message and the game keeps running.
7. Switch language to Spanish and repeat one save and one load to check texts.

Saves folder: `%APPDATA%\TALT\saves` (Windows), `~/.config/TALT/saves` (Linux),
`~/Library/Application Support/TALT/saves` (macOS) — wherever `ApplicationData` resolves.

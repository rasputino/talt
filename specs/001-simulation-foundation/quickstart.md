# Quickstart: Validating the Simulation Foundation

How to prove the feature works end to end. See [spec.md](spec.md) for requirements,
[data-model.md](data-model.md) and [contracts/](contracts/) for the interfaces referenced here.

## Prerequisites

- .NET 10 SDK; repository built in Release: `dotnet build -c Release`.
- For visual checks: a GPU with Vulkan, Metal or DirectX 12.

## 1. Automated tests (all platforms)

```
dotnet test -c Release
```

Expected: all tests pass, including:
- calendar tests (start year, leap years, month/year rollover, 1 000-year run),
- runner tests (standard speeds, pause, fast forward locked by default, unlock/lock, toggle restores
  the previous speed, catch-up cap, fast-forward budget, interpolation factor),
- command tests (add/remove vehicle, rejection without side effects, ordering),
- determinism tests (two replays identical for 10 000 ticks; sequential = parallel),
- golden fingerprint tests (must match on Windows, Linux and macOS),
- guard test: no floating-point fields in simulation state.
- `tests/Talt.Client.Tests` (every string key exists in English and Spanish) and
  `tests/Talt.Headless.Tests` (CLI argument parsing).

## 2. Headless runs (US4, SC-008)

```
dotnet run -c Release --project tools/Talt.Headless -- run --map 4096 --vehicles 50000 --ticks 10000
```

Expected: completes without a window in under 60 s; prints `date`, `fingerprint`, `avg-tick-ms`.
Run it twice: the `fingerprint` must be identical. Add `--sequential`: still identical.

Record and replay:

```
dotnet run -c Release --project tools/Talt.Headless -- run --ticks 10000 --record session.json
dotnet run -c Release --project tools/Talt.Headless -- replay session.json --checkpoint-every 1000
```

Expected: exit code 0 and every checkpoint `OK`.

## 3. Cross-platform check (FR-013, SC-002)

Run step 1 (golden tests) and the `replay` command on Linux and macOS at least once (done by CI since
feature 002, which runs them on every push). Fingerprints must equal the Windows values.

## 4. In the game (US1, US2, US3)

```
dotnet run -c Release --project src/Talt.Client -- 16 20000
```

Check:
- The date starts on 1 January 1950 and advances ~30 days per minute (one day ≈ 2 s).
- Pause (Space): date and vehicles freeze; the camera still moves; resuming causes no jump.
- Standard speeds (keys to be listed in ARCHITECTURE.md): ×1, ×3, ×8; the date speeds up
  accordingly; vehicles move smoothly at every speed.
- Fast forward: with default settings its key does nothing. Unlock it in the settings (launch option
  or settings file): its separate key toggles fast forward on and off, returning to the previous
  speed; fast forward keeps ≥ 30 FPS. Lock it again: the game returns to the previous speed.
- Add a vehicle to a line and remove a vehicle with their shortcuts; the change appears on the next
  tick.
- Drag the window for a second: the game does not freeze afterwards and resumes smoothly.
- Start with `--language es`: date (for example `1 ene 1950`), speed and command messages appear
  in Spanish; with `--language en` they appear in English.

Reference load check (SC-003): start with a 4096 map (`-- 128 50000`), unlock and switch to fast forward and
confirm the title shows ≥ ×20 effective speed and ≥ 30 FPS.

## 5. Benchmarks

```
dotnet run -c Release --project benchmarks/Talt.Benchmarks -- --filter *SimulationTick* --job short
```

Expected: full tick for 50 000 vehicles well below 2.5 ms (so ×20 fits in a frame); results and the
SoA vs Arch decision recorded in `docs/ARCHITECTURE.md`.

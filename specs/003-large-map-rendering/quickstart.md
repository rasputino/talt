# Quickstart: Validating Large-Map Rendering

Prerequisites: .NET 10 SDK, the reference machine for performance numbers (integrated AMD Radeon,
Vulkan). Commands run from the repository root.

## 1. Automated checks

```powershell
dotnet build -c Release
dotnet test -c Release
```

Expected: all tests pass, including greedy-vs-naive surface equality, reference-mesher equality,
no bottom faces, LOD road continuity, skirts, touched-chunk sets, frustum and LOD selection, and the
unchanged golden fingerprints.

## 2. Meshing benchmarks

```powershell
dotnet run -c Release --project benchmarks/Talt.Benchmarks -- --filter *ChunkMeshing*
```

Expected: greedy per chunk no slower than ~2 × the reference naive mesher, with far fewer quads;
record the table in `docs/ARCHITECTURE.md`.

## 3. Window checks (by hand; synthetic key presses do not reach the window)

For each map: `-- 16 50000` (512), `-- 64 50000` (2048), `-- 128 50000` (4096):

```powershell
dotnet run --project src/Talt.Client -c Release -- 128 50000
```

1. The window opens within ~10 s; the map fills in nearest-first within a few seconds.
2. Press F3; read average/worst frame time, draw calls, triangles and world GPU memory.
3. Pan, rotate and zoom continuously for a minute from closest to farthest zoom; the farthest zoom
   shows the whole map. Expected: 60 FPS, worst frame ≤ 33 ms, world memory < 2 GB.
4. Look at LOD boundaries at medium zoom: no sky-coloured cracks; roads continuous at all distances.
5. Press PageUp / PageDown (and with Shift). Expected console line: edit visible in < 100 ms.
6. Fast forward (feature 001 SC-003): `-- 128 50000 --unlock-fast-forward`, press F: effective speed
   ≥ ×20 at ≥ 30 FPS.

Record all numbers in `docs/ARCHITECTURE.md` ("Rendering performance").

# Contract: supported `.vox` subset

Reader: `Talt.Core.Meshing.Models.VoxReader.Read(ReadOnlySpan<byte> data, string name)` → `VoxModel`
(first model) or `ReadAll` → all models. Errors throw `VoxFormatException` naming the file and the
problem. Written from the public MagicaVoxel file format description; no third-party code.

## Layout (little-endian)

```text
"VOX " | int32 version (150 or 200 accepted; others rejected)
chunk  = id[4] | int32 contentBytes | int32 childrenBytes | content | children
MAIN   : no content; children:
  PACK : int32 modelCount                                  (optional)
  SIZE : int32 x, int32 y, int32 z                         (1–256 each; z is up in the file)
  XYZI : int32 n, then n × (byte x, y, z, colorIndex)      (follows its SIZE)
  RGBA : 256 × (byte r, g, b, a); file entry i is colour index i + 1
  other chunks (nTRN, nGRP, nSHP, MATL, LAYR, rOBJ, rCAM, NOTE, IMAP, unknown): skipped by size
```

## Validation

- Magic, version, `MAIN` first; every chunk's content and children inside its parent.
- `SIZE` axes 1–256; `XYZI` count ≤ x·y·z and ≤ remaining bytes / 4; each voxel inside its size;
  colour index 1–255; each `SIZE` followed by one `XYZI`.
- At least one model with at least one voxel; an `RGBA` chunk present.
- Maximum file size 8 MiB.

## Axes

File: right-handed, Z up. Game: right-handed, Y up. `game = (x, z, sizeY − 1 − y)`; sizes
`(sizeX, sizeZ, sizeY)`. A model's front (seen from −Y in the editor) faces +Z in the game; yaw 0
means "front towards +Z".

## Writer

`VoxWriter.Write(VoxModel)` emits `VOX ` 150, `MAIN` with one `SIZE`, `XYZI` and `RGBA`, converting
back from game axes. `Read(Write(m))` equals `m`.

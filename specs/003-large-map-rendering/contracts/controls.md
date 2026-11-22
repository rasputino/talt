# Contract: Player-visible controls and output

| Input | Effect |
|---|---|
| F3 | Toggle the window title between game status and render statistics; while statistics are shown they are also printed to the console once per second |
| PageUp | Raise the tile under the camera target by one voxel (debug terrain edit) |
| PageDown | Lower the tile under the camera target by one voxel (never below height 1) |
| Shift + PageUp / PageDown | Same for a 32 × 32 area centred on the target |
| Mouse wheel | Zoom; the farthest zoom now shows the whole map |

Statistics line (localized, en/es): frame time average / worst (ms), FPS, draw calls, triangles,
visible regions per LOD (0/1/2/3), world GPU memory (MB), pending mesh jobs.

Console after each debug edit: `Edit visible after {ms} ms ({chunks} chunks rebuilt).`

Command line unchanged: `Talt.Client [chunks-per-side] [vehicles] [seed] [options]`.

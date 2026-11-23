# Contract: command lines

## Client (`src/Talt.Client/Program.cs`)

Existing positional form keeps working: `Talt.Client [chunks-per-side] [vehicles] [seed]`.
New options (all optional, values validated by `MapSettings.Validate`, errors printed and exit code 2):

| Option | Meaning | Default |
|--------|---------|---------|
| `--map <N>` or `--map <W>x<D>` | map size in tiles (multiples of 32, 256 to 16 384); `N` = square | from chunks-per-side, else 512 (16 chunks) |
| `--seed <long>` | seed | random (printed) |
| `--roughness <0-100>` | detail and relief variation | 50 |
| `--max-height <8-127>` | highest terrain | 28 |
| `--water <0-90>` | percent of the map under the sea | 25 |
| `--sea-level <1..>` | sea surface height in voxels | 7 |
| `--map-text <text>` | all of the above as one shareable text; individual options after it override it | |

Output on the console (texts localized en/es through `Strings`):

```text
Map settings: T1-1024x512-s2026-r50-h28-w25-l7
[  0%] Terrain
[ 21%] Slopes
...
[100%] Sites
Generated 1024x512 map: 3 rivers, 12 town sites, 19 industry sites in 1 840 ms.
```

The existing "Created simulation" and "Generated terrain" lines stay. Non-square maps: the road grid
scenario uses the largest multiple of 32 ≤ min(width, depth).

## Headless (`tools/Talt.Headless`)

```text
Talt.Headless map [--map <WxD|N>] [--seed <long>] [--roughness n] [--max-height n] [--water n]
                  [--sea-level n] [--map-text <settings text>] [--threads <n>] [--build-world]
```

Prints the settings text, per-stage timing, counts (rivers, lakes, sites) and
`Fingerprint: <16 hex digits>`; `--build-world` also builds the voxel world and prints its time and
chunk count. Exit code 0 on success, 2 on invalid arguments. The golden fingerprints of the test suite
(`MapGoldenTests`) run on every CI platform.

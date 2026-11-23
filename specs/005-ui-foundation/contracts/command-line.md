# Contract: client command line

Existing options keep their meaning (`[chunks-per-side] [vehicles] [seed]`, `--start-year`,
`--language`, `--unlock-fast-forward`, `--lock-fast-forward`, `--record`, `--print-fingerprint-at`,
`--exit-after-fingerprint`, `--stats`, `--zoom`).

Changes:

| Option | Meaning |
|---|---|
| (no game arguments) | open on the main menu |
| any positional argument, `--record`, `--print-fingerprint-at` or `--play` | skip the menu and start a game straight away (smoke test unchanged) |
| `--play` | start a game with the default options without showing the menu |
| `--language en\|es` | overrides the saved language for this run (not saved) |
| `--ui-scale <0.75–2>` | overrides the interface scale for this run (not saved) |
| `--ui-test-windows <n>` | development: opens n sample windows in game to measure UI cost |

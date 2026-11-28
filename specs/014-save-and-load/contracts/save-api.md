# Contract: save and load API (Talt.Core.Saves) and tools

## Core

```csharp
namespace Talt.Core.Saves;

public static class SaveGame
{
    /// Main thread, at a tick boundary. Copies the state into pooled buffers (≤ 100 ms in the
    /// reference scenario, no allocations once warm).
    public static SaveSnapshot Capture(GameSimulation simulation, SaveDetailsInput details,
                                       IReadOnlyList<ExtraSection>? extraSections = null);

    /// Any thread. Compresses, checksums and atomically writes the snapshot, leaving disposal to the caller.
    public static Task<SaveWriteResult> WriteAsync(SaveSnapshot snapshot, string path,
                                                   CancellationToken cancellationToken = default);

    /// Capture + write synchronously (headless tool, tests).
    public static SaveWriteResult Save(GameSimulation simulation, string path, SaveDetailsInput details);

    /// Reads header, details and thumbnail only (load screen). Never throws for bad files.
    public static SavePeek Peek(string path);

    /// Reads, verifies, decompresses and restores. Never throws for bad files (status instead).
    public static SaveLoadResult Load(string path, SaveLoadOptions? options = null,
                                      IProgress<float>? progress = null);

    public static SaveLoadResult Load(Stream stream, SaveLoadOptions? options = null,
                                      IProgress<float>? progress = null);
}

public readonly record struct SaveDetailsInput(string Name, SaveKind Kind, DateTimeOffset SavedAtUtc, string GameVersion);
public sealed record SaveLoadOptions(GameContent? Content = null, ConstructionRules? Rules = null);
public readonly record struct ExtraSection(SaveSectionId Id, int Version, ReadOnlyMemory<byte> Payload);
```

- `SaveSnapshot` is `IDisposable`; the caller retains ownership through completion of `WriteAsync` and may then reuse or dispose it.
- `SavePeek`: `Status` (`Ok`, `Damaged`, `TooNew`, `NotASave`, `IoError`), `Details?`, thumbnail pixels.
- `SaveLoadResult`: see [data-model.md](../data-model.md).
- The simulation is never modified by `Capture` (SC-007, tested by fingerprint before/after).

## Client

- `SaveStore` (client): the saves folder, safe file names, listing (`Peek` per file, newest first),
  delete, autosave rotation, quick save path.
- `AutosaveScheduler`: `Update(double elapsedSeconds, bool paused)` → `bool due`; allocation-free.
- `GameSession`: `HasUnsavedProgress`, `CaptureSave(kind, name)`, camera state for `CAMR`.
- Screens: `SaveScreen` (name field, list of existing saves for overwrite), `LoadScreen` (list with
  thumbnails, details, delete), confirmation dialog; pause menu gains "Save" and "Load"; the main
  menu's "Load game" button is enabled.
- Key bindings (data-driven, `Input/`): `quickSave` = F5, `quickLoad` = F9.
- Settings screen: autosave interval (off/5/10/15/30 min) and autosaves kept.
- Command line: `--load <file>` starts straight into a save (verification and hand tests).

## Headless tool

```
dotnet run --project tools/Talt.Headless -c Release -- run --map 256 --ticks 4000 --save game.talt
dotnet run --project tools/Talt.Headless -c Release -- load game.talt --ticks 10000 --checkpoint-every 1000 [--save again.talt] [--sequential]
```

`load` prints `loaded tick=<t> fingerprint=<hex>` and one `tick=<t> fingerprint=<hex>` per checkpoint,
in the same format as `run`, so outputs can be compared line by line. Exit code 0 on success, 2 for a
refused or damaged save (message on stderr).

## Localisation keys (both `Strings.en.json` and `Strings.es.json`)

`pause.save`, `pause.load`, `menu.loadGame` (enabled), `save.title`, `save.name`, `save.confirm`,
`save.overwrite.title`, `save.overwrite.body`, `save.done`, `save.failed`, `save.quick.done`,
`load.title`, `load.empty`, `load.column.*`, `load.autosave`, `load.quicksave`, `load.damaged`,
`load.delete`, `load.delete.confirm`, `load.progress`, `load.failed.damaged`, `load.failed.tooNew`,
`load.failed.missingContent`, `load.failed.io`, `load.warning.changedData`, `load.quick.none`,
`unsaved.title`, `unsaved.body`, `settings.autosave`, `settings.autosave.off`,
`settings.autosave.minutes`, `settings.autosaveKeep`, `keys.quickSave`, `keys.quickLoad`.

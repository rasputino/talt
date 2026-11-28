using System.Text;
using Talt.Core.Saves;

namespace Talt.Client.Saves;

/// <summary>One row of the load screen (data-model.md SaveCatalogEntry).</summary>
internal sealed record SaveCatalogEntry(string Path, SaveDetails? Details, SavePeekStatus Status, DateTime FileTimeUtc)
{
    /// <summary>The file could not be read as a save (shown as damaged, can be deleted).</summary>
    public bool IsDamaged => Status != SavePeekStatus.Ok;

    /// <summary>The name to show: the saved name, or the file name for damaged files.</summary>
    public string DisplayName => Details?.Name ?? System.IO.Path.GetFileNameWithoutExtension(Path);

    /// <summary>When it was saved, for sorting (newest first).</summary>
    public DateTimeOffset SortTime => Details?.SavedAtUtc ?? new DateTimeOffset(FileTimeUtc, TimeSpan.Zero);
}

/// <summary>
/// The saves folder of the player (research R15): <c>&lt;ApplicationData&gt;/TALT/saves</c>, one <c>.talt</c> file per save,
/// names made safe for every platform, autosaves rotated as <c>autosave-N.talt</c>, one <c>quicksave.talt</c>.
/// </summary>
internal sealed class SaveStore
{
    public const string Extension = ".talt";
    public const string QuickSaveFile = "quicksave";
    private const int MaxFileName = 80;

    private static readonly HashSet<string> ReservedWindowsNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    public SaveStore(string folder)
    {
        Folder = folder ?? throw new ArgumentNullException(nameof(folder));
    }

    /// <summary>The default folder next to the settings file.</summary>
    public static string DefaultFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TALT", "saves");

    public string Folder { get; }

    public string QuickSavePath => Path.Combine(Folder, QuickSaveFile + Extension);

    /// <summary>
    /// A file name for a save name: letters, digits, spaces, '-', '_' and '.' kept, anything else replaced by '_', trimmed,
    /// at most 80 characters, never a reserved Windows device name or a name starting with "autosave-" or "quicksave".
    /// </summary>
    public static string SafeFileName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var text = new StringBuilder(name.Length);
        foreach (var c in name.Trim())
        {
            text.Append(char.IsLetterOrDigit(c) || c is ' ' or '-' or '_' or '.' ? c : '_');
        }

        var safe = text.ToString().Trim(' ', '.');
        if (safe.Length > MaxFileName)
        {
            safe = safe[..MaxFileName].TrimEnd(' ', '.');
        }

        if (safe.Length == 0 || ReservedWindowsNames.Contains(safe) || ReservedWindowsNames.Contains(safe.Split('.')[0]))
        {
            safe = "_" + safe;
        }

        if (safe.StartsWith("autosave-", StringComparison.OrdinalIgnoreCase) || safe.Equals(QuickSaveFile, StringComparison.OrdinalIgnoreCase))
        {
            safe = "_" + safe;
        }

        return safe;
    }

    /// <summary>The path a manual save with this name is written to.</summary>
    public string PathFor(string name) => Path.Combine(Folder, SafeFileName(name) + Extension);

    /// <summary>
    /// The existing save a new save named <paramref name="name"/> would replace: same file name compared without case
    /// (so a save never appears twice on case-insensitive file systems), or null.
    /// </summary>
    public string? Existing(string name)
    {
        var target = SafeFileName(name) + Extension;
        if (!Directory.Exists(Folder))
        {
            return null;
        }

        foreach (var file in Directory.EnumerateFiles(Folder, "*" + Extension))
        {
            if (string.Equals(Path.GetFileName(file), target, StringComparison.OrdinalIgnoreCase))
            {
                return file;
            }
        }

        return null;
    }

    /// <summary>Every save in the folder with its details (peek only, FR-016), newest first.</summary>
    public IReadOnlyList<SaveCatalogEntry> List()
    {
        if (!Directory.Exists(Folder))
        {
            return [];
        }

        var files = new DirectoryInfo(Folder).GetFiles("*" + Extension);
        var entries = new SaveCatalogEntry[files.Length];
        Parallel.For(0, files.Length, new ParallelOptions { MaxDegreeOfParallelism = 8 }, i =>
        {
            var file = files[i];
            var peek = SaveGame.Peek(file.FullName, includeThumbnail: false);
            entries[i] = new SaveCatalogEntry(file.FullName, peek.Details, peek.Status, file.LastWriteTimeUtc);
        });

        Array.Sort(entries, (a, b) =>
        {
            var time = b.SortTime.CompareTo(a.SortTime);
            return time != 0 ? time : StringComparer.Ordinal.Compare(a.Path, b.Path);
        });
        return entries;
    }

    /// <summary>Deletes a save; false when it could not be deleted.</summary>
    public static bool Delete(string path)
    {
        try
        {
            File.Delete(path);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Chooses a free slot or the oldest autosave. Pruning waits for a successful replacement.</summary>
    public string NextAutosavePath(int keep)
    {
        var slots = AutosaveSlots();
        if (slots.Count >= Math.Clamp(keep, 1, 10))
        {
            return slots[0].Path;
        }

        for (var slot = 1; slot <= 10; slot++)
        {
            var path = Path.Combine(Folder, $"autosave-{slot}{Extension}");
            if (!File.Exists(path))
            {
                return path;
            }
        }

        throw new IOException("No autosave slot is available.");
    }

    public void PruneAutosaves(int keep)
    {
        var slots = AutosaveSlots();
        for (var i = 0; i < slots.Count - Math.Clamp(keep, 1, 10); i++)
        {
            Delete(slots[i].Path);
        }
    }

    private List<(string Path, DateTimeOffset Time)> AutosaveSlots()
    {
        var slots = new List<(string Path, DateTimeOffset Time)>();
        for (var slot = 1; slot <= 10; slot++)
        {
            var path = Path.Combine(Folder, $"autosave-{slot}{Extension}");
            if (File.Exists(path))
            {
                var peek = SaveGame.Peek(path, includeThumbnail: false);
                if (peek.Details is null || peek.Details.Kind == SaveKind.Autosave)
                {
                    slots.Add((path, peek.Details?.SavedAtUtc ?? DateTimeOffset.MinValue));
                }
            }
        }

        slots.Sort((a, b) => a.Time.CompareTo(b.Time));
        return slots;
    }
}

namespace Talt.Core.Saves;

/// <summary>Kind of save (data-model.md SaveKind).</summary>
public enum SaveKind
{
    /// <summary>Saved by the player under a name of their choice.</summary>
    Manual = 0,

    /// <summary>Written automatically at the autosave interval.</summary>
    Autosave = 1,

    /// <summary>The single quick save slot (F5).</summary>
    QuickSave = 2,
}

/// <summary>
/// Four ASCII characters identifying a save section ("CORE", "MAP_"…), packed into a <see cref="uint"/> with the
/// first character in the low byte (contracts/save-format.md, contracts/save-sections.md).
/// </summary>
public readonly record struct SaveSectionId(uint Value)
{
    /// <summary>Packs a four-character id; every character must be printable ASCII.</summary>
    public static SaveSectionId Of(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length != 4)
        {
            throw new ArgumentException("A section id has exactly four characters.", nameof(text));
        }

        uint value = 0;
        for (var i = 0; i < 4; i++)
        {
            var c = text[i];
            if (c < 0x21 || c > 0x7E)
            {
                throw new ArgumentException("A section id uses printable ASCII characters.", nameof(text));
            }

            value |= (uint)c << (8 * i);
        }

        return new SaveSectionId(value);
    }

    public override string ToString()
    {
        Span<char> chars = stackalloc char[4];
        for (var i = 0; i < 4; i++)
        {
            var b = (byte)(Value >> (8 * i));
            chars[i] = b is >= 0x21 and <= 0x7E ? (char)b : '?';
        }

        return new string(chars);
    }
}

/// <summary>Why a file could not be read as a save.</summary>
public enum SaveFailure
{
    /// <summary>A save, but damaged, truncated or inconsistent.</summary>
    Damaged = 0,

    /// <summary>Not a save file at all (wrong magic, too short).</summary>
    NotASave = 1,

    /// <summary>Written by a newer version: container, section version, section id or codec unknown here.</summary>
    TooNew = 2,
}

/// <summary>A save file or section that cannot be read (FR-019); loading turns it into a status, never a crash.</summary>
public sealed class SaveFormatException : Exception
{
    public SaveFormatException()
    {
    }

    public SaveFormatException(string message)
        : base(message)
    {
    }

    public SaveFormatException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public SaveFormatException(SaveFailure failure, string message)
        : base(message)
    {
        Failure = failure;
    }

    /// <summary>The kind of failure; <see cref="SaveFailure.Damaged"/> unless stated otherwise.</summary>
    public SaveFailure Failure { get; }
}

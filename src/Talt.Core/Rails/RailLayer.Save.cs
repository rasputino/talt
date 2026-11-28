using Talt.Core.Saves;

namespace Talt.Core.Rails;

public sealed partial class RailLayer
{
    /// <summary>
    /// The <c>RAIL</c> section's track layers (feature 014). Games that never had track write only the flag, like the
    /// fingerprint (research R15 of feature 015).
    /// </summary>
    internal void WriteState(SaveWriter writer)
    {
        writer.WriteBool(EverUsed);
        writer.WriteInt(Version);
        writer.WriteInt(RailTileCount);
        writer.WriteList(_changed);
        if (!EverUsed)
        {
            return;
        }

        writer.WritePages(_links, (byte)0);
        writer.WritePages(_types, (byte)0);
        writer.WritePages(_flags, (byte)0);
        writer.WritePages(_spans, (byte)0);
        writer.WritePages(_longDirs, (byte)0);
        writer.WritePages(_signals, (byte)0);
    }

    internal void ReadState(SaveReader reader, int version)
    {
        EverUsed = reader.ReadBool();
        Version = reader.ReadInt();
        RailTileCount = reader.ReadInt(0, _links.Length);
        reader.ReadListInto(_changed);
        if (!EverUsed)
        {
            return;
        }

        reader.ReadPages(_links, (byte)0, version);
        reader.ReadPages(_types, (byte)0, version);
        reader.Content.RemapPieces(ContentTable.RailType, _types);
        reader.ReadPages(_flags, (byte)0, version);
        reader.ReadPages(_spans, (byte)0, version);
        reader.ReadPages(_longDirs, (byte)0, version);
        reader.ReadPages(_signals, (byte)0, version);
    }
}

public sealed partial class RailBlocks
{
    /// <summary>
    /// Signal blocks verbatim (feature 014): a rebuild after loading would also recount block ownership, so the blocks of
    /// a game with track are saved rather than derived.
    /// </summary>
    internal void WriteState(SaveWriter writer, bool everUsed)
    {
        writer.WriteInt(_version);
        writer.WriteInt(Count);
        writer.WriteArray(_queue);
        if (everUsed)
        {
            writer.WritePages(_block, -1);
        }
    }

    internal void ReadState(SaveReader reader, bool everUsed, int version)
    {
        _version = reader.ReadInt();
        Count = reader.ReadInt();
        _queue = reader.ReadRequiredArray<int>();
        if (everUsed)
        {
            reader.ReadPages(_block, -1, version);
        }
    }
}

public sealed partial class RailDepotRegistry
{
    internal void WriteState(SaveWriter writer)
    {
        writer.WriteInt(_nextId);
        writer.WriteInt(Version);
        writer.WriteList(_depots);
        writer.WriteDictionary(_indexByTile);
    }

    internal void ReadState(SaveReader reader)
    {
        _nextId = reader.ReadInt();
        Version = reader.ReadInt();
        reader.ReadListInto(_depots);
        reader.ReadDictionaryInto(_indexByTile);
    }
}

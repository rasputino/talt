using Talt.Core.Saves;

namespace Talt.Core.Roads;

public sealed partial class RoadLayer
{
    /// <summary>The road tile layers, the road tile count and the pending changed tiles (feature 014, <c>ROAD</c>).</summary>
    internal void WriteState(SaveWriter writer)
    {
        writer.WriteArray(_links);
        writer.WriteArray(_exits);
        writer.WriteArray(_types);
        writer.WriteArray(_flags);
        writer.WriteArray(_spans);
        writer.WriteArray(_longDirs);
        writer.WriteInt(RoadTileCount);
        writer.WriteList(_changed);
    }

    internal void ReadState(SaveReader reader)
    {
        reader.ReadInto<byte>(_links);
        reader.ReadInto<byte>(_exits);
        reader.ReadInto<byte>(_types);
        reader.Content.RemapPieces(ContentTable.RoadType, _types);
        reader.ReadInto<byte>(_flags);
        reader.ReadInto<byte>(_spans);
        reader.ReadInto<byte>(_longDirs);
        RoadTileCount = reader.ReadInt(0, _links.Length);
        reader.ReadListInto(_changed);
    }
}

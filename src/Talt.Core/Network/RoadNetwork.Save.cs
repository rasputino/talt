using Talt.Core.Saves;

namespace Talt.Core.Network;

public sealed partial class RoadNetwork
{
    /// <summary>
    /// The road graph verbatim (feature 014, research R1): node numbering and free list depend on the history of edits
    /// and can decide ties in path searches, so the graph is saved instead of rebuilt from the road layer.
    /// </summary>
    internal void WriteState(SaveWriter writer)
    {
        writer.WriteInt(_regions.Length);
        foreach (var region in _regions)
        {
            writer.WriteBool(region is not null);
            if (region is null)
            {
                continue;
            }

            writer.WriteArray(region.Nodes);
            writer.WriteInt(region.NodeCount);
            writer.WriteArray(region.EdgeTo);
            writer.WriteArray(region.EdgeLength);
            writer.WriteArray(region.EdgeSpeed);
            writer.WriteArray(region.EdgeFirstDir);
            writer.WriteInt(region.EdgeCount);
        }

        writer.WriteDictionary(_nodeOfTile);
        writer.WriteArray(_dirty);
        writer.WriteList(_dirtyRegions);
        writer.WriteArray(_nodeTile);
        writer.WriteArray(_nodeRegion);
        writer.WriteArray(_nodeFirst);
        writer.WriteArray(_nodeCount);
        writer.WriteArray(_free);
        writer.WriteInt(_freeCount);
        writer.WriteInt(_nodeHigh);
        writer.WriteInt(Version);
        writer.WriteInt(NodeCount);
        writer.WriteInt(EdgeCount);
        writer.WriteInt(LastRebuiltRegions);
    }

    internal void ReadState(SaveReader reader)
    {
        if (reader.ReadInt() != _regions.Length)
        {
            throw reader.Fail("road graph region count differs from the map's");
        }

        for (var i = 0; i < _regions.Length; i++)
        {
            if (!reader.ReadBool())
            {
                _regions[i] = null;
                continue;
            }

            var region = new Region
            {
                Nodes = reader.ReadRequiredArray<int>(),
                NodeCount = reader.ReadInt(),
                EdgeTo = reader.ReadRequiredArray<int>(),
                EdgeLength = reader.ReadRequiredArray<int>(),
                EdgeSpeed = reader.ReadRequiredArray<int>(),
                EdgeFirstDir = reader.ReadRequiredArray<byte>(),
                EdgeCount = reader.ReadInt(),
            };
            if (region.NodeCount < 0 || region.NodeCount > region.Nodes.Length || region.EdgeCount < 0 || region.EdgeCount > region.EdgeTo.Length
                || region.EdgeLength.Length != region.EdgeTo.Length || region.EdgeSpeed.Length != region.EdgeTo.Length || region.EdgeFirstDir.Length != region.EdgeTo.Length)
            {
                throw reader.Fail("road graph region is inconsistent");
            }

            _regions[i] = region;
        }

        reader.ReadDictionaryInto(_nodeOfTile);
        reader.ReadInto<bool>(_dirty);
        reader.ReadListInto(_dirtyRegions);
        _nodeTile = reader.ReadRequiredArray<int>();
        _nodeRegion = reader.ReadRequiredArray<int>();
        _nodeFirst = reader.ReadRequiredArray<int>();
        _nodeCount = reader.ReadRequiredArray<int>();
        _free = reader.ReadRequiredArray<int>();
        _freeCount = reader.ReadInt(0, _free.Length);
        _nodeHigh = reader.ReadInt(0, _nodeTile.Length);
        Version = reader.ReadInt();
        NodeCount = reader.ReadInt();
        EdgeCount = reader.ReadInt();
        LastRebuiltRegions = reader.ReadInt();
    }
}

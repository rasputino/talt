using Talt.Core.Saves;

namespace Talt.Core.Maps;

public sealed partial class MapData
{
    /// <summary>
    /// The <c>MAP_</c> section (feature 014): settings text, statistics, sites and the five tile arrays, including every
    /// terraforming change. It is read before the game is constructed, because the empty game is built on this map.
    /// </summary>
    internal void WriteState(SaveWriter writer, string settingsText)
    {
        writer.WriteString(settingsText); // Settings.ToText(), cached per game so capture does not allocate.
        writer.Write(Statistics);
        var sites = Sites;
        writer.WriteInt(sites.Count);
        for (var i = 0; i < sites.Count; i++)
        {
            var site = sites[i];
            writer.WriteInt(site.X);
            writer.WriteInt(site.Z);
            // The v1 kind occupies four bytes. Clear the three former struct-padding bytes.
            writer.WriteInt((byte)site.Kind);
            writer.WriteInt(site.FreeRadius);
            writer.WriteInt(site.Score);
        }

        writer.WriteArray(_height);
        writer.WriteArray(_terrain);
        writer.WriteArray(_waterLevel);
        writer.WriteArray(_flags);
        writer.WriteArray(_scenery);
    }

    /// <summary>Reads a map written by <see cref="WriteState"/>; its settings must equal <paramref name="expected"/> (the scenario's).</summary>
    internal static MapData ReadState(SaveReader reader, MapSettings expected)
    {
        ArgumentNullException.ThrowIfNull(expected);
        var text = reader.ReadRequiredString();
        if (!MapSettings.TryParse(text, out var settings, out _) || settings != expected)
        {
            throw reader.Fail("map settings differ from the scenario's");
        }

        var statistics = reader.Read<MapStatistics>();
        var count = reader.ReadCount(20);
        var sites = new Site[count];
        for (var i = 0; i < count; i++)
        {
            var x = reader.ReadInt();
            var z = reader.ReadInt();
            var kind = (SiteKind)(reader.ReadInt() & 0xff); // Older saves can contain arbitrary high bytes.
            sites[i] = new Site(x, z, kind, reader.ReadInt(), reader.ReadInt());
        }

        var tiles = (long)expected.Width * expected.Depth;
        var height = ReadTiles(reader, tiles);
        var terrain = ReadTiles(reader, tiles);
        var water = ReadTiles(reader, tiles);
        var flags = ReadTiles(reader, tiles);
        var scenery = ReadTiles(reader, tiles);
        return new MapData(expected, height, terrain, water, flags, scenery, sites, statistics);
    }

    private static byte[] ReadTiles(SaveReader reader, long tiles)
    {
        var array = reader.ReadRequiredArray<byte>();
        if (array.Length != tiles)
        {
            throw reader.Fail($"a tile layer has {array.Length} tiles instead of {tiles}");
        }

        return array;
    }
}

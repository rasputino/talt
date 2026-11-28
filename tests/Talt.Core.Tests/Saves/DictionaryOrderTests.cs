using System.Reflection;

namespace Talt.Core.Tests.Saves;

public class DictionaryOrderTests
{
    [Fact]
    public void Ordered_work_queues_and_lookup_only_maps_keep_their_semantics()
    {
        var game = SaveTestGames.Rich();
        var supply = Field<long, (int, int)>(game.Stations!, "_pendingSupply");
        var requests = Field<int, (long, int)>(game.RoadVehicles!, "_requests");
        var holders = Field<int, int>(game.RoadVehicles!, "_holders");
        var centres = Field<int, int>(game.Stations!, "_removedCentre");
        AddWithRemoval(supply, 99L, 2L, 4L, (3, 1));
        AddWithRemoval(requests, 19, 2, 4, (3L, 1));
        AddWithRemoval(holders, 29, 2, 4, 17);
        AddWithRemoval(centres, 39, 2, 4, 18);
        var copy = RoundTrip.Load(RoundTrip.SaveBytes(game));
        var nextSupply = Field<long, (int, int)>(copy.Stations!, "_pendingSupply");
        var nextRequests = Field<int, (long, int)>(copy.RoadVehicles!, "_requests");
        supply[14] = nextSupply[14] = (4, 1);
        requests[24] = nextRequests[24] = (7, 2);
        Assert.Equal(supply.ToArray(), nextSupply.ToArray());
        Assert.Equal(requests.ToArray(), nextRequests.ToArray());
        // The simulation queries these by key. Their canonical serialized order ignores runtime free slots.
        Assert.Equal(holders.OrderBy(p => p.Key), Field<int, int>(copy.RoadVehicles!, "_holders").OrderBy(p => p.Key));
        Assert.Equal(centres.OrderBy(p => p.Key), Field<int, int>(copy.Stations!, "_removedCentre").OrderBy(p => p.Key));
    }

    private static void AddWithRemoval<TKey, TValue>(Dictionary<TKey, TValue> map, TKey first, TKey second, TKey third, TValue value)
        where TKey : notnull
    {
        map.Clear();
        map[first] = value;
        map[second] = value;
        map.Remove(first);
        map[third] = value;
    }

    private static Dictionary<TKey, TValue> Field<TKey, TValue>(object owner, string name) where TKey : notnull =>
        (Dictionary<TKey, TValue>)owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;
}

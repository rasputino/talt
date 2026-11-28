using Talt.Core.Roads;

namespace Talt.Core.Rails;

/// <summary>
/// Signal blocks (feature 015, research R5): components of track tiles joined by links that no signal edge cuts. A signal
/// facing travel <c>d</c> on tile X sits on X's edge towards <c>d</c>, so the link X → d is a boundary. Derived from the rail
/// layer (not hashed); rebuilt when the layer's version changes, before trains reserve.
/// </summary>
public sealed partial class RailBlocks
{
    private readonly RailLayer _rails;
    private readonly int[] _block;
    private int[] _queue = new int[1024];
    private int _version = -1;

    public RailBlocks(RailLayer rails)
    {
        _rails = rails ?? throw new ArgumentNullException(nameof(rails));
        _block = new int[rails.Width * rails.Depth];
        Array.Fill(_block, -1);
    }

    public int Count { get; private set; }

    /// <summary>Rebuilds after track or signal changes; true when it did.</summary>
    public bool Refresh()
    {
        if (_version == _rails.Version)
        {
            return false;
        }

        _version = _rails.Version;
        Array.Fill(_block, -1);
        Count = 0;
        var links = _rails.Links;
        for (var tile = 0; tile < _block.Length; tile++)
        {
            if (links[tile] != 0 && _block[tile] < 0)
            {
                Fill(tile, Count++);
            }
            else if (_rails.HasRail(tile) && _block[tile] < 0)
            {
                _block[tile] = Count++;
            }
        }

        return true;
    }

    /// <summary>The block of a tile, or −1 without track.</summary>
    public int BlockAt(int tile) => _block[tile];

    /// <summary>True when the link from <paramref name="tile"/> towards <paramref name="direction"/> crosses a signal edge.</summary>
    public bool IsBoundary(int tile, int direction)
    {
        var signal = _rails.SignalAt(tile);
        if (signal != 0 && RailSignal.Faces(signal, RailSignal.Axis(_rails.LinksAt(tile)), direction))
        {
            return true;
        }

        var other = _rails.FollowLink(tile, direction);
        var back = Dir8.Opposite(direction);
        var otherSignal = _rails.SignalAt(other);
        return otherSignal != 0 && RailSignal.Faces(otherSignal, RailSignal.Axis(_rails.LinksAt(other)), back);
    }

    private void Fill(int start, int id)
    {
        var head = 0;
        var tail = 0;
        Push(start, ref tail);
        _block[start] = id;
        while (head < tail)
        {
            var tile = _queue[head++];
            var links = _rails.LinksAt(tile);
            for (var d = 0; d < 8; d++)
            {
                if ((links & (1 << d)) == 0 || IsBoundary(tile, d))
                {
                    continue;
                }

                var next = _rails.FollowLink(tile, d);
                if (next >= 0 && _block[next] < 0)
                {
                    _block[next] = id;
                    Push(next, ref tail);
                }
            }
        }
    }

    private void Push(int tile, ref int tail)
    {
        if (tail == _queue.Length)
        {
            Array.Resize(ref _queue, tail * 2);
        }

        _queue[tail++] = tile;
    }
}

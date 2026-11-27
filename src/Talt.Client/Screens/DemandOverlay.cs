using System.Numerics;
using Talt.Client.Ui;
using Talt.Client.Ui.Widgets;
using Talt.Core.Demand;
using Talt.Core.Terrain;
using Talt.Core.Towns;

namespace Talt.Client.Screens;

/// <summary>
/// The unserved demand overlay (feature 013, US6, research R14): dotted lines between the town pairs with the largest
/// passenger and mail demand the player's network does not carry, more opaque and thicker for more demand. The pairs
/// are recomputed once per in-game day; projection runs every frame without allocating.
/// </summary>
internal sealed class DemandOverlay : Widget
{
    private const float AnchorHeight = 4f;
    private const float DotSpacing = 14f;

    private readonly TownRegistry _towns;
    private readonly DestinationTables _tables;
    private readonly Heightmap _ground;
    private readonly int _pairsShown;
    private readonly int _mailCargo;
    private readonly List<DemandPair> _pairs = [];
    private readonly List<DemandPair> _scratch = [];
    private readonly int[] _monthly;
    private readonly Vector2[] _screen;
    private readonly bool[] _visible;
    private long _day = -1;
    private int _largest = 1;

    public DemandOverlay(TownRegistry towns, DestinationTables tables, Heightmap ground, int mailCargo)
    {
        _mailCargo = mailCargo;
        _towns = towns ?? throw new ArgumentNullException(nameof(towns));
        _tables = tables ?? throw new ArgumentNullException(nameof(tables));
        _ground = ground ?? throw new ArgumentNullException(nameof(ground));
        _pairsShown = tables.Rules.OverlayPairs;
        _monthly = new int[towns.Count];
        _screen = new Vector2[towns.Count];
        _visible = new bool[towns.Count];
        Visible = false;
    }

    /// <summary>Pairs drawn now.</summary>
    public IReadOnlyList<DemandPair> Pairs => _pairs;

    /// <summary>Refreshes the pairs on a new day and projects the towns for this frame.</summary>
    public void Update(long day, Matrix4x4 viewProjection, Vector2 viewport)
    {
        if (!Visible)
        {
            return;
        }

        if (day != _day)
        {
            _day = day;
            RefreshPairs();
        }

        for (var town = 0; town < _towns.Count; town++)
        {
            var (cx, cz) = _towns.CentreOf(town);
            var world = new Vector3(cx + 0.5f, _ground.Get(cx, cz) + AnchorHeight, cz + 0.5f);
            _visible[town] = TownLabels.TryProject(viewProjection, viewport, world, out _screen[town]);
        }
    }

    /// <summary>Sums passengers and mail per pair, largest first.</summary>
    private void RefreshPairs()
    {
        _pairs.Clear();
        var rules = _tables.Rules;
        for (var r = 0; r < rules.Cargo.Count; r++)
        {
            if (rules.Cargo[r].Source != DemandSource.Buildings)
            {
                continue;
            }

            var mail = rules.Cargo[r].CargoIndex == _mailCargo;
            for (var town = 0; town < _towns.Count; town++)
            {
                _towns.TryGetTown(town, out var info);
                _monthly[town] = mail ? info.MailGeneratedLastMonth : info.PassengersGeneratedLastMonth;
            }

            _tables.TopUnservedPairs(r, _monthly, _pairsShown, _scratch);
            foreach (var pair in _scratch)
            {
                var found = _pairs.FindIndex(p => p.TownA == pair.TownA && p.TownB == pair.TownB);
                if (found >= 0)
                {
                    _pairs[found] = _pairs[found] with { Unserved = _pairs[found].Unserved + pair.Unserved };
                }
                else
                {
                    _pairs.Add(pair);
                }
            }
        }

        _pairs.Sort((a, b) => b.Unserved.CompareTo(a.Unserved));
        if (_pairs.Count > _pairsShown)
        {
            _pairs.RemoveRange(_pairsShown, _pairs.Count - _pairsShown);
        }

        _largest = _pairs.Count > 0 ? Math.Max(1, _pairs[0].Unserved) : 1;
    }

    protected override Vector2 MeasureCore(UiContext ui, Vector2 available) => Vector2.Zero;

    protected override void DrawSelf(UiContext ui, UiDrawList list)
    {
        foreach (var pair in _pairs)
        {
            if (!_visible[pair.TownA] || !_visible[pair.TownB])
            {
                continue;
            }

            var strength = Math.Clamp(pair.Unserved / (float)_largest, 0.15f, 1f);
            var size = (2f + (4f * strength)) * ui.Scale;
            var color = UiColor.FromBytes(226, 74, 58, (byte)(80 + (175 * strength)));
            var a = _screen[pair.TownA];
            var b = _screen[pair.TownB];
            var length = Vector2.Distance(a, b);
            var dots = Math.Min(400, (int)(length / (DotSpacing * ui.Scale)));
            for (var i = 0; i <= dots; i++)
            {
                var p = dots == 0 ? a : Vector2.Lerp(a, b, i / (float)dots);
                list.AddRect(new RectF(p.X - (size / 2), p.Y - (size / 2), size, size), color, size / 2);
            }
        }
    }
}

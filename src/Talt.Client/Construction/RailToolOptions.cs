using Talt.Core.Construction;
using Talt.Core.Rails;
using Talt.Core.Roads;

namespace Talt.Client.Construction;

/// <summary>
/// Choices of the rail tools (feature 015): track type, signal kind, station shape (platforms, length, axis). Interface
/// state, not simulation state: the choices travel in each command.
/// </summary>
internal static class RailToolOptions
{
    private static readonly (int Platforms, int Length)[] Shapes = [(1, 4), (2, 5), (2, 8), (3, 6), (4, 8), (1, 12)];
    private static int _shape;

    /// <summary>Incremented when a choice changes (previews are planned again).</summary>
    public static int Version { get; private set; }

    public static string TypeId { get; private set; } = "normal";

    public static SignalKind Signal { get; private set; } = SignalKind.Block;

    /// <summary>Platforms run along X (0) or Z (1).</summary>
    public static int Axis { get; private set; }

    public static int Platforms => Shapes[_shape].Platforms;

    public static int Length => Shapes[_shape].Length;

    public static int TypeFor(ConstructionState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var index = state.RailRules.FindType(TypeId);
        if (index < 0)
        {
            index = 0;
            TypeId = state.RailRules.Types[0].Id;
        }

        return index;
    }

    public static void CycleType(ConstructionState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var types = state.RailRules.Types;
        TypeId = types[(TypeFor(state) + 1) % types.Count].Id;
        Version++;
    }

    public static void CycleSignal()
    {
        Signal = Signal switch
        {
            SignalKind.Block => SignalKind.Path,
            SignalKind.Path => SignalKind.OneWayPath,
            _ => SignalKind.Block,
        };
        Version++;
    }

    /// <summary>Next station shape; after the last shape the axis turns.</summary>
    public static void CycleStation()
    {
        _shape++;
        if (_shape == Shapes.Length)
        {
            _shape = 0;
            Axis ^= 1;
        }

        Version++;
    }

    /// <summary>The signal a click on a tile sets: the chosen kind facing one way, then the other, then both, then none.</summary>
    public static (SignalKind Kind, int Faces) NextSignal(ConstructionState state, (int X, int Z) tile)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (!state.Map.Contains(tile.X, tile.Z))
        {
            return (Signal, RailSignal.FaceLow);
        }

        var current = state.Rails.SignalAt(state.Map.Index(tile.X, tile.Z));
        if (current == 0 || RailSignal.Kind(current) != Signal)
        {
            return (Signal, RailSignal.FaceLow);
        }

        var faces = RailSignal.Faces(current);
        var last = Signal == SignalKind.OneWayPath ? RailSignal.FaceHigh : RailSignal.FaceLow | RailSignal.FaceHigh;
        return faces >= last ? (SignalKind.None, 0) : (Signal, faces + 1);
    }

    /// <summary>The entrance of a depot on the tile: towards a neighbouring track end, else east.</summary>
    public static int DepotDirection(ConstructionState state, (int X, int Z) tile)
    {
        ArgumentNullException.ThrowIfNull(state);
        for (var d = 0; d < 8; d += 2)
        {
            var x = tile.X + Dir8.OffsetX(d);
            var z = tile.Z + Dir8.OffsetZ(d);
            if (state.Map.Contains(x, z) && state.Rails.HasRail(state.Map.Index(x, z)) && Dir8.Count(state.Rails.LinksAt(state.Map.Index(x, z))) <= 1)
            {
                return d;
            }
        }

        for (var d = 0; d < 8; d += 2)
        {
            var x = tile.X + Dir8.OffsetX(d);
            var z = tile.Z + Dir8.OffsetZ(d);
            if (state.Map.Contains(x, z) && state.Rails.HasRail(state.Map.Index(x, z)))
            {
                return d;
            }
        }

        return Dir8.E;
    }

    /// <summary>The depot id on a tile, or −1.</summary>
    public static int DepotAt(ConstructionState state, (int X, int Z) tile)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (!state.Map.Contains(tile.X, tile.Z))
        {
            return -1;
        }

        var index = state.RailDepots.IndexAtTile(state.Map.Index(tile.X, tile.Z));
        return index < 0 ? -1 : state.RailDepots.IdAt(index);
    }
}

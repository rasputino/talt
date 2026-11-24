using Talt.Core.Commands;
using Talt.Core.Construction;

namespace Talt.Client.Construction;

/// <summary>
/// The active construction tool and its drag (contracts/construction-api.md, research R11): hovering
/// previews one tile, pressing starts a drag, the shape follows the cursor, releasing confirms and the
/// cancel key or the right button cancels. The preview is planned with the same planner and checks as
/// the command (FR-002), only when the tool, the tiles or the game state changed, and allocates nothing.
/// GPU-free, so it is unit-tested.
/// </summary>
internal sealed class ToolController
{
    private readonly ConstructionPlanner _planner = new();
    private (int X, int Z)? _hover;
    private (int X, int Z) _start;
    private PreviewKey _planned;
    private int _reportedVersion;

    public ConstructionTool? Active { get; private set; }

    public bool IsDragging { get; private set; }

    /// <summary>The plan of the current preview (tiles, problems, cost, reason).</summary>
    public ConstructionPlan Preview { get; } = new();

    /// <summary>The checks of the current preview, as the command would report them.</summary>
    public CommandResult PreviewResult { get; private set; }

    /// <summary>True when <see cref="Preview"/> describes something to draw this frame.</summary>
    public bool HasPreview { get; private set; }

    /// <summary>Incremented whenever the preview changed (for uploading preview boxes only then).</summary>
    public int PreviewVersion { get; private set; }

    /// <summary>Selects a tool (null: none); any drag is cancelled.</summary>
    public void Select(ConstructionTool? tool)
    {
        Active = tool;
        IsDragging = false;
        Invalidate();
    }

    /// <summary>The tile under the cursor this frame, or null (cursor over the interface or the sky).</summary>
    public void Hover((int X, int Z)? tile) => _hover = tile;

    /// <summary>Starts a drag at a tile. Returns false when no tool is active (the camera handles the press).</summary>
    public bool Press((int X, int Z) tile)
    {
        if (Active is null)
        {
            return false;
        }

        IsDragging = true;
        _start = tile;
        _hover = tile;
        return true;
    }

    /// <summary>
    /// Ends the drag. Returns the command to submit when the preview was valid; otherwise null and
    /// <paramref name="reason"/> tells why (null when there was no drag).
    /// </summary>
    public ConstructionCommand? Release(ConstructionState state, out string? reason)
    {
        ArgumentNullException.ThrowIfNull(state);
        reason = null;
        if (!IsDragging || Active is null)
        {
            return null;
        }

        Plan(state);
        IsDragging = false;
        var (start, end) = Shape(_start, _hover ?? _start);
        Invalidate();
        if (!PreviewResult.Succeeded)
        {
            reason = PreviewResult.ReasonCode;
            return null;
        }

        return Active.Create(state.Player.Id, start, end);
    }

    /// <summary>Cancels the drag, or deselects the tool when not dragging. Returns false when there was nothing to cancel.</summary>
    public bool Cancel()
    {
        if (IsDragging)
        {
            IsDragging = false;
            Invalidate();
            return true;
        }

        if (Active is not null)
        {
            Select(null);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Plans the preview for the current tool and tiles if anything changed since the last plan. Returns
    /// true when the preview changed.
    /// </summary>
    public bool UpdatePreview(ConstructionState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        Plan(state);

        // Report every change since the last call, including the preview disappearing on cancel or
        // deselect (hand test: old marks stayed on screen because that case was not reported).
        var changed = PreviewVersion != _reportedVersion;
        _reportedVersion = PreviewVersion;
        return changed;
    }

    private void Plan(ConstructionState state)
    {
        if (Active is null || (!IsDragging && _hover is null))
        {
            if (HasPreview)
            {
                Invalidate();
            }

            return;
        }

        var (start, end) = IsDragging ? Shape(_start, _hover ?? _start) : (_hover!.Value, _hover.Value);
        var key = new PreviewKey(Active, start, end, state.Version, state.Player.Balance, RoadToolOptions.Version, state.Year);
        if (HasPreview && key == _planned)
        {
            return;
        }

        _planned = key;
        Active.Plan(_planner, state, state.Player.Id, start, end, Preview);
        PreviewResult = ConstructionCommand.Check(state.Player, Preview);
        HasPreview = true;
        PreviewVersion++;
    }

    /// <summary>The tiles a drag covers for the active tool's shape.</summary>
    internal (( int X, int Z) Start, (int X, int Z) End) Shape((int X, int Z) start, (int X, int Z) current) => Active?.Shape switch
    {
        DragShape.Tile => (current, current),
        DragShape.Line => Math.Abs(current.X - start.X) >= Math.Abs(current.Z - start.Z)
            ? (start, (current.X, start.Z))
            : (start, (start.X, current.Z)),
        DragShape.Line8 => (start, Line8End(start, current)),
        _ => (start, current),
    };

    private static (int X, int Z) Line8End((int X, int Z) start, (int X, int Z) current)
    {
        var (direction, steps) = Talt.Core.Roads.RoadPlanner.LineOf(start, current, straightOnly: false);
        return (start.X + (Talt.Core.Roads.Dir8.OffsetX(direction) * steps), start.Z + (Talt.Core.Roads.Dir8.OffsetZ(direction) * steps));
    }

    private void Invalidate()
    {
        HasPreview = false;
        _planned = default;
        PreviewVersion++;
    }

    private readonly record struct PreviewKey(ConstructionTool? Tool, (int X, int Z) Start, (int X, int Z) End, int Version, long Balance, int RoadOptions, int Year);
}

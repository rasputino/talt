using Talt.Core.Construction;
using Talt.Core.Maps;

namespace Talt.Core.Towns;

/// <summary>Rating penalties and demolition of town buildings by construction actions (research R9, R10).</summary>
public sealed partial class TownRegistry
{
    private int[]? _penalty;
    private readonly List<int> _penalized = [];
    private readonly List<int> _removedBuildings = [];
    private readonly List<int> _zoneTowns = [];

    /// <summary>
    /// The rating change a plan would cause to the company, summed over towns, after the per-action cap (for the
    /// preview; changes nothing).
    /// </summary>
    public int PlanRatingChange(ConstructionPlan plan, int companyId)
    {
        ArgumentNullException.ThrowIfNull(plan);
        CollectPenalties(plan);
        var total = 0;
        foreach (var town in _penalized)
        {
            var current = RatingOf(town, companyId);
            var after = Math.Max(TownRatingRules.Min, current - Math.Min(_penalty![town], Rules.Ratings.MaxPenaltyPerAction));
            total += after - current;
            _penalty[town] = 0;
        }

        _penalized.Clear();
        return total;
    }

    /// <summary>
    /// Called by <see cref="ConstructionState"/> just before a plan is written (FR-017): removes the town buildings
    /// the plan clears and lowers the company's ratings. Returns true when buildings were removed.
    /// </summary>
    public bool ApplyConstruction(ConstructionPlan plan, int companyId)
    {
        ArgumentNullException.ThrowIfNull(plan);
        CollectPenalties(plan);
        foreach (var town in _penalized)
        {
            AddRating(town, companyId, -Math.Min(_penalty![town], Rules.Ratings.MaxPenaltyPerAction));
            _penalty[town] = 0;
        }

        _penalized.Clear();
        var removed = _removedBuildings.Count > 0;
        foreach (var id in _removedBuildings)
        {
            var slot = Buildings.SlotOf(id);
            if (slot >= 0)
            {
                RemoveBuilding(slot, clearObjects: false); // The plan clears the tiles itself.
            }
        }

        _removedBuildings.Clear();
        return removed;
    }

    /// <summary>Fills the per-town penalties and the list of buildings a plan removes.</summary>
    private void CollectPenalties(ConstructionPlan plan)
    {
        _penalty ??= new int[Count];
        _penalized.Clear();
        _removedBuildings.Clear();
        var changes = plan.Changes;
        if (changes.Length == 0)
        {
            return;
        }

        var rules = Rules.Ratings;
        var anyTree = false;
        foreach (var change in changes)
        {
            if (change.OldObject == TileObjectKind.TownBuilding && change.NewObject == TileObjectKind.None
                && change.OldOwner.Kind == TileOwnerKind.Town)
            {
                if (Buildings.TryGetAt(change.Index, out var id) && !_removedBuildings.Contains(id))
                {
                    _removedBuildings.Add(id);
                    Penalize(change.OldOwner.Index, rules.BuildingPenalty);
                }
            }
            else if (change.OldObject == TileObjectKind.Road && change.OldOwner.Kind == TileOwnerKind.Town
                && change.NewObject == TileObjectKind.None)
            {
                Penalize(change.OldOwner.Index, rules.RoadPenalty);
            }

            if (Scenery.Kind(change.OldScenery) != 0 && Scenery.Kind(change.NewScenery) == 0
                && change.OldObject != TileObjectKind.TownBuilding)
            {
                anyTree = true;
            }
        }

        if (anyTree && rules.TreePenalty > 0)
        {
            PenalizeTrees(changes);
        }
    }

    /// <summary>Tree penalties: only towns whose zone meets the plan's bounding box are looked at.</summary>
    private void PenalizeTrees(ReadOnlySpan<TileChange> changes)
    {
        var width = _map.Width;
        int minX = int.MaxValue, minZ = int.MaxValue, maxX = int.MinValue, maxZ = int.MinValue;
        foreach (var change in changes)
        {
            var x = change.Index % width;
            var z = change.Index / width;
            minX = Math.Min(minX, x);
            maxX = Math.Max(maxX, x);
            minZ = Math.Min(minZ, z);
            maxZ = Math.Max(maxZ, z);
        }

        _zoneTowns.Clear();
        var margin = Rules.Ratings.ZoneMargin;
        for (var town = 0; town < Count; town++)
        {
            var reach = RadiusOf(town) + margin;
            if (_centreX[town] + reach >= minX && _centreX[town] - reach <= maxX
                && _centreZ[town] + reach >= minZ && _centreZ[town] - reach <= maxZ)
            {
                _zoneTowns.Add(town);
            }
        }

        if (_zoneTowns.Count == 0)
        {
            return;
        }

        foreach (var change in changes)
        {
            if (Scenery.Kind(change.OldScenery) == 0 || Scenery.Kind(change.NewScenery) != 0 || change.OldObject == TileObjectKind.TownBuilding)
            {
                continue;
            }

            var x = change.Index % width;
            var z = change.Index / width;
            var best = -1;
            var bestDistance = int.MaxValue;
            foreach (var town in _zoneTowns)
            {
                var distance = Math.Max(Math.Abs(x - _centreX[town]), Math.Abs(z - _centreZ[town]));
                if (distance <= RadiusOf(town) + margin && distance < bestDistance)
                {
                    best = town;
                    bestDistance = distance;
                }
            }

            if (best >= 0)
            {
                Penalize(best, Rules.Ratings.TreePenalty);
            }
        }
    }

    private void Penalize(int town, int amount)
    {
        if ((uint)town >= (uint)Count || amount <= 0)
        {
            return;
        }

        if (_penalty![town] == 0)
        {
            _penalized.Add(town);
        }

        _penalty[town] = (int)Math.Min(int.MaxValue / 2, (long)_penalty[town] + amount);
    }
}

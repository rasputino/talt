using System.Collections;
using System.Reflection;

namespace Talt.Core.Tests.Saves;

/// <summary>
/// Diagnostics for save tests: compares two object graphs field by field (reflection is fine in tests) and returns the path
/// of the first difference, so a failing re-save names the field instead of a byte offset.
/// </summary>
internal static class StateDiff
{
    public static string? FirstDifference(object? a, object? b, string path = "", int depth = 0, HashSet<(object, object)>? seen = null)
    {
        seen ??= new HashSet<(object, object)>(new PairComparer());
        if (a is null || b is null)
        {
            return a is null && b is null ? null : $"{path}: {(a is null ? "null" : "set")} vs {(b is null ? "null" : "set")}";
        }

        var type = a.GetType();
        if (type != b.GetType())
        {
            return $"{path}: type {type.Name} vs {b.GetType().Name}";
        }

        if (type.IsPrimitive || type.IsEnum || a is string || a is decimal)
        {
            return Equals(a, b) ? null : $"{path}: {a} vs {b}";
        }

        if (depth > 12 || a is Delegate || type.Namespace?.StartsWith("System.Threading", StringComparison.Ordinal) == true
            || (depth > 0 && OtherSystems.Contains(type.Name)))
        {
            return null;
        }

        if (!type.IsValueType && !seen.Add((a, b)))
        {
            return null;
        }

        if (a is IDictionary da && b is IDictionary db)
        {
            if (da.Count != db.Count)
            {
                return $"{path}: dictionary count {da.Count} vs {db.Count}";
            }

            var ka = da.Keys.Cast<object>().ToList();
            var kb = db.Keys.Cast<object>().ToList();
            for (var i = 0; i < ka.Count; i++)
            {
                if (!Equals(ka[i], kb[i]))
                {
                    return $"{path}: dictionary key #{i} {ka[i]} vs {kb[i]}";
                }

                var d = FirstDifference(da[ka[i]], db[kb[i]], $"{path}[{ka[i]}]", depth + 1, seen);
                if (d is not null)
                {
                    return d;
                }
            }

            return null;
        }

        if (a is IEnumerable ea && b is IEnumerable eb && a is not string)
        {
            var la = ea.Cast<object?>().ToList();
            var lb = eb.Cast<object?>().ToList();
            if (la.Count != lb.Count)
            {
                return $"{path}: length {la.Count} vs {lb.Count}";
            }

            for (var i = 0; i < la.Count; i++)
            {
                var d = FirstDifference(la[i], lb[i], $"{path}[{i}]", depth + 1, seen);
                if (d is not null)
                {
                    return d;
                }
            }

            return null;
        }

        for (var t = type; t is not null && t != typeof(object); t = t.BaseType)
        {
            foreach (var field in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                var d = FirstDifference(field.GetValue(a), field.GetValue(b), $"{path}.{field.Name}", depth + 1, seen);
                if (d is not null)
                {
                    return d;
                }
            }
        }

        return null;
    }

    /// <summary>Wiring to other systems and immutable data, not followed below the root.</summary>
    private static readonly HashSet<string> OtherSystems =
    [
        "ConstructionState", "StationRegistry", "FinanceSystem", "GameContent", "RoadLayer", "RailLayer", "IndustryFleet",
        "TownRegistry", "MapData", "TrainSystem", "RoadVehicleSystem", "GameSimulation", "VehicleRules", "RailRules", "DemandRules",
        "StationRules", "FinanceRules", "TownRules", "RoadRules", "ConstructionRules", "RoadNetwork", "TownNames", "MoneyEventFeed",
        "VehicleNoticeFeed", "RoadRouter", "RailRouter", "RouteAssignment",
    ];

    private sealed class PairComparer : IEqualityComparer<(object, object)>
    {
        public bool Equals((object, object) x, (object, object) y) => ReferenceEquals(x.Item1, y.Item1) && ReferenceEquals(x.Item2, y.Item2);

        public int GetHashCode((object, object) obj) => HashCode.Combine(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj.Item1), System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj.Item2));
    }
}

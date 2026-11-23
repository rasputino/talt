using Talt.Core.Maps;

namespace Talt.Core.Content;

/// <summary>
/// World-wide rules of the industry economy, loaded from <c>industries.json</c> (data-model.md,
/// EconomyRules). Every value is an integer so the simulation stays exact.
/// </summary>
/// <param name="InitialOccupancyPercent">Share of the suitable sites that start with an industry.</param>
/// <param name="DriftPerMille">Random monthly production drift, plus or minus this many per mille.</param>
/// <param name="GrowthPerMille">Monthly rate growth for an industry that received deliveries.</param>
/// <param name="DeclineAfterMonths">Months without deliveries before the rate starts falling.</param>
/// <param name="DeclinePerMille">Monthly rate loss while neglected.</param>
/// <param name="CloseAfterMonths">Months without deliveries before closing is possible.</param>
/// <param name="CloseChancePerMille">Monthly chance to close once the thresholds are met.</param>
/// <param name="CloseAtRatePerMille">Closing also requires the rate to be at or below this.</param>
/// <param name="OpeningChancePerMillePerSite">Monthly chance per free site to open an industry.</param>
/// <param name="MaxOpeningsPerMonth">Maximum new industries per month.</param>
/// <param name="MinRatePerMille">Lowest allowed production rate.</param>
/// <param name="MaxRatePerMille">Highest allowed production rate.</param>
public sealed record EconomyRules(
    int InitialOccupancyPercent = 75,
    int DriftPerMille = 15,
    int GrowthPerMille = 25,
    int DeclineAfterMonths = 24,
    int DeclinePerMille = 20,
    int CloseAfterMonths = 60,
    int CloseChancePerMille = 8,
    int CloseAtRatePerMille = 700,
    int OpeningChancePerMillePerSite = 4,
    int MaxOpeningsPerMonth = 3,
    int MinRatePerMille = 300,
    int MaxRatePerMille = 1500);

/// <summary>
/// A kind of industry loaded from <c>industries.json</c> (data-model.md, IndustryKindDefinition).
/// Instances are placed on the sites reserved by feature 004.
/// </summary>
/// <param name="Id">Stable data id such as <c>steel-mill</c>.</param>
/// <param name="NameKey">Localization key for the player-facing name.</param>
/// <param name="FootprintWidth">Occupied tiles along X (1 to 5).</param>
/// <param name="FootprintDepth">Occupied tiles along Z (1 to 5).</param>
/// <param name="Production">Cargo produced every month.</param>
/// <param name="Accepts">Cargo the industry takes from transport.</param>
/// <param name="Recipes">Automatic conversions applied to accepted cargo.</param>
/// <param name="Stock">Per-cargo storage caps.</param>
/// <param name="Model">Placeholder boxes, relative to the footprint centre.</param>
/// <param name="SiteKinds">Site kinds where it may stand; never <see cref="SiteKind.Town"/>.</param>
/// <param name="EraStart">First year in which it may open.</param>
/// <param name="EraEnd">Last year in which it may open (inclusive).</param>
/// <param name="Weight">Relative placement probability.</param>
public readonly record struct IndustryKindDefinition(
    string Id,
    string NameKey,
    int FootprintWidth,
    int FootprintDepth,
    IReadOnlyList<ProductionEntry> Production,
    IReadOnlyList<string> Accepts,
    IReadOnlyList<RecipeDefinition> Recipes,
    IReadOnlyList<StockDefinition> Stock,
    IReadOnlyList<ModelBox> Model,
    IReadOnlyList<SiteKind> SiteKinds,
    int EraStart,
    int EraEnd,
    int Weight)
{
    /// <summary>True when <paramref name="cargoId"/> is in <see cref="Accepts"/>.</summary>
    public bool AcceptsCargo(string cargoId)
    {
        for (var i = 0; i < Accepts.Count; i++)
        {
            if (string.Equals(Accepts[i], cargoId, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The recipe for <paramref name="cargoId"/>, or null when the cargo is consumed directly.</summary>
    public RecipeDefinition? RecipeFor(string cargoId)
    {
        for (var i = 0; i < Recipes.Count; i++)
        {
            if (string.Equals(Recipes[i].InputCargoId, cargoId, StringComparison.Ordinal))
            {
                return Recipes[i];
            }
        }

        return null;
    }

    /// <summary>Storage capacity for <paramref name="cargoId"/>; zero when not stored.</summary>
    public int CapacityFor(string cargoId)
    {
        for (var i = 0; i < Stock.Count; i++)
        {
            if (string.Equals(Stock[i].CargoId, cargoId, StringComparison.Ordinal))
            {
                return Stock[i].Capacity;
            }
        }

        return 0;
    }

    /// <summary>Highest model box top, used for picking and selection highlights.</summary>
    public int ModelHeight
    {
        get
        {
            var height = 1;
            for (var i = 0; i < Model.Count; i++)
            {
                height = Math.Max(height, Model[i].Y + Model[i].Height);
            }

            return height;
        }
    }
}

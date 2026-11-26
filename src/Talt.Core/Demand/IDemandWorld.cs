using Talt.Core.Industries;
using Talt.Core.Towns;

namespace Talt.Core.Demand;

/// <summary>
/// What the demand tables read from the world (towns and industries). <see cref="GameDemandWorld"/> reads the game's
/// registries; tests use small hand-made worlds.
/// </summary>
public interface IDemandWorld
{
    /// <summary>Number of towns; towns are indices <c>0..TownCount-1</c>.</summary>
    int TownCount { get; }

    (int X, int Z) TownCentre(int town);

    int TownPopulation(int town);

    /// <summary>True when buildings of the town accept the cargo (goods for industry demand).</summary>
    bool TownAccepts(int town, int cargo);

    /// <summary>Number of industry slots (slots may change as industries open and close).</summary>
    int IndustryCount { get; }

    int IndustryIdAt(int slot);

    (int X, int Z) IndustryPosition(int slot);

    /// <summary>True when the industry in the slot can produce the cargo (production or a recipe output).</summary>
    bool IndustryProduces(int slot, int cargo);
}

/// <summary>The game's towns and industries as an <see cref="IDemandWorld"/>.</summary>
public sealed class GameDemandWorld(TownRegistry towns, IndustryFleet industries) : IDemandWorld
{
    private readonly int _foodCargo = industries.Content.CargoIndex("food");

    public int TownCount => towns.Count;

    public (int X, int Z) TownCentre(int town) => towns.CentreOf(town);

    public int TownPopulation(int town) => towns.PopulationOf(town);

    public bool TownAccepts(int town, int cargo) => towns.Accepts(town, food: cargo == _foodCargo);

    public int IndustryCount => industries.Count;

    public int IndustryIdAt(int slot) => industries.IdAt(slot);

    public (int X, int Z) IndustryPosition(int slot) => (industries.XAt(slot), industries.ZAt(slot));

    public bool IndustryProduces(int slot, int cargo)
    {
        var kind = industries.Content.IndustryKind(industries.KindIndexAt(slot));
        var id = industries.Content.Cargo(cargo).Id;
        // Indexed loops: enumerating the read-only lists would allocate (this runs every day).
        for (var i = 0; i < kind.Production.Count; i++)
        {
            if (kind.Production[i].CargoId == id)
            {
                return true;
            }
        }

        for (var i = 0; i < kind.Recipes.Count; i++)
        {
            if (kind.Recipes[i].OutputCargoId == id)
            {
                return true;
            }
        }

        return false;
    }
}

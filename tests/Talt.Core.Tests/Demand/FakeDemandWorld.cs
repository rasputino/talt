using Talt.Core.Demand;

namespace Talt.Core.Tests.Demand;

/// <summary>A hand-made world of towns (and goods industries) for demand table tests.</summary>
internal sealed class FakeDemandWorld : IDemandWorld
{
    private readonly List<(int X, int Z, int Population, bool AcceptsGoods)> _towns = [];
    private readonly List<(int Id, int X, int Z, int Cargo)> _industries = [];

    public int TownCount => _towns.Count;

    public int IndustryCount => _industries.Count;

    public FakeDemandWorld Town(int x, int z, int population, bool acceptsGoods = true)
    {
        _towns.Add((x, z, population, acceptsGoods));
        return this;
    }

    public FakeDemandWorld Industry(int id, int x, int z, int cargo)
    {
        _industries.Add((id, x, z, cargo));
        return this;
    }

    public void SetPopulation(int town, int population) => _towns[town] = _towns[town] with { Population = population };

    public (int X, int Z) TownCentre(int town) => (_towns[town].X, _towns[town].Z);

    public int TownPopulation(int town) => _towns[town].Population;

    public bool TownAccepts(int town, int cargo) => _towns[town].AcceptsGoods;

    public int IndustryIdAt(int slot) => _industries[slot].Id;

    public (int X, int Z) IndustryPosition(int slot) => (_industries[slot].X, _industries[slot].Z);

    public bool IndustryProduces(int slot, int cargo) => _industries[slot].Cargo == cargo;
}

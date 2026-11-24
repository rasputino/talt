using Talt.Core.Network;
using Talt.Core.Simulation;

namespace Talt.Core.Scenarios;

/// <summary>
/// Builds the prototype network and the fleet described by <see cref="ScenarioSettings"/>. Shared by the
/// client and the headless runner so both create exactly the same game. Map-less scenarios (benchmarks, CI
/// goldens) get the synthetic grid and traffic; games with a map get neither (feature 007, research R1).
/// </summary>
public static class ScenarioBuilder
{
    public static (TransportNetwork Network, VehicleFleet Fleet) Build(ScenarioSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate();
        if (settings.Map is not null)
        {
            // Games with a map have real roads (feature 007, research R1) and no synthetic traffic until road
            // vehicles exist (feature 012): an empty prototype network and fleet.
            var empty = new TransportNetworkBuilder().Build();
            return (empty, new VehicleFleet(empty));
        }

        var junctions = settings.MapSizeTiles / settings.RoadSpacing;
        var origin = settings.RoadSpacing / 2;
        var network = GridNetworkGenerator.Generate(
            junctions,
            junctions,
            settings.RoadSpacing,
            settings.Seed,
            settings.RoadRemovalChancePerMille,
            origin,
            origin);

        var fleet = SyntheticTraffic.Create(network, settings.EffectiveLineCount, settings.VehicleCount, settings.Seed);
        return (network, fleet);
    }
}

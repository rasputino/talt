using Talt.Core.Network;
using Talt.Core.Simulation;

namespace Talt.Core.Scenarios;

/// <summary>
/// Builds the network and the fleet described by <see cref="ScenarioSettings"/>. Shared by the
/// client and the headless runner so both create exactly the same game.
/// </summary>
public static class ScenarioBuilder
{
    public static (TransportNetwork Network, VehicleFleet Fleet) Build(ScenarioSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate();

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

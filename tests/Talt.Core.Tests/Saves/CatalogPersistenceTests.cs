using Talt.Core.Rails;
using Talt.Core.Vehicles;

namespace Talt.Core.Tests.Saves;

public class CatalogPersistenceTests
{
    [Fact]
    public void Year_boundary_preserves_company_policies_and_individual_wagon_ages()
    {
        var game = SaveTestGames.Rich(startYear: 1959);
        SaveTestGames.RunUntil(game, s => s.Date is { Month: 12, Day: 31 });
        var player = game.Construction!.Player;
        player.RailReplacements.Set("wagon-hopper", new ReplacementRule("wagon-deepwell", true, false));
        var train = game.Trains!.TrainAt(0);
        train.Parts[1].PurchaseDay -= 365;
        train.Parts[2].RenewalNotified = true;
        var restored = RoundTrip.SaveAndLoad(game);
        Assert.Equal(player.RoadReplacements.Rules, restored.Construction!.Player.RoadReplacements.Rules);
        Assert.Equal(player.RailReplacements.Rules, restored.Construction.Player.RailReplacements.Rules);
        Assert.True(restored.Trains!.TrainAt(0).Parts[2].RenewalNotified);
        Assert.Equal(train.Parts[1].PurchaseDay, restored.Trains.TrainAt(0).Parts[1].PurchaseDay);
        RoundTrip.AssertContinuesIdentically(game, restored, 80, every: 1);
        Assert.Equal(1960, restored.Date.Year);
        game.Submit(new SendTrainToDepotCommand(player.Id, train.Id));
        restored.Submit(new SendTrainToDepotCommand(player.Id, train.Id));
        RoundTrip.AssertContinuesIdentically(game, restored, 2000);
        Assert.Equal(train.Depot, restored.Trains.TrainAt(0).Depot);
        Assert.Equal(player.Balance, restored.Construction.Player.Balance);
    }
}

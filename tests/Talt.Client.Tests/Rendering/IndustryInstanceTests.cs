using Talt.Client.Rendering;

namespace Talt.Client.Tests.Rendering;

public class IndustryInstanceTests
{
    [Fact]
    public void OneInstanceIsBuiltPerModelBox()
    {
        var game = GameSetup.Create(new NewGameOptions(ChunksPerSide: 8, VehicleCount: 0, Seed: 7));
        var session = new GameSession(game, new SessionOptions(), fastForwardUnlocked: true);
        var industries = session.Simulation.Industries;
        Assert.True(industries.Count > 0);

        var expected = 0;
        for (var slot = 0; slot < industries.Count; slot++)
        {
            expected += industries.Content.IndustryKind(industries.KindIndexAt(slot)).Model.Count;
        }

        session.BuildInstances();

        Assert.Equal(expected, session.IndustryInstances.Length);
        Assert.All(session.IndustryInstances.ToArray(), instance =>
        {
            Assert.True(instance.Extent.X >= 1f);
            Assert.True(instance.Extent.Y >= 1f);
            Assert.True(instance.Extent.Z >= 1f);
            Assert.True(instance.Color.W > 0f);
        });
    }
}

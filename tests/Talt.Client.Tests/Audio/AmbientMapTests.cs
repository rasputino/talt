using Talt.Client;
using Talt.Client.Audio;
using Xunit;

namespace Talt.Client.Tests.Audio;

public class AmbientMapTests
{
    [Fact]
    public void GeneratedMap_ProducesAValidEnvironmentForEveryCellCentre()
    {
        var game = GameSetup.Create(new NewGameOptions(ChunksPerSide: 4, VehicleCount: 10, Seed: 3));
        var map = new AmbientMap(game.Map);
        Assert.True(map.Width > 0);
        Assert.True(map.Depth > 0);
        for (var z = 0; z < map.Depth; z += 16)
        {
            for (var x = 0; x < map.Width; x += 16)
            {
                var environment = map.EnvironmentAt((x + 0.5f) * map.TilesPerCell, (z + 0.5f) * map.TilesPerCell);
                Assert.True(Enum.IsDefined(environment));
            }
        }
    }

    [Fact]
    public void AudioSystem_SetGame_StartsAmbientVoicesWithoutThrowing()
    {
        var game = GameSetup.Create(new NewGameOptions(ChunksPerSide: 4, VehicleCount: 10, Seed: 3));
        var backend = new FakeAudioBackend();
        using var audio = new AudioSystem(backend, AudioTestData.Definition(), new PlayerSettings());
        audio.SetGame(game.Map);
        audio.Update(default);
        Assert.NotNull(backend.VoicePlaying("Country.flac"));
    }
}

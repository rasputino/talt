using Talt.Client.Audio;
using Xunit;

namespace Talt.Client.Tests.Audio;

public class AudioCommandClassifierTests
{
    [Theory]
    [InlineData("BuildRoadCommand")]
    [InlineData("RemoveStationCommand")]
    [InlineData("TerraformCommand")]
    [InlineData("TrackCommand")]
    [InlineData("DemolishBuildingCommand")]
    public void ConstructionNames_AreRecognised(string name) => Assert.True(AudioCommandClassifier.IsConstruction(name));

    [Theory]
    [InlineData("AddVehicleCommand")]
    [InlineData("RemoveVehicleCommand")]
    [InlineData("SetSpeedCommand")]
    public void OtherCommands_AreNotConstruction(string name) => Assert.False(AudioCommandClassifier.IsConstruction(name));
}

using System.Diagnostics;
using System.Numerics;
using Talt.Client.Rendering;
using Talt.Client.Localization;
using Talt.Client.Saves;
using Talt.Client.Screens;
using Talt.Client.Tests.Ui;
using Talt.Client.Ui;
using Talt.Client.Ui.Widgets;
using Talt.Client.Ui.Windows;
using Talt.Core.Saves;
using Talt.Core.Scenarios;
using Talt.Core.Simulation;

namespace Talt.Client.Tests.Saves;

public class SaveScreensTests(Xunit.Abstractions.ITestOutputHelper output)
{
    [Fact]
    public void Overwrite_and_load_wait_for_confirmation()
    {
        var folder = Directory.CreateTempSubdirectory();
        try
        {
            var host = new Host(new SaveStore(folder.FullName));
            var path = host.Saves.PathFor(host.SuggestedSaveName);
            var game = GameSimulation.Create(new ScenarioSettings(MapSizeTiles: 64, VehicleCount: 0));
            Assert.True(SaveGame.Save(game, path, new SaveDetailsInput("Test", SaveKind.Manual, DateTimeOffset.UnixEpoch, "test")).Succeeded);
            var save = host.Ui.Windows.Open(SaveWindow.Create(host));
            Click(save, "save.confirm");
            Assert.Null(host.Saved);
            Click(host.Ui.Windows.Front!, "confirm.cancel");
            Assert.True(host.Ui.Windows.IsOpen(save));
            Click(save, "save.confirm");
            Click(host.Ui.Windows.Front!, "save.overwrite.yes");
            Assert.Equal("Test", host.Saved);

            var load = host.Ui.Windows.Open(LoadWindow.Create(host));
            var list = Assert.Single(UiTestKit.Walk(load.Content!).OfType<ListView>());
            list.OnSelected!(0);
            Click(load, "load.confirm");
            Assert.Null(host.Loaded);
            host.Discard!();
            Assert.Equal(path, host.Loaded);
        }
        finally
        {
            folder.Delete(true);
        }
    }

    [Fact]
    public void Two_hundred_game_catalog_opens_within_budget()
    {
        var folder = Directory.CreateTempSubdirectory();
        try
        {
            var host = new Host(new SaveStore(folder.FullName));
            var path = host.Saves.PathFor("First");
            var game = GameSimulation.Create(new ScenarioSettings(MapSizeTiles: 64, VehicleCount: 0, Map: new Talt.Core.Maps.MapSettings(64, 64)));
            Assert.True(SaveGame.Save(game, path, new SaveDetailsInput("First", SaveKind.Manual, DateTimeOffset.UnixEpoch, "test")).Succeeded);
            for (var i = 1; i < 200; i++)
            {
                File.Copy(path, host.Saves.PathFor($"Copy {i}"));
            }

            var timer = Stopwatch.StartNew();
            var window = host.Ui.Windows.Open(LoadWindow.Create(host));
            UiTestKit.Frame(host.Ui);
            timer.Stop();
            output.WriteLine($"Catalog opened and drew 200 rows in {timer.Elapsed.TotalMilliseconds:F1} ms");
            Assert.Equal(200, Assert.Single(UiTestKit.Walk(window.Content!).OfType<ListView>()).Count);
            Assert.True(timer.Elapsed < TimeSpan.FromSeconds(3), timer.Elapsed.ToString());
        }
        finally
        {
            folder.Delete(true);
        }
    }

    [Fact]
    public void Camera_survives_a_client_save_and_headless_can_resume_it()
    {
        var folder = Directory.CreateTempSubdirectory();
        try
        {
            var path = Path.Combine(folder.FullName, "camera.talt");
            var game = GameSimulation.Create(new ScenarioSettings(MapSizeTiles: 64, VehicleCount: 0));
            var camera = new OrbitCamera { Target = new Vector3(18, 4, 12), Distance = 200, Yaw = 1, Pitch = 0.7f };
            using var controller = new SaveController();
            Assert.True(controller.TryStart(game, camera, path, "Camera", SaveKind.Manual, "test"));
            Assert.True(controller.WaitForWrite()!.Result.Succeeded);
            Assert.True(SaveGame.Load(path).Succeeded);
            var result = SaveGame.Load(path, new SaveLoadOptions { ExtraSectionIds = [SaveController.CameraSection] });
            var restored = new OrbitCamera();
            Assert.True(SaveController.TryReadCamera(result.ExtraSections, restored));
            Assert.Equal(camera.Target, restored.Target);
            Assert.Equal(camera.Distance, restored.Distance);
            Assert.Equal(camera.Yaw, restored.Yaw);
            Assert.Equal(camera.Pitch, restored.Pitch);
        }
        finally
        {
            folder.Delete(true);
        }
    }

    private static void Click(UiWindow window, string key) =>
        Assert.Single(UiTestKit.Walk(window.Content!).OfType<Button>(), b => b.Text?.Key == key).Click();

    private sealed class Host(SaveStore store) : ISaveHost
    {
        public UiContext Ui { get; } = UiTestKit.CreateContext();
        public MoneyFormatter Money { get; } = MoneyFormatter.Load();
        public Currency Currency => Money.Resolve(null, "en");
        public SaveStore Saves => store;
        public bool InGame => true;
        public string SuggestedSaveName => "Test";
        public string? Saved { get; private set; }
        public string? Loaded { get; private set; }
        public Action? Discard { get; private set; }
        public void ShowFileError(string messageKey) => Assert.Fail(messageKey);
        public void SaveAs(string name) => Saved = name;
        public void LoadSave(string path) => Loaded = path;
        public void ConfirmDiscard(Action action) => Discard = action;
    }
}

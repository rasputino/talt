using Talt.Client.Screens;
using Talt.Client.Tests.Ui;
using Talt.Client.Ui.Widgets;
using Xunit;

namespace Talt.Client.Tests.Audio;

public class AudioSettingsTests
{
    [Fact]
    public void SettingsWindow_HasFourVolumeSlidersAndMute()
    {
        var ui = UiTestKit.CreateContext("en");
        var host = new FakeHost(ui);
        var window = SettingsWindow.Create(host);
        var sliders = UiTestKit.Walk(window).OfType<Slider>().ToArray();
        Assert.Equal(4, sliders.Length);
        var mute = UiTestKit.Walk(window).OfType<Checkbox>().First();
        Assert.Equal("Mute all sound", mute.Text.Resolve(ui));
    }

    [Fact]
    public void MovingASlider_UpdatesTheRightSettingAndApplies()
    {
        var ui = UiTestKit.CreateContext("es");
        var host = new FakeHost(ui);
        var window = SettingsWindow.Create(host);
        var sliders = UiTestKit.Walk(window).OfType<Slider>().ToArray();
        sliders[1].OnChanged(0.25f);
        Assert.Equal(0.25f, host.Settings.MusicVolume);
        Assert.Contains("apply", host.Calls);
    }

    [Fact]
    public void MuteCheckbox_CanBeToggled()
    {
        var ui = UiTestKit.CreateContext("en");
        var host = new FakeHost(ui);
        var mute = UiTestKit.Walk(SettingsWindow.Create(host)).OfType<Checkbox>().First();
        mute.OnChanged(true);
        Assert.True(host.Settings.Muted);
        Assert.Contains("apply", host.Calls);
    }
}

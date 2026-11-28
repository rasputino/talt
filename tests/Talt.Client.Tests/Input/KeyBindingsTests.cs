using Silk.NET.Input;
using Talt.Client.Input;

namespace Talt.Client.Tests.Input;

public class KeyBindingsTests
{
    private static KeyBindings Create() => new(KeyBindings.LoadDefinitions());

    [Theory]
    [InlineData(Key.Space, "pause")]
    [InlineData(Key.Number1, "speedNormal")]
    [InlineData(Key.Number2, "speedFast")]
    [InlineData(Key.Number3, "speedVeryFast")]
    [InlineData(Key.F, "fastForward")]
    [InlineData(Key.N, "addVehicle")]
    [InlineData(Key.Delete, "removeVehicle")]
    [InlineData(Key.F5, "quickSave")]
    [InlineData(Key.F9, "quickLoad")]
    [InlineData(Key.F3, "statistics")]
    [InlineData(Key.PageUp, "toolRaise")]
    [InlineData(Key.L, "toolLevel")]
    [InlineData(Key.Backspace, "undoConstruction")]
    [InlineData(Key.Escape, "back")]
    public void Defaults_AreTheOriginalControls(Key key, string action)
    {
        Assert.True(Create().TryGetAction(key, false, out var found));
        Assert.Equal(action, found);
    }

    [Fact]
    public void HeldCameraKeys_AreWasdAndArrows()
    {
        var bindings = Create();
        Assert.True(bindings.IsHeld("panForward", k => k == Key.W));
        Assert.True(bindings.IsHeld("panForward", k => k == Key.Up));
        Assert.True(bindings.IsHeld("rotateLeft", k => k == Key.Q));
        Assert.False(bindings.IsHeld("panForward", k => k == Key.S));
    }

    [Fact]
    public void ShiftVariant_WinsOverThePlainKey_OtherwiseFallsBack()
    {
        var bindings = Create();
        Assert.True(bindings.TryGetAction(Key.Delete, true, out var action));
        Assert.Equal("closeAllWindows", action);
        Assert.True(bindings.TryGetAction(Key.PageUp, true, out action));
        Assert.Equal("toolRaise", action); // No Shift variant: falls back to the plain key.
    }

    [Fact]
    public void Rebinding_MovesTheKey_AndReportsTheConflict()
    {
        var bindings = Create();
        Assert.Equal("addVehicle", bindings.FindConflict(new KeyChord(Key.N, false), "pause"));
        bindings.Set("pause", 0, new KeyChord(Key.N, false));
        Assert.True(bindings.TryGetAction(Key.N, false, out var action));
        Assert.Equal("pause", action);
        Assert.Null(bindings.Get("addVehicle", 0));
        Assert.False(bindings.TryGetAction(Key.Space, false, out _));
    }

    [Fact]
    public void Overrides_RoundTrip_AndRestoreDefaultsUndoesThem()
    {
        var bindings = Create();
        bindings.Set("pause", 0, new KeyChord(Key.P, false));
        var saved = bindings.Overrides();
        Assert.Equal(["P", ""], saved["pause"]);
        Assert.Single(saved);

        var loaded = Create();
        loaded.ApplyOverrides(saved);
        Assert.Equal(new KeyChord(Key.P, false), loaded.Get("pause", 0));

        loaded.RestoreDefaults();
        Assert.Empty(loaded.Overrides());
        Assert.Equal(new KeyChord(Key.Space, false), loaded.Get("pause", 0));
    }

    [Fact]
    public void UnknownActionsAndKeys_AreIgnored()
    {
        var bindings = Create();
        var ignored = new List<string>();
        bindings.ApplyOverrides(new Dictionary<string, string[]> { ["noSuchAction"] = ["X"], ["pause"] = ["NotAKey"] }, ignored.Add);
        Assert.Equal(2, ignored.Count);
        Assert.Equal(new KeyChord(Key.Space, false), bindings.Get("pause", 0));
    }
}

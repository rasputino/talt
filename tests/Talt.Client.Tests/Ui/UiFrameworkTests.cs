using System.Numerics;
using Silk.NET.Input;
using Talt.Client.Input;
using Talt.Client.Ui;
using Talt.Client.Ui.Font;
using Talt.Client.Ui.Widgets;
using Talt.Client.Ui.Windows;
using Button = Talt.Client.Ui.Widgets.Button;

namespace Talt.Client.Tests.Ui;

public class FontAtlasTests
{
    [Fact]
    public void Font_HasEverySpanishAndCurrencyCharacter()
    {
        // FR-023: á é í ó ú ü ñ ¿ ¡ €, typographic quotes and the supported currency symbols.
        const string required = "áéíóúüñÁÉÍÓÚÜÑ¿¡€£¥₹₩₽₺“”«»–—…ł";
        foreach (var c in required)
        {
            Assert.True(UiTestKit.Font.Contains(c), $"missing glyph U+{(int)c:X4} '{c}'");
        }
    }

    [Fact]
    public void Measure_GrowsWithTextAndSize()
    {
        var font = UiTestKit.Font;
        var small = TextLayout.Measure(font, "Hola", 16);
        Assert.True(small > 0);
        Assert.True(TextLayout.Measure(font, "Hola mundo", 16) > small);
        Assert.Equal(small * 2, TextLayout.Measure(font, "Hola", 32), 3);
    }

    [Fact]
    public void Wrap_BreaksAtSpacesWithinTheWidth()
    {
        var font = UiTestKit.Font;
        var lines = new List<(int Start, int Length)>();
        const string text = "one two three four five six seven";
        TextLayout.WrapLines(font, text, 16, 80, lines);
        Assert.True(lines.Count > 1);
        foreach (var (start, length) in lines)
        {
            Assert.True(TextLayout.Measure(font, text.AsSpan(start, length), 16) <= 80.01f);
        }
    }

    [Fact]
    public void Ellipsis_CutsTextThatDoesNotFit()
    {
        var font = UiTestKit.Font;
        var fit = TextLayout.FitWithEllipsis(font, "A rather long button label", 16, 60);
        Assert.InRange(fit, 1, 20);
        Assert.Equal(5, TextLayout.FitWithEllipsis(font, "Short", 16, 500));
    }
}

public class LayoutTests
{
    [Fact]
    public void VerticalStack_PlacesChildrenWithSpacing()
    {
        var ui = UiTestKit.CreateContext();
        var stack = new StackPanel(Orientation.Vertical) { Spacing = 10 };
        var a = stack.Add(new Spacer { Height = 20 });
        var b = stack.Add(new Spacer { Height = 30 });
        ui.Screen = stack;
        ui.Layout();
        Assert.Equal(0, a.Bounds.Y);
        Assert.Equal(30, b.Bounds.Y);
        Assert.Equal(1600, b.Bounds.Width);
    }

    [Fact]
    public void Fill_TakesTheRemainingSpace()
    {
        var ui = UiTestKit.CreateContext();
        var row = new StackPanel(Orientation.Horizontal) { Spacing = 0 };
        row.Add(new Spacer { Width = 100 });
        var fill = row.Add(new Spacer { Fill = true });
        row.Add(new Spacer { Width = 200 });
        ui.Screen = row;
        ui.Layout();
        Assert.Equal(100, fill.Bounds.X);
        Assert.Equal(1300, fill.Bounds.Width);
    }

    [Fact]
    public void Alignment_CentersAndPadsPanels()
    {
        var ui = UiTestKit.CreateContext();
        var root = new Panel { Style = PanelStyle.None };
        var panel = root.Add(new Panel { Width = 200, Height = 100, HorizontalAlignment = Alignment.Center, VerticalAlignment = Alignment.Center, Padding = new Thickness(10) });
        var inner = panel.Add(new Spacer());
        ui.Screen = root;
        ui.Layout();
        Assert.Equal(new RectF(700, 400, 200, 100), panel.Bounds);
        Assert.Equal(new RectF(710, 410, 180, 80), inner.Bounds);
    }

    [Fact]
    public void Scale_MultipliesLogicalSizes()
    {
        var ui = UiTestKit.CreateContext(scale: 2f);
        var spacer = new Spacer { Width = 50, Height = 20, HorizontalAlignment = Alignment.Start, VerticalAlignment = Alignment.Start };
        ui.Screen = spacer;
        ui.Layout();
        Assert.Equal(new RectF(0, 0, 100, 40), spacer.Bounds);
    }

    [Fact]
    public void ZeroSizeViewport_DoesNotLayOutOrDraw()
    {
        var ui = UiTestKit.CreateContext(width: 0, height: 0);
        ui.Screen = new Button(TextRef.Of("menu.quit"));
        var list = new UiDrawList();
        UiTestKit.Frame(ui, list);
        Assert.Equal(0, list.QuadCount);
    }

    [Fact]
    public void Label_UsesTheCurrentLanguage()
    {
        var ui = UiTestKit.CreateContext();
        var label = new Label(TextRef.Of("menu.quit"));
        Assert.Equal("Quit", label.CurrentText(ui));
        ui.SetStrings(Talt.Client.Localization.Strings.Load("es"));
        Assert.Equal("Salir", label.CurrentText(ui));
    }
}

public class DrawListTests
{
    [Fact]
    public void SteadyFrames_DoNotAllocate()
    {
        // FR-029: once warmed up, laying out and drawing the same interface allocates nothing.
        var ui = UiTestKit.CreateContext();
        var stack = new StackPanel(Orientation.Vertical);
        stack.Add(new Label(TextRef.Of("menu.subtitle")));
        stack.Add(new Button(TextRef.Of("menu.newGame"), icon: "pause"));
        stack.Add(new Label(TextRef.Of("sample.text")) { Wrap = true, Width = 120 });
        ui.Screen = stack;
        for (var i = 0; i < 8; i++)
        {
            ui.Windows.Open(Talt.Client.Screens.SampleWindows.Create(i));
        }

        var list = new UiDrawList();
        for (var i = 0; i < 5; i++)
        {
            UiTestKit.Frame(ui, list);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 20; i++)
        {
            ui.Update(0.016);
            UiTestKit.Frame(ui, list);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.True(list.QuadCount > 100);
    }

    [Fact]
    public void Clip_SplitsDrawCommandsAndSkipsHiddenQuads()
    {
        var list = new UiDrawList();
        list.Begin(800, 600);
        list.AddRect(new RectF(0, 0, 10, 10), UiColor.White);
        list.PushClip(new RectF(100, 100, 50, 50));
        list.AddRect(new RectF(110, 110, 10, 10), UiColor.White);
        list.AddRect(new RectF(500, 500, 10, 10), UiColor.White); // Outside the clip: skipped.
        list.PopClip();
        list.End();
        Assert.Equal(2, list.QuadCount);
        Assert.Equal(2, list.Commands.Length);
        Assert.Equal(new RectF(100, 100, 50, 50), list.Commands[1].Clip);
    }
}

public class InputRouterTests
{
    private sealed class FakeWorld : IWorldInput
    {
        public List<string> Events { get; } = [];

        public void MouseDown(MouseButton button, Vector2 position) => Events.Add($"down {button}");

        public void Drag(Vector2 position, Vector2 delta) => Events.Add("drag");

        public void MouseUp(MouseButton button, Vector2 position, bool isClick) => Events.Add($"up {button} {(isClick ? "click" : "drag")}");

        public void Scroll(float delta) => Events.Add("scroll");
    }

    private static (UiContext Ui, InputRouter Router, FakeWorld World, List<string> Actions, Button Button) Create()
    {
        var ui = UiTestKit.CreateContext();
        var root = new Panel { Style = PanelStyle.None };
        var clicks = new List<string>();
        var button = root.Add(new Button(TextRef.Of("menu.quit"), () => clicks.Add("clicked"))
        {
            Width = 100,
            Height = 40,
            HorizontalAlignment = Alignment.Start,
            VerticalAlignment = Alignment.Start,
        });
        ui.Screen = root;
        ui.Layout();
        var world = new FakeWorld();
        var actions = new List<string>();
        var router = new InputRouter(ui, new KeyBindings(KeyBindings.LoadDefinitions()), world, (a, _) => actions.Add(a));
        button.OnClick = () => actions.Add("button");
        return (ui, router, world, actions, button);
    }

    [Fact]
    public void ClickOnInterface_NeverReachesTheWorld()
    {
        var (_, router, world, actions, _) = Create();
        router.MouseMove(new Vector2(20, 20));
        router.MouseDown(MouseButton.Left, new Vector2(20, 20));
        router.MouseUp(MouseButton.Left, new Vector2(20, 20));
        Assert.Empty(world.Events);
        Assert.Equal(["button"], actions);
    }

    [Fact]
    public void ClickOnMap_IsAWorldClick_DragIsNot()
    {
        var (_, router, world, _, _) = Create();
        router.MouseDown(MouseButton.Left, new Vector2(500, 500));
        router.MouseUp(MouseButton.Left, new Vector2(502, 501));
        router.MouseDown(MouseButton.Left, new Vector2(500, 500));
        router.MouseMove(new Vector2(560, 500));
        router.MouseUp(MouseButton.Left, new Vector2(560, 500));
        Assert.Equal(["down Left", "up Left click", "down Left", "drag", "up Left drag"], world.Events);
    }

    [Fact]
    public void DragFromMapReleasedOverInterface_DoesNotClickIt()
    {
        var (_, router, world, actions, _) = Create();
        router.MouseDown(MouseButton.Left, new Vector2(500, 500));
        router.MouseMove(new Vector2(20, 20));
        router.MouseUp(MouseButton.Left, new Vector2(20, 20));
        Assert.Empty(actions);
        Assert.Equal("up Left drag", world.Events[^1]);
    }

    [Fact]
    public void Keys_MapToActions_ExceptWhileTyping()
    {
        var (ui, router, _, actions, _) = Create();
        router.KeyDown(Key.Space, false);
        router.KeyDown(Key.Delete, true);
        Assert.Equal(["pause", "closeAllWindows"], actions);

        var input = new TextInput("1", 9, char.IsAsciiDigit) { HorizontalAlignment = Alignment.End, VerticalAlignment = Alignment.Start };
        ((Panel)ui.Screen!).Add(input);
        ui.Layout();
        router.MouseDown(MouseButton.Left, input.Bounds.Center);
        router.MouseUp(MouseButton.Left, input.Bounds.Center);
        Assert.Same(input, ui.Focused);
        router.KeyDown(Key.Space, false);
        router.Char('7');
        Assert.Equal(2, actions.Count); // FR-018: no shortcut while a text input has focus.
        Assert.Equal("17", input.Text);
    }

    [Fact]
    public void Scroll_OverMapZooms_OverInterfaceDoesNot()
    {
        var (_, router, world, _, _) = Create();
        router.MouseMove(new Vector2(20, 20));
        router.Scroll(1);
        router.MouseMove(new Vector2(800, 800));
        router.Scroll(1);
        Assert.Equal(["scroll"], world.Events);
    }
}

public class WindowManagerTests
{
    private static UiWindow Window(string kind = "test", string? key = null) =>
        new(kind, TextRef.Of("settings.title")) { Key = key, Content = new Spacer { Width = 200, Height = 100 } };

    [Fact]
    public void ClickingAWindowBehind_BringsItToFront()
    {
        var ui = UiTestKit.CreateContext();
        var back = ui.Windows.Open(Window());
        var front = ui.Windows.Open(Window());
        ui.Layout();
        Assert.Same(front, ui.Windows.Front);
        ui.MouseDown(MouseButton.Left, back.Bounds.Position + new Vector2(5, 60));
        ui.MouseUp(MouseButton.Left, back.Bounds.Position + new Vector2(5, 60));
        Assert.Same(back, ui.Windows.Front);
    }

    [Fact]
    public void SameKind_OpensCascaded_SameKey_FocusesTheOpenOne()
    {
        var ui = UiTestKit.CreateContext();
        var a = ui.Windows.Open(Window(key: "tile:1,1"));
        var b = ui.Windows.Open(Window(key: "tile:2,2"));
        var again = ui.Windows.Open(Window(key: "tile:1,1"));
        ui.Layout();
        Assert.Same(a, again);
        Assert.Equal(2, ui.Windows.Count);
        Assert.NotEqual(a.Position, b.Position);
        Assert.Same(a, ui.Windows.Front);
    }

    [Fact]
    public void CloseAll_KeepsPinnedWindows()
    {
        var ui = UiTestKit.CreateContext();
        var pinned = ui.Windows.Open(Window());
        pinned.Pinned = true;
        ui.Windows.Open(Window());
        ui.Windows.Open(Window());
        ui.Windows.CloseAll();
        Assert.Equal(1, ui.Windows.Count);
        Assert.Same(pinned, ui.Windows.Front);
    }

    [Fact]
    public void DraggingByTheTitleBar_MovesTheWindow_AndKeepsItOnScreen()
    {
        var ui = UiTestKit.CreateContext();
        var window = ui.Windows.Open(Window());
        ui.Layout();
        var start = window.Position;
        var grab = window.Bounds.Position + new Vector2(20, 10);
        ui.MouseDown(MouseButton.Left, grab);
        ui.MouseMove(grab + new Vector2(100, 50));
        ui.MouseUp(MouseButton.Left, grab + new Vector2(100, 50));
        Assert.Equal(start + new Vector2(100, 50), window.Position);

        ui.MouseDown(MouseButton.Left, window.Bounds.Position + new Vector2(20, 10));
        ui.MouseMove(new Vector2(-5000, -5000));
        ui.MouseUp(MouseButton.Left, new Vector2(-5000, -5000));
        ui.Layout();
        Assert.True(window.Position.Y >= 0);
        Assert.True(window.Bounds.Right >= WindowManager.KeepVisible - 1);
    }

    [Fact]
    public void TooManyWindows_ReplaceTheOldestUnpinned()
    {
        var ui = UiTestKit.CreateContext();
        var first = ui.Windows.Open(Window());
        for (var i = 0; i < WindowManager.MaxWindows; i++)
        {
            ui.Windows.Open(Window());
        }

        Assert.Equal(WindowManager.MaxWindows, ui.Windows.Count);
        Assert.False(ui.Windows.IsOpen(first));
    }

    [Fact]
    public void Tooltip_AppearsAfterTheDelay_AndStaysOnScreen()
    {
        var ui = UiTestKit.CreateContext();
        var button = new Button(TextRef.Of("menu.quit")) { Tooltip = TextRef.Of("menu.settings.tooltip"), HorizontalAlignment = Alignment.End, VerticalAlignment = Alignment.End, Width = 60 };
        ui.Screen = button;
        ui.Layout();
        ui.MouseMove(button.Bounds.Center);
        ui.Update(0.1);
        Assert.False(ui.IsTooltipVisible);
        ui.Update(0.5);
        Assert.True(ui.IsTooltipVisible);
        ui.Layout();
        var tip = ui.TooltipPanel.Bounds;
        Assert.True(tip.Right <= 1600 && tip.Bottom <= 900 && tip.X >= 0 && tip.Y >= 0);
    }
}

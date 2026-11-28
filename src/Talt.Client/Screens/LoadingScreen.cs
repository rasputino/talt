using Talt.Client.Ui;
using Talt.Client.Ui.Widgets;

namespace Talt.Client.Screens;

/// <summary>Shown while a save is read and its world is built (FR-025): a title and a progress bar.</summary>
internal sealed class LoadingScreen : IScreen
{
    public LoadingScreen(GenerationProgress progress)
    {
        ArgumentNullException.ThrowIfNull(progress);
        var root = new Panel { Style = PanelStyle.Overlay };
        var column = root.Add(new StackPanel(Orientation.Vertical)
        {
            Spacing = 12,
            HorizontalAlignment = Alignment.Center,
            VerticalAlignment = Alignment.Center,
        });
        column.Add(new Label(TextRef.Of("load.progress"), TextSize.Heading) { HorizontalAlignment = Alignment.Center });
        column.Add(new ProgressBar(() => progress.Fraction) { Width = 360, HorizontalAlignment = Alignment.Center });
        Root = root;
    }

    public Widget Root { get; }

    public bool ShowsWorld => false;

    public void Update(double deltaSeconds)
    {
    }

    public bool HandleAction(string actionId, bool shift) => false;
}

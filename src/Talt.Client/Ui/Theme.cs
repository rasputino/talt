namespace Talt.Client.Ui;

/// <summary>Font sizes of the theme, in logical pixels.</summary>
internal sealed record FontSizes(float Small, float Body, float Title, float Heading);

/// <summary>
/// The interface's single visual theme (FR-019), loaded from <c>Ui/Data/theme.json</c>
/// (data-model.md Theme).
/// </summary>
internal sealed class Theme
{
    private const string FileName = "theme.json";

    private static readonly string[] RequiredColors =
    [
        "panel", "panelBorder", "titleBar", "text", "textMuted", "accent", "button", "buttonHover",
        "buttonPressed", "buttonDisabled", "buttonSelected", "tooltip", "overlay", "input",
        "highlightHover", "highlightSelection", "background",
    ];

    public required UiColor Panel { get; init; }
    public required UiColor PanelBorder { get; init; }
    public required UiColor TitleBar { get; init; }
    public required UiColor Text { get; init; }
    public required UiColor TextMuted { get; init; }
    public required UiColor Accent { get; init; }
    public required UiColor Button { get; init; }
    public required UiColor ButtonHover { get; init; }
    public required UiColor ButtonPressed { get; init; }
    public required UiColor ButtonDisabled { get; init; }
    public required UiColor ButtonSelected { get; init; }
    public required UiColor Tooltip { get; init; }
    public required UiColor Overlay { get; init; }
    public required UiColor Input { get; init; }
    public required UiColor HighlightHover { get; init; }
    public required UiColor HighlightSelection { get; init; }

    /// <summary>Construction preview: tiles that would change (feature 006).</summary>
    public required UiColor PreviewValid { get; init; }

    /// <summary>Construction preview: tiles that block the action.</summary>
    public required UiColor PreviewInvalid { get; init; }

    /// <summary>Construction preview: tiles the action leaves out (objects of others when clearing).</summary>
    public required UiColor PreviewSkipped { get; init; }

    /// <summary>Construction preview: pieces a removal tool would take away (feature 007 hand test).</summary>
    public required UiColor PreviewRemove { get; init; }

    /// <summary>Construction preview: tiles of the dragged area that do not change.</summary>
    public required UiColor PreviewArea { get; init; }
    public required UiColor Background { get; init; }

    public required float CornerRadius { get; init; }
    public required float BorderWidth { get; init; }
    public required float Spacing { get; init; }
    public required float Padding { get; init; }
    public required FontSizes FontSizes { get; init; }
    public required float TooltipDelaySeconds { get; init; }
    public required float TopBarHeight { get; init; }
    public required float ToolbarWidth { get; init; }
    public required float ButtonHeight { get; init; }
    public required float IconSize { get; init; }

    /// <summary>Loads and validates the embedded theme.</summary>
    public static Theme Load() => FromData(DataFiles.Load<ThemeData>(FileName));

    internal static Theme FromData(ThemeData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var colors = data.Colors ?? throw DataFiles.Invalid(FileName, "missing 'colors'.");
        foreach (var name in RequiredColors)
        {
            if (!colors.TryGetValue(name, out var text) || !UiColor.TryParse(text, out _))
            {
                throw DataFiles.Invalid(FileName, $"colour '{name}' is missing or not #RRGGBB[AA].");
            }
        }

        var sizes = data.FontSizes ?? throw DataFiles.Invalid(FileName, "missing 'fontSizes'.");
        if (sizes.Small <= 0 || sizes.Body <= 0 || sizes.Title <= 0 || sizes.Heading <= 0)
        {
            throw DataFiles.Invalid(FileName, "font sizes must be positive.");
        }

        if (data.CornerRadius < 0 || data.BorderWidth < 0 || data.Spacing < 0 || data.Padding < 0
            || data.TopBarHeight <= 0 || data.ToolbarWidth <= 0 || data.ButtonHeight <= 0 || data.IconSize <= 0
            || data.TooltipDelaySeconds < 0)
        {
            throw DataFiles.Invalid(FileName, "sizes must not be negative and bar, button and icon sizes must be positive.");
        }

        UiColor C(string name) => UiColor.Parse(colors[name]);
        return new Theme
        {
            Panel = C("panel"),
            PanelBorder = C("panelBorder"),
            TitleBar = C("titleBar"),
            Text = C("text"),
            TextMuted = C("textMuted"),
            Accent = C("accent"),
            Button = C("button"),
            ButtonHover = C("buttonHover"),
            ButtonPressed = C("buttonPressed"),
            ButtonDisabled = C("buttonDisabled"),
            ButtonSelected = C("buttonSelected"),
            Tooltip = C("tooltip"),
            Overlay = C("overlay"),
            Input = C("input"),
            HighlightHover = C("highlightHover"),
            HighlightSelection = C("highlightSelection"),
            PreviewValid = C("previewValid"),
            PreviewInvalid = C("previewInvalid"),
            PreviewSkipped = C("previewSkipped"),
            PreviewRemove = C("previewRemove"),
            PreviewArea = C("previewArea"),
            Background = C("background"),
            CornerRadius = data.CornerRadius,
            BorderWidth = data.BorderWidth,
            Spacing = data.Spacing,
            Padding = data.Padding,
            FontSizes = sizes,
            TooltipDelaySeconds = data.TooltipDelaySeconds,
            TopBarHeight = data.TopBarHeight,
            ToolbarWidth = data.ToolbarWidth,
            ButtonHeight = data.ButtonHeight,
            IconSize = data.IconSize,
        };
    }

    /// <summary>JSON shape of theme.json.</summary>
    internal sealed class ThemeData
    {
        public Dictionary<string, string>? Colors { get; set; }
        public float CornerRadius { get; set; }
        public float BorderWidth { get; set; }
        public float Spacing { get; set; }
        public float Padding { get; set; }
        public FontSizes? FontSizes { get; set; }
        public float TooltipDelaySeconds { get; set; }
        public float TopBarHeight { get; set; }
        public float ToolbarWidth { get; set; }
        public float ButtonHeight { get; set; }
        public float IconSize { get; set; }
    }
}

using System.Globalization;
using Silk.NET.Input;
using Talt.Client.Input;
using Talt.Client.Localization;
using Talt.Client.Picking;
using Talt.Client.Ui;
using Talt.Client.Ui.Widgets;
using Talt.Client.Ui.Windows;
using Talt.Core.Content;
using Talt.Core.Industries;
using Button = Talt.Client.Ui.Widgets.Button;
using MouseButton = Silk.NET.Input.MouseButton;

namespace Talt.Client.Screens;

/// <summary>The information window of a picked object, one per object (FR-010, clarification Q5).</summary>
internal static class ObjectInfoWindow
{
    public const string Kind = "object-info";

    public static UiWindow Create(PickResult pick, GameSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        var x = pick.TileX.ToString(CultureInfo.InvariantCulture);
        var z = pick.TileZ.ToString(CultureInfo.InvariantCulture);
        var title = pick.Kind switch
        {
            PickKind.Vehicle => TextRef.Of("info.vehicle.title", ("id", pick.VehicleId.ToString(CultureInfo.InvariantCulture))),
            PickKind.Road => TextRef.Of("info.road.title"),
            PickKind.Industry => IndustryTitle(pick.IndustryId, session),
            _ => TextRef.Of("info.tile.title"),
        };
        var window = new UiWindow(Kind, title) { Key = pick.Key };
        var content = new StackPanel(Orientation.Vertical) { Spacing = 4 };
        if (pick.Kind == PickKind.Vehicle)
        {
            var id = pick.VehicleId;
            content.Add(new Label(TextRef.Dynamic(() => session.Simulation.CurrentTick, s =>
            {
                var (vx, vz) = VehicleTile(session, id);
                return vx < 0 ? s.Get("info.vehicle.gone") : s.Format("info.position", ("x", vx.ToString(CultureInfo.InvariantCulture)), ("z", vz.ToString(CultureInfo.InvariantCulture)));
            })));
            content.Add(new Label(TextRef.Of("info.vehicle.note")) { Color = TextColor.Muted, Wrap = true, Width = 240 });
        }
        else if (pick.Kind == PickKind.Industry)
        {
            AddIndustry(content, pick.IndustryId, session);
        }
        else
        {
            content.Add(new Label(TextRef.Of("info.position", ("x", x), ("z", z))));
            content.Add(new Label(TextRef.Dynamic(() => 0, s => s.Format("info.terrain", ("terrain", s.Get($"material.{pick.Material}"))))));
            content.Add(new Label(TextRef.Of("info.height", ("height", pick.Height))));
        }

        window.Content = content;
        return window;
    }

    private static TextRef IndustryTitle(int industryId, GameSession session)
    {
        var industries = session.Simulation.Industries;
        var slot = industries.SlotOf(industryId);
        return slot < 0
            ? TextRef.Of("info.industry.title")
            : TextRef.Of(industries.Content.IndustryKind(industries.KindIndexAt(slot)).NameKey);
    }

    /// <summary>Industry window: production, acceptance, recipes, stock and last month's figures (FR-015).</summary>
    private static void AddIndustry(StackPanel content, int industryId, GameSession session)
    {
        var industries = session.Simulation.Industries;
        if (industries.SlotOf(industryId) < 0)
        {
            content.Add(new Label(TextRef.Of("info.industry.gone")) { Color = TextColor.Muted, Wrap = true, Width = 280 });
            return;
        }

        content.Add(new Label(TextRef.Dynamic(() => session.Simulation.CurrentTick, s => FormatRate(s, industryId, session))) { Color = TextColor.Muted });

        if (!industries.TryGetIndustry(industryId, out var info))
        {
            content.Add(new Label(TextRef.Of("info.industry.gone")) { Color = TextColor.Muted, Wrap = true, Width = 280 });
            return;
        }

        if (info.Production.Count == 0)
        {
            content.Add(new Label(TextRef.Of("info.industry.production.none")) { Color = TextColor.Muted });
        }
        else
        {
            foreach (var production in info.Production)
            {
                var entry = production;
                content.Add(new Label(TextRef.Dynamic(() => session.Simulation.CurrentTick, s => FormatProduction(s, entry, industryId, session))));
            }
        }

        content.Add(new Label(TextRef.Dynamic(() => session.Simulation.CurrentTick, s => FormatAccepts(s, info, industryId, session))));
        foreach (var recipe in info.Recipes)
        {
            var entry = recipe;
            content.Add(new Label(TextRef.Dynamic(() => 0, s => FormatRecipe(s, entry, session))));
        }

        foreach (var cargo in info.Cargo)
        {
            var cargoId = cargo.CargoId;
            content.Add(new Label(TextRef.Dynamic(() => session.Simulation.CurrentTick, s => FormatStock(s, cargoId, industryId, session))));
            content.Add(new Label(TextRef.Dynamic(() => session.Simulation.CurrentTick, s => FormatLastMonth(s, cargoId, industryId, session))) { Color = TextColor.Muted });
        }

        foreach (var cargo in info.Cargo)
        {
            var definition = session.Simulation.Content.Cargo(session.Simulation.Content.CargoIndex(cargo.CargoId));
            var properties = TextRef.Dynamic(() => 0, s => s.Format(
                "info.cargo.properties",
                ("cargo", s.Get($"cargo.{cargo.CargoId}")),
                ("category", s.Get(CategoryKey(definition.Category))),
                ("unit", s.Get(definition.UnitKey)),
                ("decay", definition.ValueDecayPerDayPerMille.ToString(CultureInfo.InvariantCulture)),
                ("floor", definition.MinimumValuePercent.ToString(CultureInfo.InvariantCulture)),
                ("vehicles", VehicleNames(definition.VehicleKinds, s))));
            content.Add(new Label(properties) { Color = TextColor.Muted, Wrap = true, Width = 320 });
        }
    }

    private static string FormatRate(Strings s, int industryId, GameSession session)
    {
        var industries = session.Simulation.Industries;
        var slot = industries.SlotOf(industryId);
        return slot < 0
            ? s.Get("info.industry.gone")
            : s.Format("info.industry.rate", ("percent", (industries.RatePerMilleAt(slot) / 10).ToString(CultureInfo.InvariantCulture)));
    }

    private static string FormatProduction(Strings s, ProductionEntry production, int industryId, GameSession session)
    {
        var industries = session.Simulation.Industries;
        var slot = industries.SlotOf(industryId);
        if (slot < 0)
        {
            return s.Get("info.industry.gone");
        }

        var cargo = session.Simulation.Content.Cargo(session.Simulation.Content.CargoIndex(production.CargoId));
        return s.Format(
            "info.industry.production",
            ("amount", industries.EffectiveProductionPerMonth(slot, session.Simulation.Content.CargoIndex(production.CargoId)).ToString(CultureInfo.InvariantCulture)),
            ("unit", s.Get(cargo.UnitKey)),
            ("cargo", s.Get($"cargo.{production.CargoId}")));
    }

    private static string FormatAccepts(Strings s, IndustryInfo info, int industryId, GameSession session)
    {
        var slot = session.Simulation.Industries.SlotOf(industryId);
        if (slot < 0)
        {
            return s.Get("info.industry.gone");
        }

        if (info.Accepts.Count == 0)
        {
            return s.Get("info.industry.accepts.none");
        }

        var names = new List<string>(info.Accepts.Count);
        foreach (var cargo in info.Accepts)
        {
            names.Add(s.Get($"cargo.{cargo}"));
        }

        return s.Format("info.industry.accepts", ("cargos", string.Join(", ", names)));
    }

    private static string FormatRecipe(Strings s, RecipeDefinition recipe, GameSession session)
    {
        var content = session.Simulation.Content;
        var input = content.Cargo(content.CargoIndex(recipe.InputCargoId));
        var output = content.Cargo(content.CargoIndex(recipe.OutputCargoId));
        return s.Format(
            "info.industry.recipe",
            ("inputAmount", recipe.InputAmount.ToString(CultureInfo.InvariantCulture)),
            ("inputUnit", s.Get(input.UnitKey)),
            ("input", s.Get($"cargo.{recipe.InputCargoId}")),
            ("outputAmount", recipe.OutputAmount.ToString(CultureInfo.InvariantCulture)),
            ("outputUnit", s.Get(output.UnitKey)),
            ("output", s.Get($"cargo.{recipe.OutputCargoId}")));
    }

    private static string FormatStock(Strings s, string cargoId, int industryId, GameSession session)
    {
        var industries = session.Simulation.Industries;
        var slot = industries.SlotOf(industryId);
        if (slot < 0)
        {
            return s.Get("info.industry.gone");
        }

        var index = session.Simulation.Content.CargoIndex(cargoId);
        var cargo = session.Simulation.Content.Cargo(index);
        var available = industries.AvailableAt(slot, index);
        var pending = industries.PendingInputAt(slot, index);
        var availableText = s.Format(
            "info.industry.available",
            ("amount", available.ToString(CultureInfo.InvariantCulture)),
            ("unit", s.Get(cargo.UnitKey)),
            ("cargo", s.Get($"cargo.{cargoId}")));
        return pending > 0
            ? availableText + "  " + s.Format(
                "info.industry.pending",
                ("amount", pending.ToString(CultureInfo.InvariantCulture)),
                ("unit", s.Get(cargo.UnitKey)),
                ("cargo", s.Get($"cargo.{cargoId}")))
            : availableText;
    }

    private static string FormatLastMonth(Strings s, string cargoId, int industryId, GameSession session)
    {
        var industries = session.Simulation.Industries;
        var slot = industries.SlotOf(industryId);
        if (slot < 0)
        {
            return s.Get("info.industry.gone");
        }

        var index = session.Simulation.Content.CargoIndex(cargoId);
        return s.Format(
            "info.industry.lastMonth",
            ("accepted", industries.AcceptedLastMonthAt(slot, index).ToString(CultureInfo.InvariantCulture)),
            ("shipped", industries.ShippedLastMonthAt(slot, index).ToString(CultureInfo.InvariantCulture)),
            ("cargo", s.Get($"cargo.{cargoId}")));
    }

    private static string CategoryKey(CargoCategory category) => category switch
    {
        CargoCategory.RawMaterial => "cargo.category.raw",
        CargoCategory.ProcessedGood => "cargo.category.processed",
        CargoCategory.Passenger => "cargo.category.passenger",
        CargoCategory.Mail => "cargo.category.mail",
        _ => "cargo.category.goods",
    };

    private static string VehicleNames(VehicleKind kinds, Strings s)
    {
        var names = new List<string>(4);
        if ((kinds & VehicleKind.Road) != 0)
        {
            names.Add(s.Get("vehicle.road"));
        }

        if ((kinds & VehicleKind.Rail) != 0)
        {
            names.Add(s.Get("vehicle.rail"));
        }

        if ((kinds & VehicleKind.Ship) != 0)
        {
            names.Add(s.Get("vehicle.ship"));
        }

        if ((kinds & VehicleKind.Air) != 0)
        {
            names.Add(s.Get("vehicle.air"));
        }

        return string.Join(", ", names);
    }

    private static (int X, int Z) VehicleTile(GameSession session, int id)
    {
        var ids = session.InstanceIds;
        var vehicles = session.Instances;
        for (var v = 0; v < ids.Length; v++)
        {
            if (ids[v] == id)
            {
                return ((int)MathF.Floor(vehicles[v].Position.X), (int)MathF.Floor(vehicles[v].Position.Z));
            }
        }

        return (-1, -1);
    }
}

/// <summary>
/// Settings (FR-025): language, currency, interface scale, fullscreen, vertical sync, fast-forward
/// unlock and key bindings. Every change applies and is saved at once (clarification Q4).
/// </summary>
internal static class SettingsWindow
{
    public const string Kind = "settings";

    public static UiWindow Create(IScreenHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        var settings = host.Settings;
        var window = new UiWindow(Kind, TextRef.Of("settings.title")) { SingleInstance = true };
        var form = new StackPanel(Orientation.Vertical) { Spacing = 8, Width = 460 };

        var languages = Strings.SupportedLanguages;
        Row(form, "settings.language", new Dropdown(
            languages.Select(l => new DropdownChoice(TextRef.Of($"language.{l}"), l)).ToList(),
            () => IndexOf(languages, host.Ui.Strings.Language),
            index =>
            {
                settings.Language = languages[index];
                host.ApplySettings();
            }));

        var currencies = host.Money.Currencies;
        Row(form, "settings.currency", new Dropdown(
            currencies.Select(c => new DropdownChoice(TextRef.Of("settings.currency.value", ("name", new LocalizedName(c.NameKey)), ("symbol", c.Symbol)), c.Code)).ToList(),
            () => IndexOf(currencies, host.Currency),
            index =>
            {
                settings.Currency = currencies[index].Code;
                host.ApplySettings();
            }));

        var scales = UiContext.Scales;
        Row(form, "settings.scale", new Dropdown(
            scales.Select(s => new DropdownChoice(TextRef.Of("settings.scale.value", ("percent", ((int)(s * 100)).ToString(CultureInfo.InvariantCulture))), s)).ToList(),
            () => Array.IndexOf(scales, host.Ui.Scale),
            index =>
            {
                settings.UiScale = scales[index];
                host.ApplySettings();
            })
        {
            Tooltip = TextRef.Of("settings.scale.tooltip"),
        });

        form.Add(new Label(TextRef.Of("settings.audio"), TextSize.Title) { Color = TextColor.Accent, Margin = new Thickness(0, 8, 0, 0) });
        VolumeRow(form, "settings.masterVolume", "settings.masterVolume.tooltip", () => settings.MasterVolume, value =>
        {
            settings.MasterVolume = value;
            host.ApplySettings();
        });
        VolumeRow(form, "settings.musicVolume", "settings.musicVolume.tooltip", () => settings.MusicVolume, value =>
        {
            settings.MusicVolume = value;
            host.ApplySettings();
        });
        VolumeRow(form, "settings.effectsVolume", "settings.effectsVolume.tooltip", () => settings.EffectsVolume, value =>
        {
            settings.EffectsVolume = value;
            host.ApplySettings();
        });
        VolumeRow(form, "settings.ambientVolume", "settings.ambientVolume.tooltip", () => settings.AmbientVolume, value =>
        {
            settings.AmbientVolume = value;
            host.ApplySettings();
        });
        form.Add(new Checkbox(TextRef.Of("settings.mute"), () => settings.Muted, value =>
        {
            settings.Muted = value;
            host.ApplySettings();
        })
        {
            Tooltip = TextRef.Of("settings.mute.tooltip"),
        });

        form.Add(new Checkbox(TextRef.Of("settings.fullscreen"), () => settings.Fullscreen, value =>
        {
            settings.Fullscreen = value;
            host.ApplySettings();
        }));

        form.Add(new Checkbox(TextRef.Of("settings.vsync"), () => settings.VSync, value =>
        {
            settings.VSync = value;
            host.ApplySettings();
        })
        {
            Tooltip = TextRef.Of("settings.vsync.tooltip"),
        });
        form.Add(new Checkbox(TextRef.Of("settings.fastForward"), () => settings.FastForwardUnlocked, value =>
        {
            settings.FastForwardUnlocked = value;
            host.ApplySettings();
        })
        {
            Tooltip = TextRef.Of("settings.fastForward.tooltip"),
        });
        form.Add(new Button(TextRef.Of("settings.keyBindings"), host.OpenKeyBindings) { HorizontalAlignment = Alignment.Start, Margin = new Thickness(0, 6, 0, 0) });
        window.Content = form;
        return window;
    }

    private static void Row(StackPanel form, string labelKey, Widget editor)
    {
        var row = form.Add(new StackPanel(Orientation.Horizontal) { Spacing = 10 });
        row.Add(new Label(TextRef.Of(labelKey)) { Width = 180 });
        editor.Fill = true;
        row.Add(editor);
    }

    private static void VolumeRow(StackPanel form, string labelKey, string tooltipKey, Func<float> value, Action<float> onChanged)
    {
        var row = form.Add(new StackPanel(Orientation.Horizontal) { Spacing = 10 });
        row.Add(new Label(TextRef.Of(labelKey)) { Width = 180, Tooltip = TextRef.Of(tooltipKey) });
        row.Add(new Slider(0f, 1f, 0.05f, value, onChanged)
        {
            Fill = true,
            Tooltip = TextRef.Of(tooltipKey),
        });
        row.Add(new Label(TextRef.Dynamic(
            () => (long)(value() * 100),
            s => s.Format("settings.volume.value", ("percent", ((int)(value() * 100)).ToString(CultureInfo.InvariantCulture)))))
        {
            Width = 56,
            TextAlignment = Alignment.End,
        });
    }

    private static int IndexOf<T>(IReadOnlyList<T> list, T value)
    {
        for (var i = 0; i < list.Count; i++)
        {
            if (EqualityComparer<T>.Default.Equals(list[i], value))
            {
                return i;
            }
        }

        return -1;
    }
}

/// <summary>Display names of keys, localized where the name is a word (Space, Delete...).</summary>
internal static class KeyNames
{
    public static string Display(Strings strings, KeyChord chord)
    {
        ArgumentNullException.ThrowIfNull(strings);
        var key = chord.Key;
        var name = strings.Contains($"key.{key}") ? strings.Get($"key.{key}")
            : key is >= Key.Number0 and <= Key.Number9 ? ((int)(key - Key.Number0)).ToString(CultureInfo.InvariantCulture)
            : key.ToString();
        return chord.Shift ? strings.Format("key.withShift", ("key", name)) : name;
    }
}

/// <summary>
/// Key bindings (US6, FR-028): every action with its two keys; click a key and press a new one. A key
/// already used asks before moving it; "Restore defaults" puts back the original controls.
/// </summary>
internal static class KeyBindingsWindow
{
    public const string Kind = "key-bindings";

    public static UiWindow Create(IScreenHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        var bindings = host.Bindings;
        var window = new UiWindow(Kind, TextRef.Of("bindings.title")) { SingleInstance = true };
        var layout = new StackPanel(Orientation.Vertical) { Spacing = 8, Width = 520 };

        var prompt = new StackPanel(Orientation.Horizontal) { Spacing = 8, Visible = false };
        var promptText = prompt.Add(new Label(TextRef.Of("bindings.conflict")) { Fill = true, Wrap = true, Color = TextColor.Accent });
        (string Action, int Slot, KeyChord Chord)? pending = null;
        prompt.Add(new Button(TextRef.Of("bindings.replace"), () =>
        {
            if (pending is { } p)
            {
                Commit(host, p.Action, p.Slot, p.Chord);
            }

            pending = null;
            prompt.Visible = false;
        }));
        prompt.Add(new Button(TextRef.Of("bindings.cancel"), () =>
        {
            pending = null;
            prompt.Visible = false;
        }));

        var rows = new StackPanel(Orientation.Vertical) { Spacing = 2 };
        string? category = null;
        foreach (var action in bindings.Actions)
        {
            if (action.Category != category)
            {
                category = action.Category;
                rows.Add(new Label(TextRef.Of($"bindings.category.{category}"), TextSize.Title) { Color = TextColor.Accent, Margin = new Thickness(0, 6, 0, 2) });
            }

            var row = rows.Add(new StackPanel(Orientation.Horizontal) { Spacing = 6 });
            row.Add(new Label(TextRef.Of(action.LabelKey)) { Fill = true });
            for (var slot = 0; slot < KeyBindings.SlotsPerAction; slot++)
            {
                row.Add(new KeyCaptureButton(host, action.Id, slot, (id, s, chord) =>
                {
                    var conflict = bindings.FindConflict(chord, id);
                    if (conflict is null || bindings.Get(id, s) == chord)
                    {
                        Commit(host, id, s, chord);
                        return;
                    }

                    pending = (id, s, chord);
                    var conflictLabel = bindings.Find(conflict)!.LabelKey;
                    promptText.Text = TextRef.Dynamic(() => 0, st => st.Format("bindings.conflict",
                        ("key", KeyNames.Display(st, chord)), ("action", st.Get(conflictLabel))));
                    prompt.Visible = true;
                }) { Width = 130 });
            }
        }

        layout.Add(new Label(TextRef.Of("bindings.help")) { Color = TextColor.Muted, Wrap = true });
        layout.Add(prompt);
        layout.Add(new ScrollArea(rows) { MaxHeight = 420 });
        layout.Add(new Button(TextRef.Of("bindings.restore"), () =>
        {
            bindings.RestoreDefaults();
            host.Settings.KeyBindings = bindings.Overrides();
            host.ApplySettings();
        })
        {
            HorizontalAlignment = Alignment.Start,
            Tooltip = TextRef.Of("bindings.restore.tooltip"),
        });
        window.Content = layout;
        return window;
    }

    private static void Commit(IScreenHost host, string action, int slot, KeyChord chord)
    {
        host.Bindings.Set(action, slot, chord);
        host.Settings.KeyBindings = host.Bindings.Overrides();
        host.ApplySettings();
    }
}

/// <summary>Shows a bound key; when clicked it takes the next key pressed (Esc cancels, Backspace clears).</summary>
internal sealed class KeyCaptureButton : Button
{
    private readonly IScreenHost _host;
    private readonly string _action;
    private readonly int _slot;
    private readonly Action<string, int, KeyChord> _onCaptured;

    public KeyCaptureButton(IScreenHost host, string action, int slot, Action<string, int, KeyChord> onCaptured)
        : base(null)
    {
        _host = host;
        _action = action;
        _slot = slot;
        _onCaptured = onCaptured;
        Text = TextRef.Dynamic(
            () => host.Bindings.Version * 2L + (Capturing ? 1 : 0),
            s => Capturing ? s.Get("bindings.pressKey")
                : host.Bindings.Get(action, slot) is { } chord ? KeyNames.Display(s, chord) : s.Get("bindings.none"));
        Tooltip = TextRef.Of("bindings.key.tooltip");
        IsSelected = () => Capturing;
    }

    public bool Capturing { get; private set; }

    public override bool IsFocusable => true;

    public override bool OnMouseDown(UiContext ui, MouseButton button, System.Numerics.Vector2 position) => button == MouseButton.Left;

    public override void OnMouseUp(UiContext ui, MouseButton button, System.Numerics.Vector2 position, bool inside)
    {
        if (!inside)
        {
            ui.SetFocus(null);
        }
    }

    public override void OnFocusChanged(UiContext ui, bool focused) => Capturing = focused;

    public override bool OnKeyDown(UiContext ui, Key key, bool shift)
    {
        ArgumentNullException.ThrowIfNull(ui);
        if (key is Key.ShiftLeft or Key.ShiftRight or Key.ControlLeft or Key.ControlRight or Key.AltLeft or Key.AltRight)
        {
            return true; // Wait for the main key.
        }

        ui.SetFocus(null);
        if (key == Key.Escape)
        {
            return true; // Cancelled.
        }

        if (key == Key.Backspace)
        {
            _host.Bindings.Set(_action, _slot, null);
            _host.Settings.KeyBindings = _host.Bindings.Overrides();
            _host.ApplySettings();
            return true;
        }

        _onCaptured(_action, _slot, new KeyChord(key, shift));
        return true;
    }
}

/// <summary>Sample windows using every widget, for measuring the interface cost (<c>--ui-test-windows</c>, SC-001).</summary>
internal static class SampleWindows
{
    public const string Kind = "sample";

    public static UiWindow Create(int number)
    {
        var state = new SampleState();
        var window = new UiWindow(Kind, TextRef.Of("sample.title", ("number", number.ToString(CultureInfo.InvariantCulture))));
        var tabs = new TabControl();
        var first = new StackPanel(Orientation.Vertical) { Spacing = 6, Width = 300 };
        first.Add(new Label(TextRef.Of("sample.text")) { Wrap = true });
        first.Add(new Checkbox(TextRef.Of("sample.option"), () => state.Checked, v => state.Checked = v));
        first.Add(new Slider(0, 100, 5, () => state.Value, v => state.Value = v));
        first.Add(new Dropdown(
            [new DropdownChoice(TextRef.Of("sample.choice", ("n", "1")), 1), new DropdownChoice(TextRef.Of("sample.choice", ("n", "2")), 2)],
            () => state.Choice, i => state.Choice = i));
        first.Add(new Button(TextRef.Of("sample.button"), () => state.Clicks++, "info") { Tooltip = TextRef.Of("sample.button.tooltip") });
        var list = new ListView(90);
        for (var i = 1; i <= 6; i++)
        {
            list.AddRow(TextRef.Of("sample.row", ("n", i.ToString(CultureInfo.InvariantCulture))));
        }

        tabs.AddTab(TextRef.Of("sample.tab1"), first);
        tabs.AddTab(TextRef.Of("sample.tab2"), list);
        window.Content = tabs;
        return window;
    }

    private sealed class SampleState
    {
        public bool Checked { get; set; }
        public float Value { get; set; } = 50;
        public int Choice { get; set; }
        public int Clicks { get; set; }
    }
}

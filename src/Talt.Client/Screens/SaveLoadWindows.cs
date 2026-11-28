using System.Globalization;
using Talt.Client.Localization;
using Talt.Client.Saves;
using Talt.Client.Ui;
using Talt.Client.Ui.Widgets;
using Talt.Client.Ui.Windows;
using Talt.Core.Saves;
using Talt.Core.Simulation;

namespace Talt.Client.Screens;

/// <summary>What the save and load windows need from the client (feature 014; implemented by <see cref="GameWindow"/>).</summary>
internal interface ISaveHost
{
    UiContext Ui { get; }

    MoneyFormatter Money { get; }

    Currency Currency { get; }

    SaveStore Saves { get; }

    void ShowFileError(string messageKey);

    /// <summary>True while a game runs.</summary>
    bool InGame { get; }

    /// <summary>The suggested name of a new save (in-game date), or empty.</summary>
    string SuggestedSaveName { get; }

    /// <summary>Saves the running game under <paramref name="name"/> (after any overwrite confirmation).</summary>
    void SaveAs(string name);

    /// <summary>Loads a save (after any unsaved-progress confirmation).</summary>
    void LoadSave(string path);

    /// <summary>Asks before discarding unsaved progress, then runs <paramref name="action"/>.</summary>
    void ConfirmDiscard(Action action);
}

/// <summary>A small yes/no window (overwrite, unsaved progress, delete).</summary>
internal static class ConfirmDialog
{
    public const string Kind = "confirm";

    public static UiWindow Create(UiContext ui, TextRef title, TextRef body, TextRef yes, Action onYes)
    {
        ArgumentNullException.ThrowIfNull(ui);
        ArgumentNullException.ThrowIfNull(onYes);
        var window = new UiWindow(Kind, title) { SingleInstance = true };
        var column = new StackPanel(Orientation.Vertical) { Spacing = 10, Width = 360 };
        column.Add(new Label(body) { Wrap = true });
        var buttons = column.Add(new StackPanel(Orientation.Horizontal) { Spacing = 8, HorizontalAlignment = Alignment.End });
        buttons.Add(new Button(TextRef.Of("confirm.cancel"), () => ui.Windows.Close(window)) { ShortcutAction = "back" });
        buttons.Add(new Button(yes, () =>
        {
            ui.Windows.Close(window);
            onYes();
        })
        {
            Style = ButtonStyle.Menu,
        });
        window.Content = column;
        return window;
    }
}

/// <summary>The save window (FR-001, FR-006): a name field with the suggested name and the existing saves to overwrite.</summary>
internal static class SaveWindow
{
    public const string Kind = "save";

    public static UiWindow Create(ISaveHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        var ui = host.Ui;
        var window = new UiWindow(Kind, TextRef.Of("save.title")) { SingleInstance = true };
        var column = new StackPanel(Orientation.Vertical) { Spacing = 8, Width = 460 };
        column.Add(new Label(TextRef.Of("save.name")));
        var name = column.Add(new TextInput(host.SuggestedSaveName, SaveDetailsInput.MaxNameLength) { Fill = true });
        var list = column.Add(new ListView(240));
        var entries = host.Saves.List().Where(e => !e.IsDamaged && e.Details!.Kind == SaveKind.Manual).ToList();
        foreach (var entry in entries)
        {
            list.AddRow(LoadWindow.RowText(host, entry));
        }

        list.OnSelected = index => name.Text = entries[index].DisplayName;
        var buttons = column.Add(new StackPanel(Orientation.Horizontal) { Spacing = 8, HorizontalAlignment = Alignment.End });
        buttons.Add(new Button(TextRef.Of("confirm.cancel"), () => ui.Windows.Close(window)) { ShortcutAction = "back" });
        buttons.Add(new Button(TextRef.Of("save.confirm"), () =>
        {
            var text = name.Text.Trim();
            if (text.Length == 0)
            {
                return;
            }

            void Save()
            {
                ui.Windows.Close(window);
                host.SaveAs(text);
            }

            bool exists;
            try
            {
                exists = host.Saves.Existing(text) is not null;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                host.ShowFileError("save.folder.failed");
                return;
            }

            if (exists)
            {
                ui.Windows.Open(ConfirmDialog.Create(ui, TextRef.Of("save.overwrite.title"), TextRef.Of("save.overwrite.body", ("name", text)), TextRef.Of("save.overwrite.yes"), Save));
            }
            else
            {
                Save();
            }
        })
        {
            Style = ButtonStyle.Menu,
        });
        window.Content = column;
        return window;
    }
}

/// <summary>
/// The load window (FR-008, FR-015): every save newest first with its details, the selected save's map overview, load and
/// delete. Details come from peeking the files (FR-016); damaged saves are marked and can be deleted.
/// </summary>
internal static class LoadWindow
{
    public const string Kind = "load";

    public static UiWindow Create(ISaveHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        var ui = host.Ui;
        var window = new UiWindow(Kind, TextRef.Of("load.title")) { SingleInstance = true };
        var row = new StackPanel(Orientation.Horizontal) { Spacing = 12 };
        var left = row.Add(new StackPanel(Orientation.Vertical) { Spacing = 6, Width = 420 });
        var right = row.Add(new StackPanel(Orientation.Vertical) { Spacing = 6, Width = 260 });
        var list = left.Add(new ListView(360));
        var thumbnail = right.Add(new ThumbnailView(256));
        var selected = -1;
        IReadOnlyList<SaveCatalogEntry> entries = [];
        right.Add(new Label(TextRef.Dynamic(() => selected, s => DetailsText(host, s, selected, entries))) { Wrap = true, Width = 256 });

        void Refresh()
        {
            entries = host.Saves.List();
            list.Clear();
            foreach (var entry in entries)
            {
                list.AddRow(RowText(host, entry));
            }

            Select(-1);
        }

        void Select(int index)
        {
            selected = index;
            var thumb = index >= 0 && !entries[index].IsDamaged ? SaveGame.Peek(entries[index].Path).Thumbnail : null;
            thumbnail.Show(thumb?.Pixels, thumb?.Width ?? 0, thumb?.Height ?? 0);
        }

        list.OnSelected = Select;
        var buttons = left.Add(new StackPanel(Orientation.Horizontal) { Spacing = 8, HorizontalAlignment = Alignment.End });
        buttons.Add(new Button(TextRef.Of("load.delete"), () =>
        {
            if (selected < 0)
            {
                return;
            }

            var entry = entries[selected];
            ui.Windows.Open(ConfirmDialog.Create(ui, TextRef.Of("load.delete"), TextRef.Of("load.delete.confirm", ("name", entry.DisplayName)), TextRef.Of("load.delete"), () =>
            {
                if (SaveStore.Delete(entry.Path))
                {
                    Refresh();
                }
                else
                {
                    host.ShowFileError("load.delete.failed");
                }
            }));
        })
        {
            Enabled = true,
        });
        buttons.Add(new Button(TextRef.Of("confirm.cancel"), () => ui.Windows.Close(window)) { ShortcutAction = "back" });
        buttons.Add(new Button(TextRef.Of("load.confirm"), () =>
        {
            if (selected < 0)
            {
                return;
            }

            var path = entries[selected].Path;
            host.ConfirmDiscard(() =>
            {
                ui.Windows.Close(window);
                host.LoadSave(path);
            });
        })
        {
            Style = ButtonStyle.Menu,
        });
        Refresh();
        if (entries.Count == 0)
        {
            left.Add(new Label(TextRef.Of("load.empty")) { Color = TextColor.Muted });
        }

        window.Content = row;
        return window;
    }

    /// <summary>One list row: kind mark, name, in-game date and real save time.</summary>
    internal static TextRef RowText(ISaveHost host, SaveCatalogEntry entry)
    {
        if (entry.IsDamaged)
        {
            return TextRef.Of("load.row.damaged", ("name", entry.DisplayName));
        }

        var d = entry.Details!;
        var mark = d.Kind switch
        {
            SaveKind.Autosave => "load.autosave",
            SaveKind.QuickSave => "load.quicksave",
            _ => "load.manual",
        };
        return TextRef.Dynamic(() => 0, s => s.Format("load.row",
            ("kind", s.Get(mark)),
            ("name", d.Name),
            ("date", s.FormatDate(new GameDate(d.Year, d.Month, d.Day, 0))),
            ("saved", d.SavedAtUtc.ToLocalTime().ToString("g", s.Culture))));
    }

    private static string DetailsText(ISaveHost host, Localization.Strings s, int selected, IReadOnlyList<SaveCatalogEntry> entries)
    {
        if (selected < 0 || selected >= entries.Count)
        {
            return s.Get("load.select");
        }

        var entry = entries[selected];
        if (entry.Details is not { } d)
        {
            return s.Get(entry.Status == SavePeekStatus.TooNew ? "load.failed.tooNew" : "load.damaged");
        }

        return s.Format("load.details",
            ("name", d.Name),
            ("company", d.CompanyName.Length > 0 ? d.CompanyName : s.Get("load.company.player")),
            ("money", host.Money.Format(d.Money, host.Currency, s.Culture)),
            ("date", s.FormatDate(new GameDate(d.Year, d.Month, d.Day, 0))),
            ("saved", d.SavedAtUtc.ToLocalTime().ToString("g", s.Culture)),
            ("width", d.MapWidth.ToString(CultureInfo.InvariantCulture)),
            ("depth", d.MapDepth.ToString(CultureInfo.InvariantCulture)),
            ("version", d.GameVersion));
    }
}

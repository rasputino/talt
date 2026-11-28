using Talt.Client.Ui;
using Talt.Client.Ui.Widgets;
using Talt.Core.Vehicles;

namespace Talt.Client.Screens;

/// <summary>A compact selector for purchasing current models and maintaining rules for retired ones.</summary>
internal static class DepotCatalog
{
    private sealed record Entry(string Id, string NameKey, int First, int Last, int Kind, ulong Cargo, int Color);

    public static void Add(StackPanel parent, GameSession session, bool rail, Action<StackPanel, int> purchase)
    {
        var models = rail
            ? session.Simulation.Trains!.Rules.Models.Select(m => new Entry(m.Id, m.NameKey, m.FirstYear, m.LastYear, (int)m.Kind, m.CargoMask, m.Color)).ToArray()
            : session.RoadVehicles!.Rules.Models.Select(m => new Entry(m.Id, m.NameKey, m.FirstYear, m.LastYear, (int)m.Kind, m.CargoMask, m.Color)).ToArray();
        var company = session.Construction.Player;
        var policy = rail ? company.RailReplacements : company.RoadReplacements;
        parent.Add(new RebuildingPanel(() => session.Simulation.Date.Year, panel =>
        {
            var year = session.Simulation.Date.Year;
            var choices = models.Where(m => m.First <= year).OrderBy(m => m.Last < year).Select(m =>
                new DropdownChoice(TextRef.Dynamic(() => 0, s => m.Last >= year ? s.Get(m.NameKey) :
                    s.Format("catalog.retired", ("name", s.Get(m.NameKey)))), m)).ToList();
            if (choices.Count == 0)
            {
                return;
            }

            var selected = 0;
            panel.Add(new Dropdown(choices, () => selected, index => selected = index));
            panel.Add(new RebuildingPanel(() => selected, detail =>
            {
                var model = (Entry)choices[selected].Value;
                if (session.CatalogModels is { } library && library.TryGet((rail ? "train:" : "vehicle:") + model.Id, out var entry) && entry.Preview is { } preview)
                {
                    detail.Add(new CatalogPreview(preview, model.Color));
                }
                if (model.Last >= year)
                {
                    purchase(detail, Array.IndexOf(models, model));
                }

                var targets = new List<DropdownChoice> { new(TextRef.Of("catalog.noReplacement"), "") };
                foreach (var target in models)
                {
                    if (target.First > model.First && target.First <= year && target.Last >= year && target.Kind == model.Kind &&
                        (target.Cargo == model.Cargo || (target.Cargo & model.Cargo) != 0))
                    {
                        targets.Add(new(TextRef.Of(target.NameKey), target.Id));
                    }
                }

                ReplacementRule Current() => policy.TryGet(model.Id, out var r) ? r : new("", false, false);
                void Submit(ReplacementRule r) => session.Submit(new SetReplacementCommand(company.Id, rail, model.Id, r.Target, r.EndOfLifeOnly, r.Renew));
                detail.Add(new Label(TextRef.Of("catalog.replacement")));
                detail.Add(new Dropdown(targets, () =>
                {
                    var id = Current().Target;
                    for (var i = 1; i < targets.Count; i++)
                    {
                        if ((string)targets[i].Value == id)
                        {
                            return i;
                        }
                    }
                    return 0;
                }, i => Submit(Current() with { Target = (string)targets[i].Value })));
                detail.Add(new Checkbox(TextRef.Of("catalog.defer"), () => Current().EndOfLifeOnly, v => Submit(Current() with { EndOfLifeOnly = v }))
                { VisibleWhen = () => Current().Target.Length > 0 });
                detail.Add(new Checkbox(TextRef.Of("catalog.renew"), () => Current().Renew, v => Submit(Current() with { Renew = v })));
            }));
        }));
    }
}

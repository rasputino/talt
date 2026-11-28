namespace Talt.Core.Saves;

/// <summary>
/// The system sections in restore order (data-model.md "Sections in version 1"; ids and owners in
/// contracts/save-sections.md). Each entry delegates to the owner's <c>WriteState</c>/<c>ReadState</c> in its
/// <c>*.Save.cs</c> partial file. A layout change bumps the section version here and adds the
/// migration from the previous layout to its <c>ReadState</c> (research R11).
/// </summary>
internal static class SaveSystemSections
{
    public static IReadOnlyList<SaveSection> Create() =>
    [
        new("CNST", 2, s => s.Construction is not null, (s, w) => s.Construction!.WriteState(w), (s, r, version) => s.Construction!.ReadState(r, version)),
        new("ROAD", 1, s => s.Construction is not null,
            (s, w) =>
            {
                s.Construction!.Roads.WriteState(w);
                s.Construction.Network.WriteState(w);
            },
            (s, r, _) =>
            {
                s.Construction!.Roads.ReadState(r);
                s.Construction.Network.ReadState(r);
            }),
        new("RAIL", 2, s => s.Construction is not null && s.Trains is not null,
            (s, w) =>
            {
                var construction = s.Construction!;
                construction.Rails.WriteState(w);
                construction.RailDepots.WriteState(w);
                s.Trains!.SignalBlocks.WriteState(w, construction.Rails.EverUsed);
            },
            (s, r, version) =>
            {
                var construction = s.Construction!;
                construction.Rails.ReadState(r, version);
                construction.RailDepots.ReadState(r);
                s.Trains!.SignalBlocks.ReadState(r, construction.Rails.EverUsed, version);
            }),
        new("TOWN", 1, s => s.Towns is not null, (s, w) => s.Towns!.WriteState(w), (s, r, _) => s.Towns!.ReadState(r)),
        new("INDY", 1, _ => true, (s, w) => s.Industries.WriteState(w), (s, r, _) => s.Industries.ReadState(r)),
        new("STNS", 1, s => s.Stations is not null, (s, w) => s.Stations!.WriteState(w), (s, r, _) => s.Stations!.ReadState(r)),
        new("FINA", 1, s => s.Finance is not null, (s, w) => s.Finance!.WriteState(w), (s, r, _) => s.Finance!.ReadState(r)),
        new("RVEH", 2, s => s.RoadVehicles is not null, (s, w) => s.RoadVehicles!.WriteState(w), (s, r, version) => s.RoadVehicles!.ReadState(r, version)),
        new("TRAI", 2, s => s.Trains is not null, (s, w) => s.Trains!.WriteState(w), (s, r, version) => s.Trains!.ReadState(r, version)),
        new("CATL", 1, s => s.Trains is not null, (s, w) =>
        {
            var companies = s.Construction!.Companies;
            for (var i = 0; i < companies.Count; i++)
            {
                companies[i].RoadReplacements.WriteState(w);
                companies[i].RailReplacements.WriteState(w);
            }

            for (var i = 0; i < s.Trains!.Count; i++)
            {
                foreach (var part in s.Trains.TrainAt(i).Parts)
                {
                    w.WriteBool(part.RenewalNotified);
                }
            }
        }, (s, r, _) =>
        {
            foreach (var company in s.Construction!.Companies)
            {
                company.RoadReplacements.ReadState(r, s.RoadVehicles!.Rules.FindModel);
                company.RailReplacements.ReadState(r, s.Trains!.Rules.FindModel);
            }

            for (var i = 0; i < s.Trains!.Count; i++)
            {
                foreach (var part in s.Trains.TrainAt(i).Parts)
                {
                    part.RenewalNotified = r.ReadBool();
                }
            }
        }),
        new("LSTA", 1, s => s.Traffic is not null, (s, w) =>
        {
            s.RoadVehicles!.Lines.WriteStatistics(w);
            s.Towns!.History.WriteState(w);
            s.Stations!.History.WriteState(w);
            s.Traffic!.WriteState(w);
        }, (s, r, _) =>
        {
            s.RoadVehicles!.Lines.ReadStatistics(r);
            s.Towns!.History.ReadState(r);
            s.Stations!.History.ReadState(r);
            s.Traffic!.ReadState(r);
        }),
        new("DMND", 1, s => s.Router is not null, (s, w) => s.Router!.WriteState(w), (s, r, _) => s.Router!.ReadState(r)),
    ];
}

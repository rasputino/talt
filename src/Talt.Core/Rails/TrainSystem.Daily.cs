using Talt.Core.Finance;
using Talt.Core.Simulation;
using Talt.Core.Vehicles;

namespace Talt.Core.Rails;

/// <summary>
/// Day, month and year work (FR-023): ageing, reliability, breakdowns from a stateless hash, service due, values, depot and
/// track maintenance and the yearly profit rotation. Reliability rules come from <c>vehicles.json</c> (shared with road vehicles).
/// </summary>
public sealed partial class TrainSystem
{
    private long[] _maintenanceCarry = new long[1];

    public void AdvanceDay(GameDate date)
    {
        var rules = VehicleRules;
        foreach (var train in _byId)
        {
            if (train.Parts.Count == 0)
            {
                continue;
            }

            train.AgeDays++;
            train.DaysSinceService++;
            var life = 1000;
            long running = 0;
            foreach (var part in train.Parts)
            {
                var model = Rules.Models[part.Model];
                running += model.RunningCostPerYear;
                if (model.Kind == RollingStockKind.Locomotive)
                {
                    life = Math.Min(life, model.LifeYears);
                }
            }

            var decay = rules.ReliabilityDecayPerDay * (train.AgeDays > life * 365 ? rules.OldAgeFactor : 1);
            train.Reliability = Math.Max(rules.MinReliability, train.Reliability - decay);
            train.ProfitThisYear = Money.Add(train.ProfitThisYear, -(running / 365));
            Lines.RecordProfit(train.Line, -(running / 365));
            if (train.ServiceInterval > 0 && train.DaysSinceService >= train.ServiceInterval)
            {
                train.Orders |= VehicleOrders.ServiceDue;
            }

            if (train.Mode == TrainMode.Running && train.Speed > 0 && !train.IsBrokenDown
                && BreakdownRoll(train.Id, date.DayNumber) < (1000L - train.Reliability) * rules.BreakdownChanceScale)
            {
                train.BreakdownTicks = rules.BreakdownDays * GameClock.TicksPerDay;
                train.Orders |= VehicleOrders.Repair;
                Notify(VehicleNoticeKind.BrokenDown, train, -1);
            }
        }
    }

    /// <summary>Month work, before finance's month end: values, rail depot and track maintenance.</summary>
    public void AdvanceMonth(GameDate date)
    {
        _ = date;
        if (_finance is not null)
        {
            foreach (var train in _byId)
            {
                foreach (var part in train.Parts)
                {
                    if (part.RunningHandle >= 0)
                    {
                        _finance.RunningCosts.SetValue(part.RunningHandle, PartValue(part));
                    }
                }
            }
        }

        for (var c = 0; c < _state.Companies.Count; c++)
        {
            var company = _state.Companies[c];
            long yearly = 0;
            for (var i = 0; i < Depots.Count; i++)
            {
                if (Depots.OwnerAt(i) == company.Id)
                {
                    yearly += Rules.DepotMaintenancePerYear;
                }
            }

            for (var t = 0; t < Rules.Types.Count; t++)
            {
                yearly += _state.OwnedRailTiles(company.Id, t) * Rules.Types[t].MaintenancePerTile;
            }

            if (company.Id >= _maintenanceCarry.Length)
            {
                Array.Resize(ref _maintenanceCarry, company.Id + 1);
            }

            if (yearly == 0 && _maintenanceCarry[company.Id] == 0)
            {
                continue;
            }

            yearly = _finance?.Inflate(yearly) ?? yearly;
            var accrued = _maintenanceCarry[company.Id] + yearly;
            _maintenanceCarry[company.Id] = accrued % 12;
            if (accrued / 12 != 0)
            {
                company.Book(LedgerCategory.PropertyMaintenance, -(accrued / 12));
            }
        }
    }

    public void AdvanceYear(GameDate date)
    {
        _ = date;
        foreach (var train in _byId)
        {
            train.ProfitLastYear = train.ProfitThisYear;
            train.ProfitThisYear = 0;
        }
    }

    /// <summary>Restores reliability in a depot and ends a breakdown.</summary>
    private void Service(Train train)
    {
        var best = 0;
        foreach (var part in train.Parts)
        {
            var model = Rules.Models[part.Model];
            if (model.Kind == RollingStockKind.Locomotive)
            {
                best = best == 0 ? model.Reliability : Math.Min(best, model.Reliability);
            }
        }

        train.Reliability = Math.Max(train.Reliability, best == 0 ? 1000 : best);
        train.DaysSinceService = 0;
        train.BreakdownTicks = 0;
        Notify(VehicleNoticeKind.Serviced, train, -1);
    }

    private long BreakdownRoll(int trainId, int dayNumber)
    {
        var x = _seed ^ ((ulong)(uint)trainId * 0x9E3779B97F4A7C15UL) ^ ((ulong)(uint)dayNumber * 0xC2B2AE3D27D4EB4FUL);
        x ^= x >> 30;
        x *= 0xBF58476D1CE4E5B9UL;
        x ^= x >> 27;
        x *= 0x94D049BB133111EBUL;
        x ^= x >> 31;
        return (long)(x % 1_000_000UL);
    }
}

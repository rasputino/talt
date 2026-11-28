using Talt.Core.Construction;
using Talt.Core.Content;
using Talt.Core.Determinism;
using Talt.Core.Simulation;
using Talt.Core.Stations;

namespace Talt.Core.Finance;

/// <summary>A company's finances at a glance (for the finance window and tools).</summary>
public readonly record struct CompanyFinance(
    long Cash,
    long Loan,
    long MaxLoan,
    long LoanStep,
    int InterestPerMille,
    long Value,
    int BankruptMonths,
    int WarningMonths,
    int GameOverMonths,
    bool IsGameOver);

/// <summary>
/// The company accounts of a game with a map (feature 011, contracts/finance-api.md): pays deliveries reported by
/// the stations, charges running costs daily and maintenance and interest monthly, keeps inflation, company value
/// and bankruptcy, and rotates the ledgers. Everything is whole numbers; periodic charges keep their remainders so
/// yearly totals are exact (research R4).
/// </summary>
/// <remarks>
/// Hashes nothing until something is booked, borrowed or registered, or the settings differ from the defaults
/// (research R11), so games where the player did nothing keep their earlier fingerprints.
/// </remarks>
public sealed partial class FinanceSystem : IStateHashable, IDeliveryListener, ITransferValuer
{
    private readonly ConstructionState _state;
    private readonly StationRegistry? _stations;
    private readonly GameContent _content;
    private readonly int[] _pieceCounts;
    private readonly int[] _warehouseCounts;
    private bool _active;

    public FinanceSystem(ConstructionState state, StationRegistry? stations, GameContent content, FinanceRules rules, FinanceSettings? settings, GameDate date)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _content = content ?? throw new ArgumentNullException(nameof(content));
        Rules = rules ?? throw new ArgumentNullException(nameof(rules));
        Settings = settings ?? FinanceSettings.Default;
        _stations = stations;
        _pieceCounts = new int[rules.PieceMaintenance.Count];
        _warehouseCounts = new int[rules.WarehouseMaintenance.Count];
        if (rules.Payments.Count != content.CargoCount || rules.RoadMaintenance.Count != state.RoadRules.Types.Count
            || (stations is not null && (rules.PieceMaintenance.Count != stations.Rules.Pieces.Count || rules.WarehouseMaintenance.Count != stations.Rules.Warehouses.Count)))
        {
            throw new ArgumentException("The finance rules were not checked against this game's cargo, road and station data.", nameof(rules));
        }

        foreach (var company in state.Companies)
        {
            company.AttachLedger(new Ledger(content.CargoCount, date.Year, date.Month));
            company.Value = company.Balance;
            company.Ledger!.SetPeriodValue(company.Value);
            if (rules.StartLoan > 0)
            {
                company.Borrow(rules.StartLoan);
            }
        }

        if (Settings.InfiniteMoney)
        {
            state.Player.EnableInfiniteMoney(0);
        }

        _active = rules.StartLoan > 0 || Settings != FinanceSettings.Default;
        RunningCosts.Activated = () => _active = true;
        if (stations is not null)
        {
            stations.DeliveryListener = this;
        }
    }

    public FinanceRules Rules { get; }

    public FinanceSettings Settings { get; }

    /// <summary>Price level of costs in parts per million (inflation).</summary>
    public long CostLevel { get; private set; } = PaymentCalculator.LevelOne;

    /// <summary>Price level of payments in parts per million (inflation).</summary>
    public long PaymentLevel { get; private set; } = PaymentCalculator.LevelOne;

    /// <summary>Months of inflation applied so far.</summary>
    public int InflationMonths { get; private set; }

    /// <summary>Vehicle running costs (feature 012 registers its vehicles here).</summary>
    public RunningCosts RunningCosts { get; } = new();

    /// <summary>Events for the client's floating texts and messages; not state.</summary>
    public MoneyEventFeed Events { get; } = new();

    /// <summary>
    /// The road vehicle unloading right now (feature 012), written into income events for the client; −1 otherwise.
    /// Not state.
    /// </summary>
    public int CurrentVehicleId { get; set; } = -1;

    /// <summary>Money paid for deliveries since the start.</summary>
    public long TotalPaid { get; private set; }

    /// <summary>Deliveries paid since the start.</summary>
    public long Deliveries { get; private set; }

    /// <summary>Loan step at today's prices (rounded down to 1 000 when the step is at least 1 000).</summary>
    public long LoanStep
    {
        get
        {
            var step = Inflate(Rules.LoanStep);
            return Rules.LoanStep >= 1000 ? Math.Max(Rules.LoanStep, step / 1000 * 1000) : step;
        }
    }

    /// <summary>Largest loan at today's prices (a whole number of loan steps).</summary>
    public long MaxLoan
    {
        get
        {
            var step = LoanStep;
            return Inflate(Rules.MaxLoan) / step * step;
        }
    }

    /// <summary>A cost at today's price level.</summary>
    public long Inflate(long cost) => CostLevel == PaymentCalculator.LevelOne ? cost : Money.MulDiv(cost, CostLevel, PaymentCalculator.LevelOne);

    public bool IsGameOver(int companyId) => _state.FindCompany(companyId)?.IsGameOver ?? false;

    /// <summary>Whether the company ever used infinite money (it cannot be turned off), for goals and scores (feature 020).</summary>
    public bool UsedInfiniteMoney(int companyId) => _state.FindCompany(companyId)?.HasInfiniteMoney ?? false;

    /// <summary>A company's finances at a glance; default when the company does not exist.</summary>
    public CompanyFinance Report(int companyId)
    {
        var company = _state.FindCompany(companyId);
        return company is null
            ? default
            : new CompanyFinance(company.Balance, company.Loan, MaxLoan, LoanStep, Rules.InterestPerMille, company.Value,
                company.BankruptMonths, Rules.WarningMonths, Rules.GameOverMonths, company.IsGameOver);
    }

    /// <summary>The price of a delivery at today's payment level (pure).</summary>
    public PaymentQuote Quote(int cargo, int amount, int distance, int days)
    {
        var definition = _content.Cargo(cargo);
        var payment = Rules.Payments[cargo];
        return PaymentCalculator.Quote(payment.Rate, payment.FreeDays, definition.ValueDecayPerDayPerMille, definition.MinimumValuePercent,
            amount, distance, Rules.MinDistance, days, PaymentLevel);
    }

    /// <summary>Feature 013: what a transfer leg is worth for vehicle profit (a quote, nothing booked).</summary>
    public long Estimate(int cargo, int amount, int distanceTiles, int days) => Quote(cargo, amount, distanceTiles, days).Amount;

    /// <summary>Feature 010's report of an accepted delivery: pays it.</summary>
    public void OnDelivered(in CargoDelivery delivery) => Pay(delivery);

    /// <summary>Prices a delivery and books it as income of its cargo (FR-005 – FR-009); returns the money paid.</summary>
    public long Pay(in CargoDelivery delivery)
    {
        var company = _state.FindCompany(delivery.CompanyId);
        if (company is null || company.IsGameOver || (uint)delivery.Cargo >= (uint)_content.CargoCount || delivery.Amount <= 0)
        {
            return 0;
        }

        if (delivery.ConsumerKind != DeliveryConsumer.DebugService)
        {
            company.Ledger!.AddDelivered(delivery.Cargo, delivery.Amount);
            _active = true;
        }

        int distance;
        int x = -1, z = -1;
        _stations?.TryGetCentre(delivery.DestinationStation, out x, out z);
        if (delivery.ConsumerKind == DeliveryConsumer.DebugService)
        {
            if (!Settings.DebugServicePays)
            {
                return 0;
            }

            distance = Rules.DebugServiceDistance;
        }
        else
        {
            distance = 0;
            if (_stations is not null && _stations.TryGetCentre(delivery.OriginStation, out var ox, out var oz) && x >= 0)
            {
                distance = Math.Abs(x - ox) + Math.Abs(z - oz);
            }
        }

        var quote = Quote(delivery.Cargo, delivery.Amount, distance, delivery.DaysInTransit);
        if (quote.Amount <= 0)
        {
            return 0;
        }

        company.Book(LedgerCategory.Income, quote.Amount, delivery.Cargo);
        TotalPaid = Money.Add(TotalPaid, quote.Amount);
        Deliveries++;
        _active = true;
        Events.Add(new MoneyEvent(company.Id, quote.Amount, x, z, MoneyEventKind.Income, CurrentVehicleId));
        return quote.Amount;
    }

    /// <summary>Daily work (after the stations): vehicle running costs for the day that ended (FR-011).</summary>
    public void AdvanceDay(GameDate date)
    {
        var endedYear = date.Month == 1 && date.Day == 1 ? date.Year - 1 : date.Year;
        var days = DateTime.IsLeapYear(Math.Max(1, endedYear)) ? 366 : 365;
        for (var c = 0; c < _state.Companies.Count; c++)
        {
            var company = _state.Companies[c];
            var yearly = RunningCosts.YearlyTotal(company.Id);
            if (yearly == 0 && company.RunningCarry == 0)
            {
                continue;
            }

            var accrued = Money.Add(company.RunningCarry, Inflate(yearly));
            company.RunningCarry = accrued % days;
            var charge = accrued / days;
            if (charge != 0)
            {
                company.Book(LedgerCategory.RunningCosts, -charge);
            }
        }
    }

    /// <summary>
    /// Month-end work (FR-004, FR-010, FR-016 – FR-018, FR-022): maintenance, interest, inflation, company value and
    /// bankruptcy, booked into the month that ended; then the ledgers start the new month.
    /// </summary>
    public void AdvanceMonth(GameDate date)
    {
        for (var c = 0; c < _state.Companies.Count; c++)
        {
            var company = _state.Companies[c];
            ChargeMaintenance(company);
            ChargeInterest(company);
        }

        ApplyInflation();
        var maxLoan = MaxLoan;
        for (var c = 0; c < _state.Companies.Count; c++)
        {
            var company = _state.Companies[c];
            company.Value = Money.Add(Money.Add(company.Balance, -company.Loan), PropertyValue(company.Id));
            UpdateBankruptcy(company, maxLoan);
            company.Ledger!.SetPeriodValue(company.Value);
            company.Ledger.StartMonth(date.Year, date.Month);
            company.Ledger.SetPeriodValue(company.Value);
        }
    }

    /// <summary>Year-end work (runs after the month end): the ledgers start the new year.</summary>
    public void AdvanceYear(GameDate date)
    {
        for (var c = 0; c < _state.Companies.Count; c++)
        {
            var company = _state.Companies[c];
            company.Ledger!.StartYear(date.Year);
            company.Ledger.SetPeriodValue(company.Value);
        }
    }

    /// <summary>Turns hashing on (a loan command was applied).</summary>
    internal void MarkActive() => _active = true;

    public void AppendTo(StateHasher hasher)
    {
        ArgumentNullException.ThrowIfNull(hasher);
        if (!IsActive)
        {
            return;
        }

        hasher.Append(Rules.Fingerprint);
        hasher.Append((Settings.Sandbox ? 1 : 0) | (Settings.Inflation ? 2 : 0) | (Settings.DebugServicePays ? 4 : 0) | (Settings.InfiniteMoney ? 8 : 0));
        hasher.Append(CostLevel);
        hasher.Append(PaymentLevel);
        hasher.Append(InflationMonths);
        hasher.Append(TotalPaid);
        hasher.Append(Deliveries);
        hasher.Append(_state.Companies.Count);
        for (var c = 0; c < _state.Companies.Count; c++)
        {
            var company = _state.Companies[c];
            company.AppendFinanceTo(hasher);
        }

        hasher.Append(RunningCosts);
    }

    /// <summary>True once finance state is part of the fingerprint (research R11).</summary>
    public bool IsActive
    {
        get
        {
            if (_active)
            {
                return true;
            }

            for (var c = 0; c < _state.Companies.Count; c++)
            {
                var company = _state.Companies[c];
                if (company.Ledger is { HasEntries: true })
                {
                    return true; // Not latched: an undone action leaves no trace (derived from the ledger).
                }
            }

            return false;
        }
    }

    /// <summary>Yearly maintenance of the company's property at base prices.</summary>
    public long YearlyMaintenance(int companyId)
    {
        long total = 0;
        var roads = Rules.RoadMaintenance;
        for (var type = 0; type < roads.Count; type++)
        {
            total = Money.Add(total, (long)_state.OwnedRoadTiles(companyId, type) * roads[type]);
        }

        if (_stations is not null)
        {
            CountStations(companyId);
            for (var i = 0; i < _pieceCounts.Length; i++)
            {
                total = Money.Add(total, (long)_pieceCounts[i] * Rules.PieceMaintenance[i]);
            }

            for (var i = 0; i < _warehouseCounts.Length; i++)
            {
                total = Money.Add(total, (long)_warehouseCounts[i] * Rules.WarehouseMaintenance[i]);
            }
        }

        return total;
    }

    /// <summary>Value of the company's property (research R6): build cost × share × price level, plus vehicles.</summary>
    public long PropertyValue(int companyId)
    {
        long cost = 0;
        var types = _state.RoadRules.Types;
        for (var type = 0; type < types.Count; type++)
        {
            cost = Money.Add(cost, (long)_state.OwnedRoadTiles(companyId, type) * types[type].BuildCost);
        }

        if (_stations is not null)
        {
            CountStations(companyId);
            for (var i = 0; i < _pieceCounts.Length; i++)
            {
                cost = Money.Add(cost, (long)_pieceCounts[i] * _stations.Rules.Pieces[i].Cost);
            }

            for (var i = 0; i < _warehouseCounts.Length; i++)
            {
                cost = Money.Add(cost, (long)_warehouseCounts[i] * _stations.Rules.Warehouses[i].Cost);
            }
        }

        var property = Inflate(Money.MulDiv(cost, Rules.PropertyValuePercent, 100));
        return Money.Add(property, RunningCosts.ValueTotal(companyId));
    }

    private void CountStations(int companyId)
    {
        Array.Clear(_pieceCounts);
        Array.Clear(_warehouseCounts);
        _stations!.CountProperty(companyId, _pieceCounts, _warehouseCounts);
    }

    private void ChargeMaintenance(Company company)
    {
        var yearly = YearlyMaintenance(company.Id);
        if (yearly == 0 && company.MaintenanceCarry == 0)
        {
            return;
        }

        var accrued = Money.Add(company.MaintenanceCarry, Inflate(yearly));
        company.MaintenanceCarry = accrued % 12;
        var charge = accrued / 12;
        if (charge != 0)
        {
            company.Book(LedgerCategory.PropertyMaintenance, -charge);
        }
    }

    private void ChargeInterest(Company company)
    {
        if (company.Loan == 0 && company.InterestCarry == 0)
        {
            return;
        }

        var accrued = Money.Add(company.InterestCarry, Money.MulDiv(company.Loan, Rules.InterestPerMille, 1));
        company.InterestCarry = accrued % 12_000;
        var charge = accrued / 12_000;
        if (charge != 0)
        {
            company.Book(LedgerCategory.LoanInterest, -charge);
        }
    }

    private void ApplyInflation()
    {
        if (!Settings.Inflation || InflationMonths >= Rules.InflationYears * 12)
        {
            return;
        }

        CostLevel = Money.Add(CostLevel, Money.MulDiv(CostLevel, Rules.CostInflationBasisPoints, 120_000));
        PaymentLevel = Money.Add(PaymentLevel, Money.MulDiv(PaymentLevel, Rules.PaymentInflationBasisPoints, 120_000));
        InflationMonths++;
        _state.CostLevel = CostLevel;
    }

    private void UpdateBankruptcy(Company company, long maxLoan)
    {
        if (company.IsGameOver)
        {
            return;
        }

        if (company.HasInfiniteMoney || company.Balance >= 0 || company.Loan < maxLoan)
        {
            company.BankruptMonths = 0;
            return;
        }

        company.BankruptMonths++;
        _active = true;
        if (company.BankruptMonths >= Rules.GameOverMonths && !Settings.Sandbox)
        {
            company.IsGameOver = true;
            Events.Add(new MoneyEvent(company.Id, company.Balance, -1, -1, MoneyEventKind.GameOver));
        }
        else if (company.BankruptMonths >= Rules.WarningMonths && !Settings.Sandbox)
        {
            Events.Add(new MoneyEvent(company.Id, company.Balance, -1, -1, MoneyEventKind.BankruptcyWarning));
        }
    }
}

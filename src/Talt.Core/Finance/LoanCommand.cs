using System.Text.Json;
using Talt.Core.Commands;
using Talt.Core.Construction;
using Talt.Core.Simulation;

namespace Talt.Core.Finance;

/// <summary>What a <see cref="ChangeLoanCommand"/> does (contracts/commands.md).</summary>
public enum LoanMode : byte
{
    /// <summary>Borrow one step (or what is left below the maximum).</summary>
    Borrow = 0,

    /// <summary>Repay one step (or the remaining loan when smaller).</summary>
    Repay = 1,

    /// <summary>Borrow up to the maximum.</summary>
    BorrowMax = 2,

    /// <summary>Repay as many steps as the cash covers (the whole loan when it can).</summary>
    RepayMax = 3,
}

/// <summary>
/// Borrows or repays (feature 011, contracts/commands.md <c>changeLoan</c>, FR-002, FR-003). Cash and loan change
/// by the same amount; nothing is booked in the ledger (a loan is neither income nor expense).
/// </summary>
public sealed record ChangeLoanCommand(int CompanyId, LoanMode Mode) : ISimulationCommand
{
    public const string Type = "changeLoan";

    public string TypeName => Type;

    public CommandResult Validate(GameSimulation simulation)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        var (reason, _) = Plan(simulation);
        return reason is null ? CommandResult.Success : CommandResult.Rejected(reason, ConstructionReasons.Describe(reason));
    }

    public void Apply(GameSimulation simulation)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        var (_, amount) = Plan(simulation);
        var company = simulation.Construction!.FindCompany(CompanyId)!;
        if (Mode is LoanMode.Borrow or LoanMode.BorrowMax)
        {
            company.Borrow(amount);
        }
        else
        {
            company.Repay(amount);
        }

        simulation.Finance!.MarkActive();
    }

    /// <summary>The refusal reason, or the amount to borrow or repay.</summary>
    public (string? Reason, long Amount) Plan(GameSimulation simulation)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        var finance = simulation.Finance;
        if (finance is null)
        {
            return (ConstructionReasons.NoMap, 0);
        }

        var company = simulation.Construction!.FindCompany(CompanyId);
        if (company is null)
        {
            return (ConstructionReasons.CompanyNotFound, 0);
        }

        if (company.HasInfiniteMoney)
        {
            return (ConstructionReasons.InfiniteMoney, 0); // Research R17: cash does not change, so neither can the loan.
        }

        var step = finance.LoanStep;
        switch (Mode)
        {
            case LoanMode.Borrow:
            case LoanMode.BorrowMax:
            {
                var room = finance.MaxLoan - company.Loan;
                if (room <= 0)
                {
                    return (ConstructionReasons.MaxLoan, 0);
                }

                return (null, Mode == LoanMode.Borrow ? Math.Min(step, room) : room);
            }

            case LoanMode.Repay:
            {
                if (company.Loan == 0)
                {
                    return (ConstructionReasons.NoLoan, 0);
                }

                var amount = Math.Min(step, company.Loan);
                return company.Balance < amount ? (ConstructionReasons.NotEnoughCash, 0) : (null, amount);
            }

            case LoanMode.RepayMax:
            {
                if (company.Loan == 0)
                {
                    return (ConstructionReasons.NoLoan, 0);
                }

                var amount = company.Balance >= company.Loan ? company.Loan : Math.Max(0, company.Balance) / step * step;
                return amount <= 0 ? (ConstructionReasons.NotEnoughCash, 0) : (null, amount);
            }

            default:
                return (ConstructionReasons.NothingToDo, 0);
        }
    }

    public void WriteProperties(Utf8JsonWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteNumber("companyId", CompanyId);
        writer.WriteString("mode", ModeText(Mode));
    }

    public static string ModeText(LoanMode mode) => mode switch
    {
        LoanMode.Borrow => "borrow",
        LoanMode.Repay => "repay",
        LoanMode.BorrowMax => "borrowMax",
        LoanMode.RepayMax => "repayMax",
        _ => throw new ArgumentOutOfRangeException(nameof(mode)),
    };

    internal static ChangeLoanCommand Read(JsonElement e)
    {
        var mode = e.GetProperty("mode").GetString() switch
        {
            "borrow" => LoanMode.Borrow,
            "repay" => LoanMode.Repay,
            "borrowMax" => LoanMode.BorrowMax,
            "repayMax" => LoanMode.RepayMax,
            var other => throw new JsonException($"Unknown loan mode '{other}'."),
        };
        return new ChangeLoanCommand(e.GetProperty("companyId").GetInt32(), mode);
    }
}

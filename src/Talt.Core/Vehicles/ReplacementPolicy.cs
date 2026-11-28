using System.Text.Json;
using Talt.Core.Commands;
using Talt.Core.Construction;
using Talt.Core.Determinism;
using Talt.Core.Simulation;

namespace Talt.Core.Vehicles;

/// <summary>Per-model depot policy; a matching explicit replacement takes precedence over same-model renewal.</summary>
public readonly record struct ReplacementRule(string Target, bool EndOfLifeOnly, bool Renew);

/// <summary>Company rules keyed by permanent model ID, in ordinal order for hashing and persistence.</summary>
public sealed partial class ReplacementPolicy
{
    private readonly SortedList<string, ReplacementRule> _rules = new(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, ReplacementRule> Rules => _rules;

    public bool TryGet(string source, out ReplacementRule rule) => _rules.TryGetValue(source, out rule);

    internal void Set(string source, ReplacementRule rule)
    {
        if (rule.Target.Length == 0 && !rule.Renew)
        {
            _rules.Remove(source);
            return;
        }

        _rules[source] = rule;
    }

    internal void AppendTo(StateHasher hasher, string section)
    {
        if (_rules.Count == 0)
        {
            return;
        }

        hasher.Append(section);
        hasher.Append(_rules.Count);
        foreach (var (source, rule) in _rules)
        {
            hasher.Append(source);
            hasher.Append(rule.Target);
            hasher.Append(rule.EndOfLifeOnly ? 1 : 0);
            hasher.Append(rule.Renew ? 1 : 0);
        }
    }
}

/// <summary>Sets or clears one road or rail model's depot policy.</summary>
public sealed record SetReplacementCommand(int CompanyId, bool Rail, string Source, string Target, bool EndOfLifeOnly, bool Renew) : ISimulationCommand
{
    public const string Type = "setReplacement";
    public string TypeName => Type;

    public CommandResult Validate(GameSimulation simulation)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        if (simulation.Construction?.FindCompany(CompanyId) is null || Source is null || Target is null)
        {
            return VehicleCommand.Rejected(ConstructionReasons.UnknownModel);
        }

        if (Rail && simulation.Trains is { } trains)
        {
            var rules = trains.Rules;
            var source = rules.FindModel(Source);
            var target = rules.FindModel(Target);
            if (source >= 0 && (Target.Length == 0 || (target >= 0 && rules.Models[source].Kind == rules.Models[target].Kind &&
                (rules.Models[source].CargoMask == rules.Models[target].CargoMask || (rules.Models[source].CargoMask & rules.Models[target].CargoMask) != 0) &&
                rules.Models[source].FirstYear < rules.Models[target].FirstYear && rules.Models[target].AvailableIn(simulation.Date.Year))))
            {
                return CommandResult.Success;
            }
        }
        else if (!Rail && simulation.RoadVehicles is { } vehicles)
        {
            var rules = vehicles.Rules;
            var source = rules.FindModel(Source);
            var target = rules.FindModel(Target);
            if (source >= 0 && (Target.Length == 0 || (target >= 0 && rules.Models[source].CargoCategory == rules.Models[target].CargoCategory &&
                rules.Models[source].FirstYear < rules.Models[target].FirstYear && rules.Models[target].AvailableIn(simulation.Date.Year))))
            {
                return CommandResult.Success;
            }
        }

        return VehicleCommand.Rejected(ConstructionReasons.ModelNotAvailable);
    }

    public void Apply(GameSimulation simulation)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        var company = simulation.Construction!.FindCompany(CompanyId)!;
        (Rail ? company.RailReplacements : company.RoadReplacements).Set(Source, new(Target, EndOfLifeOnly, Renew));
    }

    public void WriteProperties(Utf8JsonWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteNumber("companyId", CompanyId);
        writer.WriteBoolean("rail", Rail);
        writer.WriteString("source", Source);
        writer.WriteString("target", Target);
        writer.WriteBoolean("endOfLifeOnly", EndOfLifeOnly);
        writer.WriteBoolean("renew", Renew);
    }

    internal static SetReplacementCommand Read(JsonElement e) => new(e.GetProperty("companyId").GetInt32(),
        e.GetProperty("rail").GetBoolean(), e.GetProperty("source").GetString() ?? "", e.GetProperty("target").GetString() ?? "",
        e.GetProperty("endOfLifeOnly").GetBoolean(), e.GetProperty("renew").GetBoolean());
}

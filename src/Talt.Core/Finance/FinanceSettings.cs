namespace Talt.Core.Finance;

/// <summary>
/// Finance options of a scenario (feature 011, data-model.md FinanceSettings). Part of the scenario
/// settings, so recorded sessions replay them.
/// </summary>
/// <param name="Sandbox">The company never goes bankrupt (clarification Q2); money still counts.</param>
/// <param name="Inflation">Costs and payments rise every month (clarification Q3).</param>
/// <param name="InfiniteMoney">The player's company has infinite money from the start (research R17); it cannot be turned off.</param>
/// <param name="DebugServicePays">
/// Debug (<c>--station-service-pays</c>): cargo collected by the stations' debug service is paid as a delivery
/// over <see cref="FinanceRules.DebugServiceDistance"/> tiles (clarification Q4).
/// </param>
public sealed record FinanceSettings(bool Sandbox = false, bool Inflation = false, bool DebugServicePays = false, bool InfiniteMoney = false)
{
    public static FinanceSettings Default { get; } = new();
}

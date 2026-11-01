namespace Meridian.Contracts.CashFlows;

/// <summary>
/// Retained subject kinds for contractual cash-flow runs. These strings are persisted inside replay
/// packets and their storage paths, so they are stable identifiers rather than an enum.
/// </summary>
public static class ContractualCashFlowSubjectKinds
{
    public const string Security = "Security";
    public const string Loan = "Loan";

    public static bool IsSupported(string? kind) => kind is Security or Loan;
}

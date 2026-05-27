namespace Meridian.Ui.Shared.Services;

public sealed class AccountingExplainabilityOptions
{
    public const string SectionName = "Workstation:AccountingExplainability";

    public decimal MaterialityThreshold { get; init; } = 1.00m;

    public decimal ReconciliationTolerance { get; init; } = 0.01m;
}

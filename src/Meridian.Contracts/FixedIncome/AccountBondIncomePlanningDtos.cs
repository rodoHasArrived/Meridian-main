using Meridian.Contracts.AssetOperations;

namespace Meridian.Contracts.FixedIncome;

public sealed record AccountBondIncomePlanningRequest(AccountCashForecastScopeDto Scope,
    DateOnly PeriodStart, DateOnly PeriodEnd, BondCalculationContext Context,
    IReadOnlyList<AccountSettledCashObservationDto> OpeningBalances,
    IReadOnlyList<AccountCashFxConversionDto> FxConversions,
    decimal MinimumCashThreshold = 0m, int BucketDays = 7);

public sealed record AccountBondIncomePlanDto(AccountCashForecastScopeDto Scope, DateOnly PeriodStart,
    DateOnly PeriodEnd, Guid? HoldingsBatchId, string? HoldingsContentHash,
    IReadOnlyList<PortfolioCashLadderDto> CurrencyLadders, PortfolioCashLadderDto BaseCurrencyLadder,
    IReadOnlyList<BondWorkflowResult> Calculations, BondMonitoringCaptureRequest MonitoringInputs,
    IReadOnlyList<string> Blockers, DateTimeOffset GeneratedAt);

public interface IAccountBondIncomePlanningService
{
    Task<AccountBondIncomePlanDto> PlanAsync(AccountBondIncomePlanningRequest request, CancellationToken ct = default);
}

using Meridian.Application.Services;
using Meridian.Application.UI;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Application.FundStructure;

/// <summary>
/// Summarizes shared governance access to Security Master, historical prices,
/// and backfill state so structure/account/ledger views can surface the same
/// operator-facing availability picture without deriving it from positions.
/// </summary>
public sealed class GovernanceSharedDataAccessService : IGovernanceSharedDataAccessService
{
    private const int SampleSymbolLimit = 5;

    private readonly ISecurityMasterQueryService? _securityMasterQueryService;
    private readonly ISecurityMasterRuntimeStatus? _securityMasterRuntimeStatus;
    private readonly HistoricalDataQueryService? _historicalDataQueryService;
    private readonly BackfillCoordinator? _backfillCoordinator;

    public GovernanceSharedDataAccessService(
        ISecurityMasterQueryService? securityMasterQueryService,
        HistoricalDataQueryService? historicalDataQueryService,
        BackfillCoordinator? backfillCoordinator)
    {
        _securityMasterQueryService = securityMasterQueryService;
        _securityMasterRuntimeStatus = securityMasterQueryService as ISecurityMasterRuntimeStatus;
        _historicalDataQueryService = historicalDataQueryService;
        _backfillCoordinator = backfillCoordinator;
    }

    public Task<FundStructureSharedDataAccessDto> GetSharedDataAccessAsync(
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var sharedDataAccess = new FundStructureSharedDataAccessDto(
            BuildSecurityMasterSummary(),
            BuildHistoricalPriceSummary(),
            BuildBackfillSummary());

        return Task.FromResult(sharedDataAccess);
    }

    private SecurityMasterAccessSummaryDto BuildSecurityMasterSummary()
    {
        if (_securityMasterQueryService is null)
        {
            return new SecurityMasterAccessSummaryDto(
                IsAvailable: false,
                AvailabilityDescription: "Security Master query service is not registered.",
                InstrumentDefinitionsAccessible: false,
                EconomicDefinitionsAccessible: false,
                TradingParametersAccessible: false);
        }

        var isAvailable = _securityMasterRuntimeStatus?.IsAvailable ?? true;
        var description = _securityMasterRuntimeStatus?.AvailabilityDescription
            ?? "Security Master query service is available.";

        return new SecurityMasterAccessSummaryDto(
            IsAvailable: isAvailable,
            AvailabilityDescription: description,
            InstrumentDefinitionsAccessible: isAvailable,
            EconomicDefinitionsAccessible: isAvailable,
            TradingParametersAccessible: isAvailable);
    }

    private HistoricalPriceAccessSummaryDto BuildHistoricalPriceSummary()
    {
        if (_historicalDataQueryService is null)
        {
            return new HistoricalPriceAccessSummaryDto(
                IsAvailable: false,
                HasStoredData: false,
                AvailableSymbolCount: 0,
                SampleSymbols: [],
                AvailabilityDescription: "Historical price query service is not registered.");
        }

        try
        {
            var symbols = _historicalDataQueryService.GetAvailableSymbols();
            var hasStoredData = symbols.Count > 0;

            return new HistoricalPriceAccessSummaryDto(
                IsAvailable: true,
                HasStoredData: hasStoredData,
                AvailableSymbolCount: symbols.Count,
                SampleSymbols: symbols.Take(SampleSymbolLimit).ToArray(),
                AvailabilityDescription: hasStoredData
                    ? $"Historical price data is available for {symbols.Count} symbol(s)."
                    : "Historical price query service is available but no stored symbols were found.");
        }
        catch (Exception ex)
        {
            return new HistoricalPriceAccessSummaryDto(
                IsAvailable: false,
                HasStoredData: false,
                AvailableSymbolCount: 0,
                SampleSymbols: [],
                AvailabilityDescription: $"Historical price data is unavailable: {ex.Message}");
        }
    }

    private BackfillAccessSummaryDto BuildBackfillSummary()
    {
        if (_backfillCoordinator is null)
        {
            return new BackfillAccessSummaryDto(
                IsAvailable: false,
                IsActive: false,
                ProviderCount: 0,
                LastProvider: null,
                LastFrom: null,
                LastTo: null,
                LastCompletedUtc: null,
                LastRunSucceeded: null,
                SymbolCheckpointCount: 0,
                SymbolBarCountCount: 0,
                AvailabilityDescription: "Backfill coordinator is not registered.");
        }

        try
        {
            var providerCount = _backfillCoordinator.DescribeProviders().Count();
            var lastRun = _backfillCoordinator.TryReadLast();
            var symbolCheckpoints = _backfillCoordinator.TryReadSymbolCheckpoints();
            var symbolBarCounts = _backfillCoordinator.TryReadSymbolBarCounts();

            var description = providerCount > 0
                ? $"Backfill services are available with {providerCount} configured provider(s)."
                : "Backfill coordinator is available but no providers are currently configured.";

            return new BackfillAccessSummaryDto(
                IsAvailable: true,
                IsActive: _backfillCoordinator.IsActive,
                ProviderCount: providerCount,
                LastProvider: lastRun?.Provider,
                LastFrom: lastRun?.From,
                LastTo: lastRun?.To,
                LastCompletedUtc: lastRun?.CompletedUtc,
                LastRunSucceeded: lastRun?.Success,
                SymbolCheckpointCount: symbolCheckpoints?.Count ?? 0,
                SymbolBarCountCount: symbolBarCounts?.Count ?? 0,
                AvailabilityDescription: description);
        }
        catch (Exception ex)
        {
            return new BackfillAccessSummaryDto(
                IsAvailable: false,
                IsActive: false,
                ProviderCount: 0,
                LastProvider: null,
                LastFrom: null,
                LastTo: null,
                LastCompletedUtc: null,
                LastRunSucceeded: null,
                SymbolCheckpointCount: 0,
                SymbolBarCountCount: 0,
                AvailabilityDescription: $"Backfill services are unavailable: {ex.Message}");
        }
    }
}

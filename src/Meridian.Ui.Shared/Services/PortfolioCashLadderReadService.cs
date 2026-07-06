using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.SecurityMaster;
using Meridian.Instruments.AssetOperations;

namespace Meridian.Ui.Shared.Services;

/// <summary>
/// Read model for the portfolio-wide cash ladder: enumerates active Security
/// Master subjects, runs each through the asset-operations projection surface,
/// joins opening cash and capital-schedule rows from optional providers, and
/// hands the assembled inputs to <see cref="PortfolioCashLadderEngine"/>.
/// </summary>
public sealed class PortfolioCashLadderReadService : IPortfolioCashLadderQueryService
{
    private const int MaxSecurities = 500;
    private const int ProjectionBatchSize = 16;
    private const string DefaultBaseCurrency = "USD";

    private readonly ISecurityMasterQueryService? _securityMasterQueryService;
    private readonly IAssetOperationsQueryService? _assetOperationsQueryService;
    private readonly IPortfolioCashBalanceProvider? _cashBalanceProvider;
    private readonly IPortfolioCapitalScheduleProvider? _capitalScheduleProvider;

    public PortfolioCashLadderReadService(
        ISecurityMasterQueryService? securityMasterQueryService = null,
        IAssetOperationsQueryService? assetOperationsQueryService = null,
        IPortfolioCashBalanceProvider? cashBalanceProvider = null,
        IPortfolioCapitalScheduleProvider? capitalScheduleProvider = null)
    {
        _securityMasterQueryService = securityMasterQueryService;
        _assetOperationsQueryService = assetOperationsQueryService;
        _cashBalanceProvider = cashBalanceProvider;
        _capitalScheduleProvider = capitalScheduleProvider;
    }

    public async Task<PortfolioCashLadderDto> GetCashLadderAsync(
        PortfolioCashLadderQuery query,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ct.ThrowIfCancellationRequested();

        var asOfDate = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var horizonDays = Math.Max(1, query.HorizonDays);
        var windowEnd = asOfDate.AddDays(horizonDays);

        var positions = await LoadPositionsAsync(ct).ConfigureAwait(false);
        var cashBalances = _cashBalanceProvider is null
            ? []
            : await _cashBalanceProvider.GetCashBalancesAsync(ct).ConfigureAwait(false);
        var capitalActivity = _capitalScheduleProvider is null
            ? (IReadOnlyList<PortfolioCapitalActivityDto>)[]
            : await _capitalScheduleProvider.GetCapitalActivityAsync(asOfDate, windowEnd, ct).ConfigureAwait(false);

        var baseCurrency = cashBalances.Count > 0
            ? cashBalances
                .GroupBy(static balance => balance.Currency, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(static group => group.Count())
                .First().Key
            : DefaultBaseCurrency;

        var inputs = new PortfolioCashLadderInputs(
            asOfDate,
            horizonDays,
            baseCurrency,
            positions,
            cashBalances,
            capitalActivity,
            query.MinimumCashThreshold ?? 0m,
            Math.Max(1, query.BucketDays));

        return PortfolioCashLadderEngine.Build(inputs, query.ScenarioId);
    }

    private async Task<IReadOnlyList<PortfolioCashLadderPositionDto>> LoadPositionsAsync(CancellationToken ct)
    {
        if (_securityMasterQueryService is null || _assetOperationsQueryService is null)
        {
            return [];
        }

        var summaries = await _securityMasterQueryService
            .SearchAsync(new SecuritySearchRequest(string.Empty, Take: MaxSecurities, ActiveOnly: true), ct)
            .ConfigureAwait(false);

        var positions = new List<PortfolioCashLadderPositionDto>(summaries.Count);
        foreach (var batch in summaries.Chunk(ProjectionBatchSize))
        {
            ct.ThrowIfCancellationRequested();
            var details = await Task.WhenAll(batch.Select(summary =>
                _assetOperationsQueryService.GetOperationsAsync(summary.SecurityId, ct))).ConfigureAwait(false);
            positions.AddRange(details
                .Where(static detail => detail is not null)
                .Select(static detail => new PortfolioCashLadderPositionDto(detail!)));
        }

        return positions;
    }
}

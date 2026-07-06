using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.SecurityMaster;
using Meridian.Instruments.AssetOperations;

namespace Meridian.Ui.Shared.Services;

/// <summary>
/// Read model for the portfolio-wide cash ladder: resolves the securities the
/// portfolio actually holds (and their quantities) from an optional holdings
/// source, runs each held instrument through the asset-operations projection
/// surface, joins opening cash and capital-schedule rows from optional providers,
/// and hands the assembled inputs to <see cref="PortfolioCashLadderEngine"/>.
/// </summary>
public sealed class PortfolioCashLadderReadService : IPortfolioCashLadderQueryService
{
    private const int MaxSecurities = 500;
    private const int ProjectionBatchSize = 16;
    private const string DefaultBaseCurrency = "USD";

    private readonly ISecurityMasterQueryService? _securityMasterQueryService;
    private readonly IAssetOperationsQueryService? _assetOperationsQueryService;
    private readonly IPortfolioHoldingsSource? _holdingsSource;
    private readonly IPortfolioCashBalanceProvider? _cashBalanceProvider;
    private readonly IPortfolioCapitalScheduleProvider? _capitalScheduleProvider;

    public PortfolioCashLadderReadService(
        ISecurityMasterQueryService? securityMasterQueryService = null,
        IAssetOperationsQueryService? assetOperationsQueryService = null,
        IPortfolioHoldingsSource? holdingsSource = null,
        IPortfolioCashBalanceProvider? cashBalanceProvider = null,
        IPortfolioCapitalScheduleProvider? capitalScheduleProvider = null)
    {
        _securityMasterQueryService = securityMasterQueryService;
        _assetOperationsQueryService = assetOperationsQueryService;
        _holdingsSource = holdingsSource;
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

        var (positions, positionNotices) = await LoadPositionsAsync(asOfDate, ct).ConfigureAwait(false);
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
            Math.Max(1, query.BucketDays))
        {
            PositionSourceNotices = positionNotices
        };

        return PortfolioCashLadderEngine.Build(inputs, query.ScenarioId);
    }

    private async Task<(IReadOnlyList<PortfolioCashLadderPositionDto> Positions, IReadOnlyList<string> Notices)> LoadPositionsAsync(
        DateOnly asOf,
        CancellationToken ct)
    {
        if (_assetOperationsQueryService is null)
        {
            return ([], []);
        }

        // Prefer actual holdings: forecast flows only for held securities and scale
        // them by the real position quantity. Fall back to enumerating active Security
        // Master subjects at unit quantity only when no holdings source is wired, and
        // make that overstatement visible rather than silent.
        if (_holdingsSource is not null)
        {
            var holdings = await _holdingsSource.GetHoldingsAsync(asOf, ct).ConfigureAwait(false);
            var heldQuantities = holdings
                .Where(static holding => holding.Quantity != 0m)
                .GroupBy(static holding => holding.SecurityId)
                .ToDictionary(static group => group.Key, static group => group.Sum(static holding => holding.Quantity));
            var positions = await ProjectAsync(heldQuantities.Keys, heldQuantities, ct).ConfigureAwait(false);
            return (positions, []);
        }

        if (_securityMasterQueryService is null)
        {
            return ([], []);
        }

        var summaries = await _securityMasterQueryService
            .SearchAsync(new SecuritySearchRequest(string.Empty, Take: MaxSecurities, ActiveOnly: true), ct)
            .ConfigureAwait(false);
        var fallbackPositions = await ProjectAsync(
            summaries.Select(static summary => summary.SecurityId),
            heldQuantities: null,
            ct).ConfigureAwait(false);
        var notices = fallbackPositions.Count == 0
            ? (IReadOnlyList<string>)[]
            : [
                "No holdings source is wired: the ladder forecasts every active Security Master subject at unit quantity, "
                + "not the portfolio's actual holdings, so projected inflows and minimum-balance breaches may be overstated."
            ];
        return (fallbackPositions, notices);
    }

    private async Task<IReadOnlyList<PortfolioCashLadderPositionDto>> ProjectAsync(
        IEnumerable<Guid> securityIds,
        IReadOnlyDictionary<Guid, decimal>? heldQuantities,
        CancellationToken ct)
    {
        var ids = securityIds.Distinct().Take(MaxSecurities).ToArray();
        var positions = new List<PortfolioCashLadderPositionDto>(ids.Length);
        foreach (var batch in ids.Chunk(ProjectionBatchSize))
        {
            ct.ThrowIfCancellationRequested();
            var details = await Task.WhenAll(batch.Select(securityId =>
                _assetOperationsQueryService!.GetOperationsAsync(securityId, ct))).ConfigureAwait(false);
            positions.AddRange(details
                .Where(static detail => detail is not null)
                .Select(detail => new PortfolioCashLadderPositionDto(
                    detail!,
                    heldQuantities is not null && heldQuantities.TryGetValue(detail!.Subject.SecurityId, out var quantity)
                        ? quantity
                        : 1m)));
        }

        return positions;
    }
}

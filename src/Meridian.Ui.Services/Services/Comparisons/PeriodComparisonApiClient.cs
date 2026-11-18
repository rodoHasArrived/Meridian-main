using System.Globalization;
using Meridian.Contracts.Api;
using Meridian.Contracts.Workstation;
using Meridian.Ui.Services;

namespace Meridian.Ui.Services.Services.Comparisons;

/// <summary>Reads the shared prior-period comparisons that the desktop does not compose in process.</summary>
public interface IPeriodComparisonApiClient
{
    /// <summary>The portfolio cash ladder today against the ladder rebuilt as of <paramref name="baselineAsOf"/> (default: the end of last month).</summary>
    Task<ApiResponse<PeriodComparisonDto>> GetCashLadderComparisonAsync(DateOnly? baselineAsOf = null, CancellationToken ct = default);
}

/// <summary>
/// HTTP client for <see cref="UiApiRoutes.PortfolioCashLadderComparison"/>. The cash ladder service
/// needs host-composed holdings and cash providers, so the desktop reads it over the API; a host
/// without them answers 501, which <see cref="PeriodComparisonProjection.DescribeFailure{T}"/> explains.
/// </summary>
public sealed class PeriodComparisonApiClient : IPeriodComparisonApiClient
{
    private readonly ApiClientService _apiClient;

    public PeriodComparisonApiClient(ApiClientService apiClient)
    {
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
    }

    /// <inheritdoc />
    public Task<ApiResponse<PeriodComparisonDto>> GetCashLadderComparisonAsync(DateOnly? baselineAsOf = null, CancellationToken ct = default) =>
        _apiClient.GetWithResponseAsync<PeriodComparisonDto>(BuildCashLadderComparisonRoute(baselineAsOf), ct);

    /// <summary>The comparison route, with the prior date when one is given.</summary>
    public static string BuildCashLadderComparisonRoute(DateOnly? baselineAsOf) =>
        baselineAsOf is { } day
            ? $"{UiApiRoutes.PortfolioCashLadderComparison}?baselineAsOf={day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}"
            : UiApiRoutes.PortfolioCashLadderComparison;
}

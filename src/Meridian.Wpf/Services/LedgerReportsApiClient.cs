using Meridian.Contracts.Api;
using Meridian.Contracts.Ledger;
using Meridian.Ui.Services;

namespace Meridian.Wpf.Services;

/// <summary>
/// Reads the governed ledger — the fund's posted journal — over the shared workstation API.
/// <para>
/// Returns the whole <see cref="ApiResponse{T}"/> rather than an unwrapped value because the
/// status code is load-bearing here: the trial-balance and P&amp;L routes answer 404 when a period
/// has no closed-period summary yet, which is an expected state for an open period and must
/// reach the operator as a notice rather than an outage.
/// </para>
/// <para>
/// HTTP is the only correct seam for this data in the desktop process: <c>ILedgerBookService</c>
/// is registered exclusively by the server-side storage composition, so an in-process call would
/// resolve null here.
/// </para>
/// </summary>
public interface ILedgerReportsApiClient
{
    Task<ApiResponse<List<LedgerBookDto>>> GetBooksAsync(CancellationToken ct = default);

    /// <summary>
    /// Periods for one ledger book. The book scope is not optional in practice: a deployment with
    /// more than one book returns every book's periods from the unscoped route, and nothing in a
    /// period identifies its book to the operator, so an unscoped surface can present one fund's
    /// closed period as another's.
    /// </summary>
    Task<ApiResponse<List<LedgerPeriodDto>>> GetPeriodsAsync(Guid? ledgerBookId, CancellationToken ct = default);

    Task<ApiResponse<List<LedgerPeriodTrialBalanceLineDto>>> GetTrialBalanceAsync(Guid periodId, CancellationToken ct = default);

    Task<ApiResponse<LedgerPeriodPnlSummaryDto>> GetPnlSummaryAsync(Guid periodId, CancellationToken ct = default);
}

public sealed class LedgerReportsApiClient : ILedgerReportsApiClient
{
    private readonly ApiClientService _apiClient;

    public LedgerReportsApiClient(ApiClientService apiClient)
    {
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
    }

    public Task<ApiResponse<List<LedgerBookDto>>> GetBooksAsync(CancellationToken ct = default)
        => _apiClient.GetWithResponseAsync<List<LedgerBookDto>>(UiApiRoutes.LedgerBooks, ct);

    public Task<ApiResponse<List<LedgerPeriodDto>>> GetPeriodsAsync(
        Guid? ledgerBookId,
        CancellationToken ct = default)
        => _apiClient.GetWithResponseAsync<List<LedgerPeriodDto>>(
            ledgerBookId is null
                ? UiApiRoutes.LedgerPeriods
                : $"{UiApiRoutes.LedgerPeriods}?ledgerBookId={Uri.EscapeDataString(ledgerBookId.Value.ToString())}",
            ct);

    public Task<ApiResponse<List<LedgerPeriodTrialBalanceLineDto>>> GetTrialBalanceAsync(
        Guid periodId,
        CancellationToken ct = default)
        => _apiClient.GetWithResponseAsync<List<LedgerPeriodTrialBalanceLineDto>>(
            BuildPeriodRoute(UiApiRoutes.LedgerPeriodTrialBalance, periodId),
            ct);

    public Task<ApiResponse<LedgerPeriodPnlSummaryDto>> GetPnlSummaryAsync(
        Guid periodId,
        CancellationToken ct = default)
        => _apiClient.GetWithResponseAsync<LedgerPeriodPnlSummaryDto>(
            BuildPeriodRoute(UiApiRoutes.LedgerPeriodPnlSummary, periodId),
            ct);

    /// <summary>
    /// Substitutes the route's <c>{periodId:guid}</c> token. The constraint suffix is part of the
    /// declared pattern, so a plain "{periodId}" replacement would silently leave the token in
    /// place and request a literal path.
    /// </summary>
    internal static string BuildPeriodRoute(string routeTemplate, Guid periodId)
        => routeTemplate.Replace("{periodId:guid}", periodId.ToString("D"), StringComparison.Ordinal);
}

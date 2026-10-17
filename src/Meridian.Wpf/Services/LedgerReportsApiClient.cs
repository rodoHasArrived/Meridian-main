using System.Text.Json;
using Meridian.Contracts.Api;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Workstation;
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

    Task<ApiResponse<List<LedgerJournalEntryDto>>> GetJournalEntriesAsync(Guid periodId, CancellationToken ct = default)
        => Task.FromResult(ApiResponse<List<LedgerJournalEntryDto>>.Fail("Posted journal detail is unavailable in this session."));

    Task<ApiResponse<EvidencePacketDto>> GetAmountProofAsync(
        string subjectId, Guid ledgerBookId, Guid periodId, string fundProfileId, CancellationToken ct = default)
        => Task.FromResult(ApiResponse<EvidencePacketDto>.Fail("Amount evidence is unavailable in this session."));

    Task<ApiResponse<JsonDocument>> GetRetainedManifestAsync(string route, CancellationToken ct = default)
        => Task.FromResult(ApiResponse<JsonDocument>.Fail("Retained manifest is unavailable in this session."));
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

    public Task<ApiResponse<List<LedgerJournalEntryDto>>> GetJournalEntriesAsync(Guid periodId, CancellationToken ct = default)
        => _apiClient.GetWithResponseAsync<List<LedgerJournalEntryDto>>(
            BuildPeriodRoute(UiApiRoutes.LedgerPeriodJournalEntries, periodId), ct);

    public Task<ApiResponse<EvidencePacketDto>> GetAmountProofAsync(
        string subjectId, Guid ledgerBookId, Guid periodId, string fundProfileId, CancellationToken ct = default)
        => _apiClient.GetWithResponseAsync<EvidencePacketDto>(
            BuildAmountProofRoute(subjectId, ledgerBookId, periodId, fundProfileId), ct);

    internal static string BuildAmountProofRoute(string subjectId, Guid ledgerBookId, Guid periodId, string fundProfileId)
        => $"/api/workstation/evidence/subjects/ledger-amount/{Uri.EscapeDataString(subjectId)}/packet"
           + $"?ledgerBookId={ledgerBookId:D}&periodId={periodId:D}&fundProfileId={Uri.EscapeDataString(fundProfileId)}";

    public Task<ApiResponse<JsonDocument>> GetRetainedManifestAsync(string route, CancellationToken ct = default)
        => IsRetainedManifestRoute(route)
            ? _apiClient.GetWithResponseAsync<JsonDocument>(route, ct)
            : Task.FromResult(ApiResponse<JsonDocument>.Fail("The retained manifest route is invalid."));

    internal static bool IsRetainedManifestRoute(string? route)
        => TryGetRetainedManifestScope(route, out _);

    internal static bool TryGetRetainedManifestScope(string? route, out IReadOnlyDictionary<string, string> query)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        query = values;
        const string prefix = "/workstation/evidence/vault/";
        var parts = route?.Split('?');
        if (parts is not { Length: 2 } || !parts[0].StartsWith(prefix, StringComparison.Ordinal) ||
            parts[0].Length <= prefix.Length ||
            !parts[0][prefix.Length..].All(character => char.IsAsciiLetterOrDigit(character) || character == '-'))
        {
            return false;
        }
        foreach (var pair in parts[1].Split('&'))
        {
            var separator = pair.IndexOf('=');
            if (separator <= 0 || separator == pair.Length - 1)
            {
                return false;
            }
            var key = pair[..separator];
            if (key is not ("ledgerAmountSubjectId" or "ledgerBookId" or "periodId" or "fundProfileId" or "expectedContentHash") ||
                !values.TryAdd(key, Uri.UnescapeDataString(pair[(separator + 1)..])))
            {
                return false;
            }
        }
        return values.Count == 5 && Guid.TryParseExact(values["ledgerBookId"], "D", out _) &&
               Guid.TryParseExact(values["periodId"], "D", out _) && Sha256Digest.IsWellFormed(values["expectedContentHash"]);
    }

    /// <summary>
    /// Substitutes the route's <c>{periodId:guid}</c> token. The constraint suffix is part of the
    /// declared pattern, so a plain "{periodId}" replacement would silently leave the token in
    /// place and request a literal path.
    /// </summary>
    internal static string BuildPeriodRoute(string routeTemplate, Guid periodId)
        => routeTemplate.Replace("{periodId:guid}", periodId.ToString("D"), StringComparison.Ordinal);
}

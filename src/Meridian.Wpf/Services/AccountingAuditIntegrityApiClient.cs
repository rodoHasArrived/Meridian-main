using Meridian.Contracts.Api;
using Meridian.Contracts.Ledger;

namespace Meridian.Wpf.Services;

/// <summary>How an audit-chain verification request ended, before any chain verdict is read.</summary>
public enum AuditIntegrityCheckOutcome
{
    /// <summary>The server verified the chains and returned a report.</summary>
    Completed,

    /// <summary>The operator lacks <c>AdminMaintenance</c>; nothing was checked.</summary>
    Forbidden,

    /// <summary>Another verification holds the host's single-flight slot, or the rate limit applied.</summary>
    Busy,

    /// <summary>The request failed; nothing is claimed about the chains.</summary>
    Failed,
}

/// <summary>Result of one verification request. <see cref="Report"/> is set only when completed.</summary>
public sealed record AuditIntegrityCheckResult(
    AuditIntegrityCheckOutcome Outcome,
    AccountingAuditIntegrityReportDto? Report = null,
    string? Error = null);

/// <summary>
/// Desktop seam over <c>GET /api/ledger/audit-integrity</c> (W9-GOV-008 verification tooling),
/// kept apart from <see cref="IWorkstationAccountingApiClient"/> so the in-process accounting
/// configuration implementations need not grow a verification method they cannot serve.
/// </summary>
public interface IAccountingAuditIntegrityApiClient
{
    Task<AuditIntegrityCheckResult> VerifyAsync(CancellationToken ct = default);
}

/// <summary>
/// HTTP-backed audit-chain verification for the desktop workstation. Refusals are returned as
/// outcomes rather than thrown, so the shell can tell an operator why nothing was checked
/// instead of reporting a verification failure.
/// </summary>
public sealed class AccountingAuditIntegrityApiClient : IAccountingAuditIntegrityApiClient
{
    private readonly Meridian.Ui.Services.ApiClientService _apiClient;

    public AccountingAuditIntegrityApiClient(Meridian.Ui.Services.ApiClientService apiClient)
    {
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
    }

    public async Task<AuditIntegrityCheckResult> VerifyAsync(CancellationToken ct = default)
    {
        var response = await _apiClient
            .GetWithResponseAsync<AccountingAuditIntegrityReportDto>(UiApiRoutes.LedgerAuditIntegrity, ct)
            .ConfigureAwait(false);

        if (response.Success && response.Data is not null)
        {
            return new(AuditIntegrityCheckOutcome.Completed, response.Data);
        }

        return response.StatusCode switch
        {
            403 => new(AuditIntegrityCheckOutcome.Forbidden),
            429 => new(AuditIntegrityCheckOutcome.Busy),
            _ => new(AuditIntegrityCheckOutcome.Failed, Error: !string.IsNullOrWhiteSpace(response.ErrorMessage)
                ? response.ErrorMessage
                : response.IsConnectionError
                    ? "the workstation service is unreachable"
                    : $"HTTP {response.StatusCode}"),
        };
    }
}

using Meridian.Contracts.Ledger;
using Microsoft.Extensions.Logging;

namespace Meridian.Ui.Shared.Endpoints;

/// <summary>
/// <see cref="IFundProfileTenantGuard"/> backed by accounting-action audit history — the only durable
/// record keyed by the free-form <c>FundProfileId</c> string that also carries tenant attribution
/// (<see cref="AccountingActionAuditEventDto.CompanyId"/> / <see cref="AccountingActionAuditEventDto.TenantId"/>).
///
/// <para>A fund is treated as foreign only when it has tenant-attributed audit history and none of that
/// history is attributed to the caller's company or tenant. Unknown funds (no attributed history) and the
/// caller's own funds are always allowed, so a legitimate edit is never blocked; an unavailable store
/// allows rather than blocks, because the single-company-per-deployment boundary — not this guard — is
/// the control.</para>
/// </summary>
internal sealed class AccountingHistoryFundProfileTenantGuard : IFundProfileTenantGuard
{
    private readonly IAccountingActionAuditStore _auditStore;
    private readonly ILogger<AccountingHistoryFundProfileTenantGuard> _logger;

    public AccountingHistoryFundProfileTenantGuard(
        IAccountingActionAuditStore auditStore,
        ILogger<AccountingHistoryFundProfileTenantGuard> logger)
    {
        _auditStore = auditStore ?? throw new ArgumentNullException(nameof(auditStore));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<FundProfileTenantDecision> EvaluateAsync(
        WorkstationTenantContext tenant,
        string? fundProfileId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(tenant);

        var fund = fundProfileId?.Trim();
        if (string.IsNullOrEmpty(fund))
        {
            return FundProfileTenantDecision.Allow("Unscoped edit; no fund profile to validate.");
        }

        IReadOnlyList<AccountingActionAuditEventDto> history;
        try
        {
            // Null tenant/company => return history for this fund across every company, which is exactly
            // the cross-company signal needed to tell "foreign" from "unknown". Coalesce to empty so a
            // misbehaving store can never throw past the loop and break a legitimate edit.
            history = await _auditStore
                .ListAsync(fund, ledgerBookId: null, ct, tenantId: null, companyId: null)
                .ConfigureAwait(false) ?? [];
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The deployment boundary is the control; a transient authority failure must not block a
            // legitimate edit. Allow and surface a warning rather than denying on uncertainty.
            _logger.LogWarning(
                ex,
                "Fund-profile tenant guard could not read accounting history for {FundProfileId}; allowing (deployment boundary remains the control).",
                fund.Replace('\n', ' ').Replace('\r', ' '));
            return FundProfileTenantDecision.Allow("Fund-profile tenant authority unavailable; deferring to deployment boundary.");
        }

        var callerCompany = Normalize(tenant.CompanyId);
        var callerTenant = Normalize(tenant.TenantId);
        var sawAttributedHistory = false;
        foreach (var entry in history)
        {
            var company = Normalize(entry.CompanyId);
            var entryTenant = Normalize(entry.TenantId);
            if (company is null && entryTenant is null)
            {
                continue; // Unattributed history cannot prove foreign ownership.
            }

            sawAttributedHistory = true;
            if (Matches(company, callerCompany) || Matches(entryTenant, callerTenant))
            {
                return FundProfileTenantDecision.Allow(
                    "Fund profile has accounting history under the caller's tenant/company.");
            }
        }

        return sawAttributedHistory
            ? FundProfileTenantDecision.Deny(
                "Fund profile has accounting history attributed exclusively to other companies.")
            : FundProfileTenantDecision.Allow("Fund profile has no tenant-attributed accounting history.");
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool Matches(string? candidate, string? callerValue)
        => candidate is not null
            && callerValue is not null
            && string.Equals(candidate, callerValue, StringComparison.OrdinalIgnoreCase);
}

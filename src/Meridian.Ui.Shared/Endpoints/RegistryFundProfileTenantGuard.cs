using Meridian.Contracts.Tenancy;
using Microsoft.Extensions.Logging;

namespace Meridian.Ui.Shared.Endpoints;

/// <summary>
/// <see cref="IFundProfileTenantGuard"/> backed by the authoritative <see cref="IFundProfileTenancyRegistry"/>
/// (security backlog SEC-005). On a governed write it claims the fund for the caller's tenant on first use
/// (trust-on-first-use) and denies only when the fund is already owned by a different tenant.
///
/// <para>An unknown fund (claimed for the caller), the caller's own fund, a blank fund scope, a caller
/// with no tenant scope, or an unavailable registry are all allowed, so a legitimate edit is never
/// blocked (no false-deny). Under a single-company-per-deployment runtime every fund binds to the one
/// tenant and nothing is ever foreign; the guard only bites once a shared/multi-tenant deployment
/// co-locates several tenants' funds, with the deployment boundary as the backstop control.</para>
/// </summary>
internal sealed class RegistryFundProfileTenantGuard : IFundProfileTenantGuard
{
    private readonly IFundProfileTenancyRegistry _registry;
    private readonly ILogger<RegistryFundProfileTenantGuard> _logger;

    public RegistryFundProfileTenantGuard(
        IFundProfileTenancyRegistry registry,
        ILogger<RegistryFundProfileTenantGuard> logger)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
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

        if (string.IsNullOrWhiteSpace(tenant.TenantId))
        {
            // No tenant scope to enforce against; the deployment boundary remains the control.
            return FundProfileTenantDecision.Allow("No tenant scope on caller context.");
        }

        try
        {
            // Claim-on-first-use: binds the fund to the caller when unbound, otherwise returns the
            // existing owner. The fund is accessible iff the effective owner is the caller's tenant.
            var owner = await _registry
                .BindAsync(fund, tenant.TenantId, tenant.CompanyId, ct)
                .ConfigureAwait(false);
            return owner.IsHeldBy(tenant.TenantId)
                ? FundProfileTenantDecision.Allow("Fund profile is owned by the caller's tenant.")
                : FundProfileTenantDecision.Deny("Fund profile is owned by a different tenant.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The deployment boundary is the control; a transient registry failure must not block a
            // legitimate edit. Allow and surface a warning rather than denying on uncertainty.
            _logger.LogWarning(
                ex,
                "Fund-profile tenancy registry unavailable for {FundProfileId}; allowing (deployment boundary remains the control).",
                fund.Replace('\n', ' ').Replace('\r', ' '));
            return FundProfileTenantDecision.Allow("Fund-profile tenancy registry unavailable; deferring to deployment boundary.");
        }
    }
}

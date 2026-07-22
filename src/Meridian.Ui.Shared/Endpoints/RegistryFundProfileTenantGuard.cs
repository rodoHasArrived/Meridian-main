using Meridian.Contracts.Tenancy;
using Microsoft.Extensions.Logging;

namespace Meridian.Ui.Shared.Endpoints;

/// <summary>
/// <see cref="IFundProfileTenantGuard"/> backed by the authoritative <see cref="IFundProfileTenancyRegistry"/>
/// (security backlog SEC-005). It performs a <b>read-only</b> ownership check: a governed write is denied
/// only when the fund is already owned by a different tenant.
///
/// <para>An unowned fund, the caller's own fund, a blank fund scope, a caller with no tenant scope, or an
/// unavailable registry are all allowed, so a legitimate edit is never blocked (no false-deny). The guard
/// deliberately does NOT bind the fund here — ownership is established on the first <i>successful</i>
/// authoritative write (a later slice), so a failed or malformed write cannot squat an arbitrary fund id
/// and block the tenant that later performs the first real write. Under a single-company-per-deployment
/// runtime nothing is ever foreign; the guard only bites once a shared/multi-tenant deployment co-locates
/// several tenants' funds, with the deployment boundary as the backstop control.</para>
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
            // Read-only check: the fund is accessible when it is unowned (unknown) or owned by the
            // caller's tenant; a fund owned by a different tenant is denied. Ownership is claimed on the
            // first successful authoritative write, not here, so a failed write cannot squat a fund.
            var accessible = await _registry
                .IsAccessibleAsync(fund, tenant.TenantId, tenant.CompanyId, ct)
                .ConfigureAwait(false);
            return accessible
                ? FundProfileTenantDecision.Allow("Fund profile is unowned or owned by the caller's tenant.")
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

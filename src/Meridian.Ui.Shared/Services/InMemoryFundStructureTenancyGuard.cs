using Meridian.Application.Composition;
using Meridian.Contracts.Services;
using Meridian.Identity;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Meridian.Ui.Shared.Services;

/// <summary>
/// Refuses to start a multi-company deployment on the fund-structure implementation that has no
/// tenant partition (W9-GOV-008 criterion 2).
/// </summary>
/// <remarks>
/// <para><b>The decision this records.</b> When no fund-structure database is configured,
/// <c>IFundStructureService</c> binds to <c>InMemoryFundStructureService</c>, and the desktop lane
/// registers the same JSON-backed implementation independently. That implementation contains no
/// tenant or company identifier at all — not a null column, no filter — so every session it serves
/// shares one graph. The criterion left two ways to resolve that: partition the in-memory store by
/// company, or refuse fund-structure access whenever that posture is configured for more than one
/// company. <b>This is the refusal</b>, chosen because partitioning a store whose whole contract is
/// "one undivided in-process graph" would be a large behavioural change to a lane ADR-019 already
/// bars from production, while the real exposure — an operator switching company ids and seeing one
/// structure — is closed completely by declining to serve it.</para>
///
/// <para><b>Why startup and not per request.</b> A per-call guard on a service with seventeen
/// independent lock sites would be seventeen chances to miss one, and each miss is silent. A startup
/// check states the incompatibility once, before any data is served, in the same shape as ADR-019's
/// <c>ProductionRegistrationGuardService</c> re-validating the composed graph.</para>
///
/// <para>Single-company deployments — the overwhelming majority of this posture — are unaffected, as
/// are deployments that configure no company at all.</para>
/// </remarks>
public sealed class InMemoryFundStructureTenancyGuard : IStartupRefusalGuard
{
    private readonly IFundStructureService _fundStructureService;
    private readonly IUserAccountStore _userAccountStore;
    private readonly ILogger<InMemoryFundStructureTenancyGuard> _logger;

    public InMemoryFundStructureTenancyGuard(
        IFundStructureService fundStructureService,
        IUserAccountStore userAccountStore,
        ILogger<InMemoryFundStructureTenancyGuard> logger)
    {
        _fundStructureService = fundStructureService;
        _userAccountStore = userAccountStore;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Only the unpartitioned posture is in question. A Postgres-backed service carries the
        // tenant column and the scoping this guard exists to substitute for.
        if (_fundStructureService is not INonProductionOnlyService)
        {
            return Task.CompletedTask;
        }

        var companies = _userAccountStore.LoadAccounts()
            .Select(account => account.CompanyId)
            .Where(companyId => !string.IsNullOrWhiteSpace(companyId))
            .Select(companyId => companyId!.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (companies.Count <= 1)
        {
            return Task.CompletedTask;
        }

        _logger.LogError(
            "W9-GOV-008: the configured fund-structure service has no tenant partition but {CompanyCount} companies are configured. Refusing to serve one undivided structure to multiple companies.",
            companies.Count);

        // StartupRefusedException, not a bare InvalidOperationException: hosts tolerate a worker
        // that fails to start, and the WPF shell's catch does exactly that. A refusal has to be
        // distinguishable from a failure or it is swallowed by the same tolerance.
        throw new StartupRefusedException(
            $"The configured fund-structure service ('{_fundStructureService.GetType().Name}') has no tenant "
            + $"partition, but {companies.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)} "
            + "companies are configured for this deployment. It would serve one undivided fund structure to "
            + "all of them. Configure the PostgreSQL fund-structure store, which partitions by tenant, or "
            + "reduce this deployment to a single company.");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

using Meridian.Contracts.Tenancy;
using Meridian.Storage.FundAccounts;
using Meridian.Storage.FundStructure;
using Meridian.Storage.Ledger;
using Meridian.Storage.Tenancy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Meridian.Application.Composition;

/// <summary>Read-only inspection of retained data before strict tenant hosts serve work.</summary>
public interface ITenantCutoverReadinessCheck
{
    Task<TenantCutoverReadiness> InspectAsync(CancellationToken cancellationToken);
}

internal sealed class PostgresTenantCutoverReadinessCheck(IServiceProvider services) : ITenantCutoverReadinessCheck
{
    public Task<TenantCutoverReadiness> InspectAsync(CancellationToken cancellationToken)
        => PostgresTenantCutoverInspector.InspectAsync(
            services.GetService<FundStructureStoreOptions>(),
            services.GetService<LedgerJournalStoreOptions>(),
            services.GetService<FundAccountStoreOptions>(),
            cancellationToken);
}

/// <summary>
/// Refuses an unsafe cutover instead of presenting an apparently empty retained database.
/// This performs bounded database IO and is not a cheap IStartupRefusalGuard. Desktop startup
/// explicitly awaits it after migrations/imports and before exposing its shell.
/// </summary>
public sealed class TenantCutoverGuardService(
    TenantScopeEnforcementOptions options,
    ITenantCutoverReadinessCheck readiness,
    ILogger<TenantCutoverGuardService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!options.IsFailClosed)
        {
            logger.LogWarning("TenantScopeEnforcement=deployment-boundary is temporary migration compatibility. " +
                "Tenant isolation is not enforced; complete the reviewed tenant backfill before enabling strict service.");
            return;
        }

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(60));
        TenantCutoverReadiness result;
        try
        {
            result = await readiness.InspectAsync(deadline.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Database exceptions may contain credentials or retained record values. Expose only
            // the safe type and remediation; never degrade an unverified database to ready.
            throw new StartupRefusedException(
                $"Tenant isolation readiness could not be verified ({exception.GetType().Name}). " +
                "Retained data has not been hidden or deleted. Verify database connectivity and schema migrations, " +
                "then follow docs/operators/fund-structure-tenant-backfill.md before restarting.");
        }

        if (!result.IsReady)
        {
            var findings = string.Join("; ", result.Findings.Select(finding =>
                $"{finding.Store}/{finding.Table}: {finding.Count} ({finding.Reason})"));
            throw new StartupRefusedException(
                "Tenant isolation cutover requires retained-data review: " + findings + ". " +
                "Startup was refused before serving tenant-scoped work; retained data remains intact. " +
                "Run --fund-tenant-backfill --action preview, review attribution and quarantine, and apply the reviewed plan. " +
                "See docs/operators/fund-structure-tenant-backfill.md. Existing single-company installations may explicitly " +
                "set TenantScopeEnforcement=deployment-boundary for a controlled migration window.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

using Meridian.Contracts.Tenancy;
using Meridian.Contracts.Workstation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Meridian.Ui.Shared.Services;

public static class ThreeWayReconciliationServiceRegistration
{
    public static IServiceCollection AddThreeWayReconciliationServices(this IServiceCollection services,
        IConfiguration? configuration, Func<IServiceProvider, string> rootDirectory)
    {
        services.TryAddSingleton(sp =>
        {
            var options = new ThreeWayReconciliationOptions();
            (configuration ?? sp.GetService<IConfiguration>())?.GetSection(ThreeWayReconciliationOptions.SectionKey).Bind(options);
            // Storage roots are host-owned, never accepted from a reconciliation HTTP request.
            options.RootDirectory = rootDirectory(sp);
            if (options.MaximumSourceAge <= TimeSpan.Zero || options.MaximumSourceDrift < TimeSpan.Zero)
                throw new InvalidOperationException("Three-way source age/drift configuration is invalid.");
            if (options.BrokerRefreshInterval is { } refresh && refresh <= TimeSpan.Zero)
                throw new InvalidOperationException("Three-way broker refresh interval must be positive when set.");
            if (options.FillPriceTolerance < 0m || options.FillFeeTolerance < 0m)
                throw new InvalidOperationException("Three-way fill tolerances cannot be negative.");
            return options;
        });
        services.TryAddSingleton<ThreeWayReconciliationMetrics>();
        services.TryAddSingleton<FileThreeWayReconciliationEvidenceStore>();
        services.TryAddSingleton<IThreeWayReconciliationSourceReader, ThreeWayReconciliationSourceReader>();
        services.TryAddSingleton<ThreeWayReconciliationService>();
        services.TryAddSingleton<ThreeWayAdjustmentDraftService>();
        services.TryAddSingleton<ThreeWayReconciliationScheduleRunner>();
        return services;
    }
}

/// <summary>Uses the existing shared scheduler tick; retains one operation per configured scope/day.</summary>
public sealed class ThreeWayReconciliationScheduleRunner(
    IThreeWayReconciliationSourceReader sources,
    ThreeWayReconciliationService reconciliation,
    ThreeWayReconciliationOptions options,
    ILogger<ThreeWayReconciliationScheduleRunner> logger,
    BrokeragePortfolioSyncService? brokerage = null,
    ThreeWayReconciliationMetrics? metrics = null)
{
    // In-memory throttle only: after a restart the first tick refreshes again, which is harmless.
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, DateTimeOffset> _lastBrokerRefresh = new();

    public async Task<int> RunDueAsync(DateTimeOffset nowUtc, CancellationToken ct = default)
    {
        var completed = 0;
        foreach (var schedule in options.Schedules.Where(schedule => schedule.Enabled))
        {
            ct.ThrowIfCancellationRequested();
            // Runs before the attempt, so a schedule that keeps failing still reports as overdue.
            metrics?.ObserveSchedule(schedule.FinancialAccountId);
            try
            {
                using var authority = FundScopeTenantAuthority.Enter(schedule.TenantId, "three-way-reconciliation-schedule");
                var access = new ReconciliationBreakQueueScope(schedule.TenantId, schedule.CompanyId);
                var scope = await sources.AuthorizeAsync(schedule.FinancialAccountId, access, schedule.LedgerBookId,
                    schedule.AccountingPeriodId, ct).ConfigureAwait(false);
                await RefreshBrokerIfDueAsync(schedule.FinancialAccountId, nowUtc, ct).ConfigureAwait(false);
                // Retain independent current observations on every shared minute tick so yesterday's
                // cutoff can select its own operands instead of using tomorrow's newest projection.
                await sources.CaptureAsync(scope, ct).ConfigureAwait(false);
                if (TimeOnly.FromDateTime(nowUtc.UtcDateTime) < options.DailyRunAfterUtc)
                    continue;
                var businessDate = DateOnly.FromDateTime(nowUtc.UtcDateTime).AddDays(-1);
                var cutoff = new DateTimeOffset(businessDate.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc));
                await reconciliation.RunAsync(schedule.FinancialAccountId, access,
                    new(scope.LedgerBookId, scope.AccountingPeriodId, $"daily:{businessDate:yyyy-MM-dd}", cutoff), ct)
                    .ConfigureAwait(false);
                completed++;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                // Account/provider details may occur in storage paths; log type and exact internal
                // scope only, not exception messages containing external account evidence.
                logger.LogWarning("Three-way reconciliation schedule for account {AccountId}, book {BookId} was blocked ({FailureType}).",
                    schedule.FinancialAccountId, schedule.LedgerBookId, exception.GetType().Name);
            }
        }
        return completed;
    }

    /// <summary>
    /// Re-syncs the linked broker account through the same path as the operator endpoint. A failed
    /// refresh is not compared around: the retained broker observation simply stays old, and the
    /// comparison's source-age check then blocks the daily run instead of reading stale evidence.
    /// </summary>
    private async Task RefreshBrokerIfDueAsync(Guid accountId, DateTimeOffset nowUtc, CancellationToken ct)
    {
        if (brokerage is null || options.BrokerRefreshInterval is not { } interval)
            return;
        if (_lastBrokerRefresh.TryGetValue(accountId, out var last) && nowUtc - last < interval)
            return;
        _lastBrokerRefresh[accountId] = nowUtc;
        try
        {
            await brokerage.RunSyncAsync(accountId, new WorkstationBrokerageSyncRunRequestDto(
                RequestedBy: "three-way-reconciliation-schedule"), ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // No account identity in the log: the affected account surfaces through its blocked daily run.
            logger.LogWarning("Scheduled broker refresh failed ({FailureType}); the retained broker observation was not updated.",
                exception.GetType().Name);
        }
    }
}

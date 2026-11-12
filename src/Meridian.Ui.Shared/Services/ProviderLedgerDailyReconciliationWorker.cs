using Meridian.Contracts.Operations;
using Meridian.Contracts.Tenancy;
using Meridian.Contracts.Workstation;
using Meridian.PortfolioRecords.FundAccounts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Meridian.Ui.Shared.Services;

public sealed class ProviderLedgerDailyReconciliationOptions
{
    public int UtcHour { get; set; } = 23;
    public int RetryMinutes { get; set; } = 15;
    public List<ProviderLedgerDailyReconciliationTarget> Targets { get; set; } = [];
}

public sealed record ProviderLedgerDailyReconciliationTarget(
    string TenantId, string CompanyId, string FundProfileId, Guid AccountId,
    decimal AmountTolerance = 0.01m, int ProviderStaleAfterMinutes = 30);

/// <summary>
/// Daily comparison only. The comparison service durably owns the deterministic operation ID,
/// so restart repeats its receipt lookup rather than creating another run or resolving cases.
/// </summary>
public sealed class ProviderLedgerDailyReconciliationWorker : BackgroundService
{
    private readonly ProviderLedgerDailyReconciliationOptions _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<ProviderLedgerDailyReconciliationWorker> _logger;
    private readonly Func<ProviderLedgerDailyReconciliationTarget, string, CancellationToken, Task<OperationTerminalState>> _run;
    private readonly Dictionary<string, (DateTimeOffset NextAttempt, bool Completed)> _attempts = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _gate = new(1, 1);

    public ProviderLedgerDailyReconciliationWorker(IServiceScopeFactory scopes,
        ProviderLedgerDailyReconciliationOptions options, TimeProvider clock,
        ILogger<ProviderLedgerDailyReconciliationWorker> logger)
        : this(options, clock, logger, async (target, operationId, ct) =>
        {
            using var scope = scopes.CreateScope();
            return await RunTargetAsync(scope.ServiceProvider, target, operationId, ct);
        })
    { }

    internal ProviderLedgerDailyReconciliationWorker(ProviderLedgerDailyReconciliationOptions options,
        TimeProvider clock, ILogger<ProviderLedgerDailyReconciliationWorker> logger,
        Func<ProviderLedgerDailyReconciliationTarget, string, CancellationToken, Task<OperationTerminalState>> run)
    {
        if (options.UtcHour is < 0 or > 23 || options.RetryMinutes is < 1 or > 1440 || options.Targets is null)
            throw new ArgumentException("Daily reconciliation requires a UTC hour from 0 through 23 and a retry interval from 1 through 1440 minutes.");
        foreach (var target in options.Targets)
        {
            if (!TenantReadPredicate.IsResolvedTenant(target.TenantId) || !TenantReadPredicate.IsResolvedTenant(target.CompanyId)
                || string.IsNullOrWhiteSpace(target.FundProfileId) || target.AccountId == Guid.Empty || target.AmountTolerance < 0
                || target.ProviderStaleAfterMinutes < 1)
                throw new ArgumentException("Each daily reconciliation target needs explicit retained tenant/company/fund/account scope and valid tolerances.");
        }
        if (options.Targets.GroupBy(t => OperationId(t, new DateOnly(2000, 1, 1))).Any(g => g.Count() != 1))
            throw new ArgumentException("Daily reconciliation targets must be unique per retained account and ownership scope.");
        _options = options;
        _clock = clock;
        _logger = logger;
        _run = run;
    }

    internal static async Task<OperationTerminalState> RunTargetAsync(IServiceProvider services,
        ProviderLedgerDailyReconciliationTarget target, string operationId, CancellationToken ct)
    {
        var owner = await services.GetRequiredService<IFundProfileTenancyRegistry>().ResolveAsync(target.FundProfileId, ct);
        if (owner is null || !owner.IsHeldBy(target.TenantId) || owner.CompanyId != target.CompanyId)
            throw new UnauthorizedAccessException("Daily reconciliation target has no matching retained fund ownership.");
        using var authority = FundScopeTenantAuthority.Enter(owner.TenantId, "daily-provider-ledger-reconciliation:" + operationId);
        var account = await services.GetRequiredService<IFundAccountService>().GetAccountAsync(target.AccountId, ct);
        if (account is null || !Guid.TryParse(target.FundProfileId, out var fundId) || account.FundId != fundId || !account.IsActive)
            throw new UnauthorizedAccessException("Daily reconciliation account does not belong to the configured active fund.");
        var result = await services.GetRequiredService<ProviderLedgerReconciliationService>().RunAsync(target.AccountId,
            new ReconciliationBreakQueueScope(owner.TenantId, owner.CompanyId!),
            new ProviderLedgerReconciliationRequestDto(target.AmountTolerance, target.ProviderStaleAfterMinutes,
                RequestedBy: "daily-provider-ledger-reconciliation", OperationId: operationId), ct);
        return result.Outcome?.State ?? OperationTerminalState.Failed;
    }

    internal async Task RunDueAsync(DateTimeOffset now, CancellationToken ct = default)
    {
        now = now.ToUniversalTime();
        if (now.Hour < _options.UtcHour || _options.Targets.Count == 0)
            return;
        await _gate.WaitAsync(ct);
        try
        {
            var day = DateOnly.FromDateTime(now.UtcDateTime);
            foreach (var priorKey in _attempts.Keys.Where(key => !key.EndsWith(day.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal)).ToArray())
                _attempts.Remove(priorKey);
            foreach (var target in _options.Targets)
            {
                var id = OperationId(target, day);
                if (_attempts.TryGetValue(id, out var prior) && (prior.Completed || prior.NextAttempt > now))
                    continue;
                try
                {
                    var state = await _run(target, id, ct);
                    _attempts[id] = (now.AddMinutes(_options.RetryMinutes),
                        state is OperationTerminalState.Succeeded or OperationTerminalState.CompletedWithWarnings or OperationTerminalState.Blocked);
                    if (state is OperationTerminalState.Failed or OperationTerminalState.Unknown)
                        _logger.LogWarning("Daily provider-ledger comparison {OperationId} did not complete; retry follows the configured interval", id);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch (Exception ex)
                {
                    _attempts[id] = (now.AddMinutes(_options.RetryMinutes), false);
                    _logger.LogError(ex, "Daily provider-ledger comparison {OperationId} failed; retry follows the configured interval", id);
                }
            }
        }
        finally { _gate.Release(); }
    }

    internal static string OperationId(ProviderLedgerDailyReconciliationTarget target, DateOnly date)
        => $"daily-provider-ledger:{Meridian.Contracts.Integrity.Sha256Digest.ComputeUtf8(target.TenantId.Trim().ToLowerInvariant() + "|" + target.CompanyId.Trim().ToLowerInvariant() + "|" + target.FundProfileId.Trim().ToLowerInvariant())}:{target.AccountId:D}:{date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)}";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1), _clock);
        try
        {
            do
            { await RunDueAsync(_clock.GetUtcNow(), stoppingToken); }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}

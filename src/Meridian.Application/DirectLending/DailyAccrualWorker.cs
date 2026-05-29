using Meridian.Contracts.DirectLending;
using Meridian.Contracts.Workstation;
using Meridian.FSharp.Ledger;
using Meridian.Storage.DirectLending;
using Meridian.Storage.Ledger;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Meridian.Application.DirectLending;

/// <summary>
/// Background service that posts daily interest and commitment-fee accruals for all active loans.
/// Runs once per day at the UTC midnight boundary, or immediately on startup if the current day
/// has not yet been processed.
/// </summary>
public sealed class DailyAccrualWorker : BackgroundService
{
    private readonly IDirectLendingOperationsStore _operationsStore;
    private readonly IDirectLendingQueryService _queryService;
    private readonly IDirectLendingCommandService _commandService;
    private readonly ILedgerJournalStore? _ledgerJournalStore;
    private readonly IOperatorInboxService? _operatorInbox;
    private readonly ILogger<DailyAccrualWorker> _logger;

    public DailyAccrualWorker(
        IDirectLendingOperationsStore operationsStore,
        IDirectLendingQueryService queryService,
        IDirectLendingCommandService commandService,
        ILogger<DailyAccrualWorker> logger,
        ILedgerJournalStore? ledgerJournalStore = null,
        IOperatorInboxService? operatorInbox = null)
    {
        _operationsStore = operationsStore;
        _queryService = queryService;
        _commandService = commandService;
        _ledgerJournalStore = ledgerJournalStore;
        _operatorInbox = operatorInbox;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("DailyAccrualWorker started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);

            try
            {
                await RunAccrualBatchAsync(today, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DailyAccrualWorker batch for {Date} encountered an unhandled error.", today);
            }

            await WaitUntilNextRunAsync(stoppingToken).ConfigureAwait(false);
        }

        _logger.LogInformation("DailyAccrualWorker stopped.");
    }

    internal async Task RunAccrualBatchAsync(DateOnly accrualDate, CancellationToken ct)
    {
        var loanIds = await _operationsStore.GetLoanIdsAsync(ct).ConfigureAwait(false);

        if (loanIds.Count == 0)
        {
            _logger.LogDebug("DailyAccrualWorker: no loans found, skipping batch for {Date}.", accrualDate);
            return;
        }

        _logger.LogInformation(
            "DailyAccrualWorker: starting accrual batch for {Date}, {LoanCount} loans to evaluate.",
            accrualDate, loanIds.Count);

        int posted = 0, skipped = 0, failed = 0;

        foreach (var loanId in loanIds)
        {
            if (ct.IsCancellationRequested)
            {
                break;
            }

            try
            {
                var servicing = await _queryService.GetServicingStateAsync(loanId, ct).ConfigureAwait(false);
                if (servicing is null || servicing.Status != LoanStatus.Active)
                {
                    skipped++;
                    continue;
                }

                if (servicing.LastAccrualDate >= accrualDate)
                {
                    skipped++;
                    continue;
                }

                var postingCheck = await CheckPostingDateAsync(loanId, accrualDate, ct).ConfigureAwait(false);
                if (postingCheck is not null && !string.Equals(postingCheck, "Allowed", StringComparison.Ordinal))
                {
                    await RoutePeriodBlockedAsync(loanId, accrualDate, postingCheck, ct).ConfigureAwait(false);
                    failed++;
                    continue;
                }

                var result = await _commandService.PostDailyAccrualAsync(
                    loanId,
                    new PostDailyAccrualRequest(accrualDate),
                    ct: ct).ConfigureAwait(false);

                if (result.IsSuccess)
                {
                    posted++;
                }
                else
                {
                    _logger.LogWarning(
                        "DailyAccrualWorker: accrual rejected for loan {LoanId} on {Date}: {Error}",
                        loanId, accrualDate, result.Error?.Message);
                    failed++;
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "DailyAccrualWorker: unhandled error posting accrual for loan {LoanId} on {Date}.",
                    loanId, accrualDate);
                failed++;
            }
        }

        _logger.LogInformation(
            "DailyAccrualWorker: batch complete for {Date} — posted={Posted}, skipped={Skipped}, failed={Failed}.",
            accrualDate, posted, skipped, failed);
    }

    private async Task<string?> CheckPostingDateAsync(Guid loanId, DateOnly accrualDate, CancellationToken ct)
    {
        if (_ledgerJournalStore is null)
        {
            return null;
        }

        var aggregateEntries = await _ledgerJournalStore.GetByAggregateAsync(loanId, ct).ConfigureAwait(false);
        var latestAggregatePeriodId = aggregateEntries
            .OrderByDescending(static entry => entry.GlobalSequence)
            .Select(static entry => (Guid?)entry.PeriodId)
            .FirstOrDefault();

        if (!latestAggregatePeriodId.HasValue)
        {
            return null;
        }

        var scopedPeriod = await _ledgerJournalStore.GetPeriodAsync(latestAggregatePeriodId.Value, ct).ConfigureAwait(false);
        if (scopedPeriod?.LedgerBookId is not Guid ledgerBookId)
        {
            return null;
        }

        var periods = await _ledgerJournalStore.ListPeriodsAsync(ledgerBookId: ledgerBookId, ct: ct).ConfigureAwait(false);
        var periodDtos = periods.Select(static period => new AccountingPeriodDto(
            period.PeriodId,
            period.FiscalYear,
            period.PeriodNo,
            period.Label,
            period.StartDate,
            period.EndDate,
            period.Status,
            period.OpenedAt,
            period.ClosedAt.HasValue
                ? Microsoft.FSharp.Core.FSharpOption<DateTimeOffset>.Some(period.ClosedAt.Value)
                : null)).ToArray();

        return LedgerInterop.CheckPostingDate(accrualDate, isAdjustment: false, periodDtos);
    }

    private async Task RoutePeriodBlockedAsync(Guid loanId, DateOnly accrualDate, string reason, CancellationToken ct)
    {
        _logger.LogWarning(
            "DailyAccrualWorker: accrual blocked for loan {LoanId} on {Date}: {Reason}",
            loanId,
            accrualDate,
            reason);

        if (_operatorInbox is null)
        {
            return;
        }

        var workItem = new OperatorWorkItemDto(
            WorkItemId: $"direct-lending-period-blocked:{loanId:D}:{accrualDate:yyyyMMdd}",
            Kind: OperatorWorkItemKindDto.LedgerPeriodClose,
            Label: "Direct lending accrual blocked by accounting period",
            Detail: $"Loan {loanId:D} could not post accrual for {accrualDate:yyyy-MM-dd}: {reason}.",
            Tone: OperatorWorkItemToneDto.Warning,
            CreatedAt: DateTimeOffset.UtcNow,
            RunId: loanId.ToString("D"),
            AuditReference: $"period-blocked:{accrualDate:yyyyMMdd}",
            Workspace: "Accounting",
            TargetRoute: "/accounting/reconciliation",
            TargetPageTag: "FundReconciliation",
            Scope: "DirectLendingAccrual");

        await _operatorInbox.UpsertItemAsync(workItem, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Waits until the next UTC midnight, so the worker runs once per calendar day.
    /// </summary>
    private static async Task WaitUntilNextRunAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var nextMidnight = now.Date.AddDays(1);
        var delay = nextMidnight - now;
        await Task.Delay(delay, ct).ConfigureAwait(false);
    }
}

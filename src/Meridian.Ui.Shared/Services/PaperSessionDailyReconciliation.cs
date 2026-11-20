using Meridian.Contracts.Operations;
using Meridian.Contracts.Services;
using Meridian.Execution.Services;
using Meridian.Platform.Scheduling;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Meridian.Ui.Shared.Services;

internal static class PaperSessionDailyReconciliation
{
    public static async Task<OperationTerminalState> RunAsync(
        IServiceProvider services, DateTimeOffset now, ILogger logger, CancellationToken ct)
    {
        var persistence = services.GetService<PaperSessionPersistenceService>();
        var receiptStore = services.GetService<PaperSessionDailyReconciliationStore>();
        if (persistence is null || receiptStore is null)
            return OperationTerminalState.Succeeded;

        var calendar = services.GetService<IOperationalTradingCalendar>() ?? new TradingCalendar();
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        // If a host has configured an early UTC run hour, keep checking until today's regular
        // session closes rather than marking today's operation complete during an open session.
        if (RegularClose(calendar, today) is { } todayClose && todayClose > now)
            return OperationTerminalState.Unknown;

        await persistence.InitialiseAsync(ct).ConfigureAwait(false);
        var failed = false;
        var needsReview = false;
        foreach (var session in persistence.GetSessions())
        {
            ct.ThrowIfCancellationRequested();
            var accountId = persistence.GetSessionAccountId(session.SessionId);
            if (accountId is null)
                continue;

            try
            {
                var latest = await receiptStore.ReadLatestAsync(session.SessionId, accountId, ct).ConfigureAwait(false);
                var through = session.ClosedAt is { } closedAt && closedAt < now ? closedAt : now;
                foreach (var cutoff in Cutoffs(calendar, session.CreatedAt, latest?.Cutoff, through))
                {
                    var evidence = await persistence.GetCutoffEvidenceAsync(session.SessionId, cutoff, ct).ConfigureAwait(false);
                    if (evidence is null)
                        break;
                    await receiptStore.RecordAsync(evidence, ct).ConfigureAwait(false);
                    latest = evidence;
                    if (evidence.CoverageBlocker is not null || evidence.Breaks.Count > 0)
                    {
                        needsReview = true;
                        logger.LogWarning(
                            "Paper reconciliation needs review for session {SessionId} at {Cutoff} ({BreakCount} differences, coverage missing: {CoverageMissing})",
                            session.SessionId, cutoff, evidence.Breaks.Count, evidence.CoverageBlocker is not null);
                    }
                }

                needsReview |= latest is not null && (latest.CoverageBlocker is not null || latest.Breaks.Count > 0);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or UnauthorizedAccessException)
            {
                failed = true;
                logger.LogError("Paper reconciliation could not verify session {SessionId} ({FailureType})",
                    session.SessionId, ex.GetType().Name);
            }
        }

        return failed ? OperationTerminalState.Failed
            : needsReview ? OperationTerminalState.CompletedWithWarnings : OperationTerminalState.Succeeded;
    }

    internal static IEnumerable<DateTimeOffset> Cutoffs(
        IOperationalTradingCalendar calendar, DateTimeOffset openedAt, DateTimeOffset? prior, DateTimeOffset through)
    {
        var earliest = prior ?? openedAt;
        var start = DateOnly.FromDateTime(earliest.UtcDateTime);
        var end = DateOnly.FromDateTime(through.UtcDateTime);
        for (var date = start; date <= end; date = date.AddDays(1))
        {
            var close = RegularClose(calendar, date);
            if (close is { } cutoff && cutoff >= openedAt && cutoff > (prior ?? DateTimeOffset.MinValue) && cutoff <= through)
                yield return cutoff;
            if (date == end)
                break;
        }
    }

    private static DateTimeOffset? RegularClose(IOperationalTradingCalendar calendar, DateOnly date) =>
        calendar.IsTradingDay(date)
            ? calendar.GetTradingSessions(date).FirstOrDefault(session =>
                string.Equals(session.SessionType, "regular", StringComparison.OrdinalIgnoreCase))?.CloseTime.ToUniversalTime()
            : null;
}

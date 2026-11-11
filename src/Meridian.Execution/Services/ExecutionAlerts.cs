using System.Globalization;
using Meridian.Contracts.Integrity;
using Meridian.Execution.Sdk;

namespace Meridian.Execution.Services;

public sealed record ExecutionNotification(string Id, Guid AccountId, string Scope, string Severity,
    string Title, string Detail, DateTimeOffset Timestamp, string? OrderId = null);
public sealed record ExecutionAlertSettings(Guid AccountId, decimal? DailyLossLimitUsd = null,
    int StaleOrderMinutes = 15, bool WebhookEnabled = false, string? WebhookUrl = null, int Version = 1);
public sealed record ExecutionEquityObservation(Guid AccountId, string Scope, DateOnly TradingDate,
    DateTimeOffset ObservedAt, decimal? Equity, decimal? PreviousCloseEquity, decimal? NetExternalFlows,
    bool IsComplete, string? BlockingReason = null, string Currency = "USD");
public sealed record PendingExecutionWebhook(ExecutionNotification Notification, int PreviousAttempts);

public static class ExecutionEquityEvidence
{
    public static ExecutionEquityObservation FromBroker(Guid account, string scope, DateOnly date,
        DateTimeOffset readStarted, DateTimeOffset cutoff, DateTimeOffset previousClose,
        AccountInfo balance, BrokerageActivitySnapshotDto activity)
    {
        var canonical = activity.Activities?.Where(item => item.EffectiveAt > previousClose && item.EffectiveAt < cutoff).ToArray();
        var complete = activity.Cursor?.IsComplete == true && canonical is not null
            && balance.PreviousCloseEquity.HasValue && balance.EquityEvidenceComplete == true
            && balance.Currency == "USD" && cutoff >= readStarted;
        var ambiguous = activity.Activities?.Any(item =>
            (item.Category is BrokerageActivityCategory.Transfer or BrokerageActivityCategory.Journal or BrokerageActivityCategory.Other
                && item.Subtype is not (BrokerageActivitySubtype.CashDeposit or BrokerageActivitySubtype.CashWithdrawal))
            || (item.Subtype is BrokerageActivitySubtype.CashDeposit or BrokerageActivitySubtype.CashWithdrawal
                && (item.Currency != balance.Currency || item.Metadata?.GetValueOrDefault("timestampPrecision") == "day"
                    || item.EffectiveAt == previousClose || item.EffectiveAt >= readStarted))) == true;
        decimal? flows = complete && !ambiguous ? canonical!
            .Where(item => item.Subtype is BrokerageActivitySubtype.CashDeposit or BrokerageActivitySubtype.CashWithdrawal)
            .Sum(item => item.NetAmount) : null;
        return new(account, scope, date, cutoff, balance.EquityEvidenceComplete == true ? balance.Equity : null,
            balance.PreviousCloseEquity, flows, complete && !ambiguous,
            complete && !ambiguous ? null : "Prior-close equity or a complete, unambiguous external cash-flow window is unavailable.", balance.Currency);
    }
}

/// <summary>Durable account-scoped alerts and webhook outbox.</summary>
public sealed class ExecutionAlertService(ExecutionJournal journal, TimeProvider? clock = null)
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly SemaphoreSlim _settingsGate = new(1, 1);

    public async Task<ExecutionAlertSettings> SettingsAsync(Guid accountId, string scope, CancellationToken ct = default) =>
        (await journal.ReadAsync(accountId, ct).ConfigureAwait(false))
        .LastOrDefault(entry => entry.Scope == scope && entry.Settings is not null)?.Settings ?? new(accountId);

    public async Task SaveSettingsAsync(ExecutionAlertSettings settings, string scope, CancellationToken ct = default)
    {
        if (settings.DailyLossLimitUsd is <= 0m || settings.StaleOrderMinutes is < 1 or > 1440)
            throw new ArgumentException("Loss limits must be positive; stale-order minutes must be between 1 and 1440.");
        if (settings.WebhookEnabled && (!Uri.TryCreate(settings.WebhookUrl, UriKind.Absolute, out var url)
            || url.Scheme != Uri.UriSchemeHttps || url.IsLoopback || !string.IsNullOrEmpty(url.UserInfo)))
            throw new ArgumentException("An enabled webhook requires an absolute HTTPS URL without user information.");
        await _settingsGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var previous = await SettingsAsync(settings.AccountId, scope, ct).ConfigureAwait(false);
            var next = settings with { Version = previous.Version + 1 };
            await journal.AppendAsync(new(1, $"settings:{Guid.NewGuid():N}", scope, settings.AccountId, "", "AlertSettings",
                _clock.GetUtcNow(), Settings: next), ct).ConfigureAwait(false);
        }
        finally { _settingsGate.Release(); }
    }

    public async Task<IReadOnlyList<ExecutionNotification>> NotificationsAsync(Guid accountId, string scope, CancellationToken ct = default, int take = 200) =>
        (await journal.ReadAsync(accountId, ct).ConfigureAwait(false))
        .Where(entry => entry.Scope == scope && entry.Notification is not null)
        .Select(entry => entry.Notification!).OrderByDescending(notification => notification.Timestamp).Take(take).ToArray();

    public async Task<IReadOnlyList<PendingExecutionWebhook>> PendingWebhookDeliveriesAsync(Guid accountId, string scope, CancellationToken ct = default)
    {
        var retained = (await journal.ReadAsync(accountId, ct).ConfigureAwait(false)).Where(entry => entry.Scope == scope).ToArray();
        return retained.Where(entry => entry.Notification is not null).Select(entry => entry.Notification!)
            .Where(notification => notification.Title != "External alert delivery failed"
                && !retained.Any(entry => entry.EventId == $"delivered:{notification.Id}"))
            .Select(notification => new PendingExecutionWebhook(notification,
                retained.Count(entry => entry.EventId.StartsWith($"attempt:{notification.Id}:", StringComparison.Ordinal))))
            .Where(delivery => delivery.PreviousAttempts < 4).OrderBy(delivery => delivery.Notification.Timestamp).Take(4).ToArray();
    }

    public async Task ObserveEquityAsync(ExecutionEquityObservation observation, CancellationToken ct = default)
    {
        await journal.AppendAsync(new(1, $"equity:{observation.ObservedAt:O}", observation.Scope, observation.AccountId,
            "", "EquityObservation", _clock.GetUtcNow(), Equity: observation), ct).ConfigureAwait(false);
        var settings = await SettingsAsync(observation.AccountId, observation.Scope, ct).ConfigureAwait(false);
        if (settings.DailyLossLimitUsd is not { } threshold)
            return;
        var complete = observation.IsComplete && observation.Currency == "USD"
            && observation.Equity.HasValue && observation.PreviousCloseEquity.HasValue && observation.NetExternalFlows.HasValue
            && observation.ObservedAt <= _clock.GetUtcNow()
            && _clock.GetUtcNow() - observation.ObservedAt <= TimeSpan.FromMinutes(2);
        if (!complete)
        {
            await PublishAsync(observation.AccountId, observation.Scope, $"equity-gap:{observation.TradingDate}:{settings.Version}",
                "warning", "Daily loss monitoring incomplete", observation.BlockingReason ?? "Opening equity, cash-flow coverage or fresh valuation evidence is unavailable.",
                observation.ObservedAt, ct: ct).ConfigureAwait(false);
            return;
        }
        var pnl = observation.Equity!.Value - observation.PreviousCloseEquity!.Value - observation.NetExternalFlows!.Value;
        if (pnl <= -threshold)
            await PublishAsync(observation.AccountId, observation.Scope, $"daily-loss:{observation.TradingDate}:{settings.Version}",
                "critical", "Daily loss threshold breached",
                string.Create(CultureInfo.InvariantCulture, $"Account equity P&L is {pnl:0.00} USD after external cash-flow adjustment; configured loss limit is {threshold:0.00} USD. Includes realized and unrealized changes."),
                observation.ObservedAt, ct: ct).ConfigureAwait(false);
    }

    public async Task ScanAsync(Guid accountId, string scope, Func<DateTimeOffset, DateTimeOffset, double> regularMinutes,
        CancellationToken ct = default)
    {
        var entries = await journal.ReadAsync(accountId, ct).ConfigureAwait(false);
        var settings = await SettingsAsync(accountId, scope, ct).ConfigureAwait(false);
        foreach (var entry in entries.Where(entry => entry.Scope == scope && entry.Report is not null))
        {
            var report = entry.Report!;
            var fill = report.ReportType is ExecutionReportType.Fill or ExecutionReportType.PartialFill;
            var rejected = report.ReportType is ExecutionReportType.Rejected or ExecutionReportType.CancelRejected or ExecutionReportType.ModificationRejected;
            var pending = report.ReportType is ExecutionReportType.ModificationPending;
            if (!fill && !rejected && !pending)
                continue;
            var key = fill ? $"fill:{entry.OrderId}:{report.FilledQuantity.ToString(CultureInfo.InvariantCulture)}" : $"operation:{entry.EventId}";
            await PublishAsync(accountId, scope, key, fill ? "success" : pending ? "warning" : "critical",
                fill ? "Order execution observed" : pending ? "Replacement requires reconciliation" : "Order operation rejected",
                fill ? $"{report.Symbol}: cumulative execution {report.FilledQuantity.ToString(CultureInfo.InvariantCulture)}. See journal for fill and cost evidence."
                    : report.RejectReason ?? report.ReportType.ToString(), report.Timestamp, entry.OrderId, ct).ConfigureAwait(false);
        }
        foreach (var entry in entries.Where(entry => entry.Scope == scope && entry.Kind == "Rejected" && entry.Report is null))
            await PublishAsync(accountId, scope, $"rejected:{entry.EventId}", "warning", "Order admission rejected",
                entry.Detail ?? "Order admission was refused.", entry.RecordedAt, entry.OrderId, ct).ConfigureAwait(false);
        foreach (var entry in entries.Where(entry => entry.Scope == scope && entry.Kind is "CancelOutcomeUnconfirmed" or "ReplaceOutcomeUnconfirmed"))
            await PublishAsync(accountId, scope, $"unconfirmed:{entry.EventId}", "warning", "Order operation outcome unconfirmed",
                entry.Detail ?? "Inspect retained order and broker evidence before retrying.", entry.RecordedAt, entry.OrderId, ct).ConfigureAwait(false);
        var orders = await journal.OrdersAsync(accountId, take: int.MaxValue, ct: ct, scope: scope).ConfigureAwait(false);
        foreach (var order in orders.Where(order => order.Scope == scope && order.Status is "Accepted" or "PartiallyFilled" or "PendingNew" or "PendingCancel"))
        {
            if (regularMinutes(order.UpdatedAt, _clock.GetUtcNow()) < settings.StaleOrderMinutes)
                continue;
            await PublishAsync(accountId, scope, $"stale:{order.OrderId}:{order.UpdatedAt:O}", "warning", "Working order needs review",
                $"{order.Symbol}: no order progress for {settings.StaleOrderMinutes} eligible market minutes.", order.UpdatedAt, order.OrderId, ct).ConfigureAwait(false);
        }
    }

    public async Task PublishAsync(Guid accountId, string scope, string key, string severity, string title, string detail,
        DateTimeOffset timestamp, string? orderId = null, CancellationToken ct = default)
    {
        var id = Sha256Digest.ComputeUtf8($"{scope.Length}:{scope}:{accountId:D}:{key}");
        var notification = new ExecutionNotification(id, accountId, scope, severity, title, detail, timestamp, orderId);
        await journal.AppendConditionAsync(new(1, $"alert:{id}", scope, accountId, orderId ?? "", "Notification",
            timestamp, Notification: notification), ct).ConfigureAwait(false);
    }
}

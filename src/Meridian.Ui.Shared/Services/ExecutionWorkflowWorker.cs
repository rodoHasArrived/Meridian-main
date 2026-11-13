using System.Net.Http.Json;
using System.Text.Json;
using Meridian.Contracts.Services;
using Meridian.Contracts.Integrity;
using Meridian.Execution;
using Meridian.Execution.Models;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
using Meridian.Platform.Scheduling;
using Meridian.Ui.Shared.Serialization;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Meridian.Ui.Shared.Services;

/// <summary>Reconciliation, loss monitoring, digest scheduling and durable webhook delivery.</summary>
public sealed class ExecutionWorkflowWorker(ExecutionJournal journal, ExecutionAlertService alerts,
    LiveBrokeragePortfolioSyncService sync, IHttpClientFactory clients, ILogger<ExecutionWorkflowWorker> logger, TimeProvider clock,
    IExecutionGateway? gateway = null, IPortfolioState? portfolio = null,
    IOrderManager? orders = null, IPaperSessionStore? sessions = null) : BackgroundService
{
    private readonly TradingCalendar _calendar = new();
    private DateTimeOffset _nextReconcile;
    private long? _lastGeneration;
    private readonly HashSet<string> _legacyImportedScopes = new(StringComparer.Ordinal);
    public string? Scope => gateway is null ? null : gateway is IBrokerageConnectionState connection ? connection.ScopeIdentity : $"local-paper:{gateway.GatewayId}";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5), clock);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            { await TickAsync(stoppingToken).ConfigureAwait(false); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "Execution journal/notification worker needs recovery"); }
            try
            { if (!await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false)) break; }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }

    public async Task TickAsync(CancellationToken ct = default)
    {
        if (Scope is not { } scope)
            return;
        var now = clock.GetUtcNow();
        if (!_legacyImportedScopes.Contains(scope))
        {
            await ImportLegacyAsync(scope, ct).ConfigureAwait(false);
            _legacyImportedScopes.Add(scope);
        }
        var all = await journal.ReadAsync(ct: ct).ConfigureAwait(false);
        var accounts = all.Where(entry => entry.Scope == scope && entry.AccountId.HasValue)
            .Select(entry => entry.AccountId!.Value).Concat(sync.GetStatuses().Select(status => status.FundAccountId)).Distinct().ToArray();
        var generation = (gateway as IBrokerageConnectionState)?.ConnectionGeneration;
        var reconcile = now >= _nextReconcile || generation != _lastGeneration;
        foreach (var account in accounts)
        {
            await alerts.ScanAsync(account, scope, RegularMinutes, ct).ConfigureAwait(false);
            if (reconcile)
                await ReconcileAsync(account, scope, now, ct).ConfigureAwait(false);
            await DigestAsync(account, scope, now, ct).ConfigureAwait(false);
        }
        if (reconcile)
        { _nextReconcile = now.AddSeconds(60); _lastGeneration = generation; }
    }

    private async Task ImportLegacyAsync(string scope, CancellationToken ct)
    {
        if (orders is null)
            return;
        var retained = await journal.ReadAsync(ct: ct).ConfigureAwait(false);
        var states = orders is OrderManagementSystem oms ? oms.GetAllRetainedOrderStates().ToList()
            : orders.GetOpenOrders().Concat(orders.GetCompletedOrders(500)).ToList();
        if (gateway is not IBrokerageGateway && sessions is not null)
            foreach (var session in await sessions.LoadAllSessionsAsync(ct).ConfigureAwait(false))
                states.AddRange(await sessions.LoadOrderHistoryAsync(session.SessionId, ct).ConfigureAwait(false));
        foreach (var state in states.Where(state => state.FundAccountId.HasValue))
        {
            if (retained.Any(entry => entry.Scope == scope && entry.AccountId == state.FundAccountId && entry.OrderId == state.OrderId))
                continue;
            if (gateway is IBrokerageGateway && (state.BrokerAccountId is null
                || gateway is not IBrokerageAccountIdentity identity || !identity.IdentifiesConnectedAccount(state.BrokerAccountId)))
                continue;
            if (Scope != scope)
                throw new InvalidDataException("Execution scope changed during legacy import.");
            var hash = Sha256Digest.ComputeUtf8(JsonSerializer.Serialize(state, ExecutionWorkflowJsonContext.Default.OrderState));
            await journal.AppendAsync(new(1, $"legacy:{hash}", scope, state.FundAccountId, state.OrderId, "LegacyOrder",
                clock.GetUtcNow(), State: state, Detail: "Retained account-bound OMS/session evidence. Original intent, tags and individual fills may be unavailable."), ct).ConfigureAwait(false);
        }
    }

    public async Task DeliveryTickAsync(CancellationToken ct)
    {
        if (Scope is not { } scope)
            return;
        var accounts = (await journal.ReadAsync(ct: ct).ConfigureAwait(false))
            .Where(entry => entry.Scope == scope && entry.Notification is not null).Select(entry => entry.AccountId!.Value).Distinct();
        foreach (var account in accounts)
            await DeliverAsync(account, scope, ct).ConfigureAwait(false);
    }

    private async Task ReconcileAsync(Guid accountId, string scope, DateTimeOffset now, CancellationToken ct)
    {
        var status = sync.GetStatus(accountId);
        var date = DateOnly.FromDateTime(_calendar.GetStatusAt(now).EasternTime.DateTime);
        if (gateway is not IBrokerageGateway broker || gateway is not IBrokerageActivitySync activitySource || status.ExternalAccountId is not { } external)
        {
            // No account-qualified local baseline is inferred from a global paper portfolio.
            var snapshots = portfolio is PaperTradingPortfolio paper ? paper.GetAllAccountSnapshots() : [];
            var account = snapshots.FirstOrDefault(snapshot => snapshot.AccountId == accountId.ToString("D"));
            await alerts.ObserveEquityAsync(new(accountId, scope, date, now, account is null ? null : account.Cash + account.NetExposure - account.MarginBalance,
                null, null, false, "A retained prior-close baseline and complete external-flow window are required for this paper account."), ct).ConfigureAwait(false);
            return;
        }
        var generation = (gateway as IBrokerageConnectionState)?.ConnectionGeneration;
        status = await sync.SynchronizeAsync(accountId, external, ct).ConfigureAwait(false);
        if (!status.IsReady)
        {
            await alerts.PublishAsync(accountId, scope, $"reconciliation-gap:{date}", "warning", "Execution reconciliation needs review",
                "Account evidence or execution-stream reconciliation is incomplete. See Trading readiness.", now, ct: ct).ConfigureAwait(false);
            return;
        }
        var accountReadStarted = clock.GetUtcNow();
        var accountInfo = await broker.GetAccountInfoAsync(ct).ConfigureAwait(false);
        var valuationCutoff = clock.GetUtcNow();
        date = DateOnly.FromDateTime(_calendar.GetStatusAt(valuationCutoff).EasternTime.DateTime);
        var previousDate = _calendar.GetPreviousTradingDay(date);
        var close = Sessions(previousDate).Single(session => session.SessionType == "regular").CloseTime;
        var retained = await journal.ReadAsync(accountId, ct).ConfigureAwait(false);
        var through = retained.LastOrDefault(entry => entry.Scope == scope && entry.ReconciledThrough is not null)?.ReconciledThrough;
        var firstIntent = retained.Where(entry => entry.Scope == scope && entry.Request is not null)
            .Select(entry => entry.RecordedAt).DefaultIfEmpty(close).Min();
        var since = through?.AddMinutes(-5) ?? (firstIntent < close ? firstIntent.AddMinutes(-5) : close);
        var activity = await activitySource.GetActivitySnapshotAsync(external, since, valuationCutoff, ct).ConfigureAwait(false);
        bool SameAccount(string value) => value == external || gateway is IBrokerageAccountIdentity identity && identity.IdentifiesConnectedAccount(value);
        void VerifyScope(BrokerageActivitySnapshotDto snapshot)
        {
            if (generation != (gateway as IBrokerageConnectionState)?.ConnectionGeneration || Scope != scope
                || !SameAccount(snapshot.AccountId) || !SameAccount(accountInfo.AccountId))
                throw new InvalidDataException("Broker account/environment changed during execution reconciliation.");
        }
        VerifyScope(activity);
        async Task RetainFillsAsync(BrokerageActivitySnapshotDto evidence)
        {
            var current = await journal.ReadAsync(accountId, ct).ConfigureAwait(false);
            foreach (var fill in evidence.Fills)
            {
                var eventId = $"activity:{fill.FillId}";
                var existing = current.FirstOrDefault(entry => entry.Scope == scope && entry.EventId == eventId);
                var native = evidence.Orders.FirstOrDefault(order => order.OrderId == fill.OrderId);
                // The journal joins the retained provider order ID to stream reports at read time.
                // A newly discovered client identity cannot rewrite immutable fill evidence.
                var orderId = existing?.OrderId ?? native?.ClientOrderId ?? $"external:{fill.OrderId ?? fill.FillId}";
                await journal.AppendAsync(new(1, eventId, scope, accountId, orderId, "ActivityFill",
                    fill.FilledAt, ActivityFill: fill), ct).ConfigureAwait(false);
            }
        }
        await RetainFillsAsync(activity).ConfigureAwait(false);
        async Task RetainActivitiesAsync(BrokerageActivitySnapshotDto evidence)
        {
            foreach (var item in evidence.Activities ?? [])
            {
                await journal.AppendAsync(new(1, $"canonical:{item.EventId}", scope, accountId, "", "ProviderActivity",
                    item.EffectiveAt, Activity: item), ct).ConfigureAwait(false);
                if (item.Category is BrokerageActivityCategory.CorporateAction or BrokerageActivityCategory.OptionLifecycle
                    || item.Subtype is BrokerageActivitySubtype.TradeCorrection or BrokerageActivitySubtype.TradeBust
                        or BrokerageActivitySubtype.SecurityTransfer or BrokerageActivitySubtype.SecurityJournal or BrokerageActivitySubtype.AcatsSecurity)
                    await journal.AppendAsync(new(1, $"economic-gap:{item.EventId}", scope, accountId, "", "EconomicCoverageGap",
                        item.EffectiveAt, Detail: "Corporate action, position transfer or trade correction requires an authoritative basis rebuild."), ct).ConfigureAwait(false);
            }
        }
        await RetainActivitiesAsync(activity).ConfigureAwait(false);
        if (activity.Cursor is { IsComplete: true } progress)
            await journal.AppendAsync(new(1, $"cursor:{now:O}", scope, accountId, "", "ActivityCursor", now,
                ActivityCursor: progress, ReconciledThrough: valuationCutoff), ct).ConfigureAwait(false);
        // Include the preceding calendar day to detect date-only transfers that cannot
        // be assigned safely to either side of the prior-close baseline.
        var dailyActivity = since > close.AddDays(-1) ? await activitySource.GetActivitySnapshotAsync(external, close.AddDays(-1), valuationCutoff, ct).ConfigureAwait(false) : activity;
        VerifyScope(dailyActivity);
        await RetainFillsAsync(dailyActivity).ConfigureAwait(false);
        await RetainActivitiesAsync(dailyActivity).ConfigureAwait(false);
        await alerts.ObserveEquityAsync(ExecutionEquityEvidence.FromBroker(accountId, scope, date,
            accountReadStarted, valuationCutoff, close, accountInfo, dailyActivity), ct).ConfigureAwait(false);
    }

    private async Task DigestAsync(Guid account, string scope, DateTimeOffset now, CancellationToken ct)
    {
        var date = DateOnly.FromDateTime(_calendar.GetStatusAt(now).EasternTime.DateTime);
        var session = Sessions(date).FirstOrDefault(session => session.SessionType == "regular");
        if (session is null || now < session.CloseTime.AddMinutes(15))
        { date = _calendar.GetPreviousTradingDay(date); session = Sessions(date).Single(session => session.SessionType == "regular"); }
        var close = session.CloseTime;
        var start = Sessions(_calendar.GetPreviousTradingDay(date)).Single(session => session.SessionType == "regular").CloseTime;
        var retained = await journal.ReadAsync(account, ct).ConfigureAwait(false);
        if (!retained.Any(entry => entry.Scope == scope && entry.RecordedAt < close))
            return;
        var summary = await journal.SummaryAsync(account, ct, scope, start, close.AddMinutes(15)).ConfigureAwait(false);
        await alerts.PublishAsync(account, scope, $"digest:{date}", "info", "Trading day digest",
            $"{summary.Orders} retained orders; {summary.Fills} fills for this close window. Evidence coverage: {summary.Coverage}. Inspect the journal for dated activity and unresolved costs.", close.AddMinutes(15), ct: ct).ConfigureAwait(false);
    }

    private async Task DeliverAsync(Guid account, string scope, CancellationToken ct)
    {
        var settings = await alerts.SettingsAsync(account, scope, ct).ConfigureAwait(false);
        if (!settings.WebhookEnabled || settings.WebhookUrl is null)
            return;
        foreach (var delivery in await alerts.PendingWebhookDeliveriesAsync(account, scope, ct).ConfigureAwait(false))
        {
            var notification = delivery.Notification;
            var attempts = delivery.PreviousAttempts;
            await journal.AppendAsync(new(1, $"attempt:{notification.Id}:{attempts + 1}", scope, account, "", "WebhookAttempt", clock.GetUtcNow()), ct).ConfigureAwait(false);
            using var client = clients.CreateClient("execution-webhook");
            try
            {
                using var response = await client.PostAsJsonAsync(settings.WebhookUrl, notification,
                    ExecutionWorkflowJsonContext.Default.ExecutionNotification, ct).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                await journal.AppendAsync(new(1, $"delivered:{notification.Id}", scope, account, "", "WebhookDelivered", clock.GetUtcNow()), ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
            {
                if (attempts == 3)
                    await alerts.PublishAsync(account, scope, $"delivery-failed:{notification.Id}", "warning", "External alert delivery failed",
                        "The webhook delivery retry limit was reached. The retained notification remains available in Meridian.", clock.GetUtcNow(), ct: ct).ConfigureAwait(false);
            }
        }
    }

    private IReadOnlyList<TradingSession> Sessions(DateOnly date) => ((IOperationalTradingCalendar)_calendar).GetTradingSessions(date);
    private double RegularMinutes(DateTimeOffset from, DateTimeOffset to)
    {
        var first = DateOnly.FromDateTime(_calendar.GetStatusAt(from).EasternTime.DateTime);
        var last = DateOnly.FromDateTime(_calendar.GetStatusAt(to).EasternTime.DateTime);
        var minutes = 0d;
        for (var date = first; date <= last; date = date.AddDays(1))
            foreach (var session in Sessions(date).Where(session => session.SessionType == "regular"))
            {
                var start = from > session.OpenTime ? from : session.OpenTime;
                var end = to < session.CloseTime ? to : session.CloseTime;
                if (end > start)
                    minutes += (end - start).TotalMinutes;
            }
        return minutes;
    }
}

/// <summary>Slow external receivers cannot delay loss monitoring or broker reconciliation.</summary>
public sealed class ExecutionWebhookWorker(ExecutionWorkflowWorker workflow, ILogger<ExecutionWebhookWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            { await workflow.DeliveryTickAsync(stoppingToken).ConfigureAwait(false); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "Execution notification outbox delivery needs recovery"); }
        }
    }
}

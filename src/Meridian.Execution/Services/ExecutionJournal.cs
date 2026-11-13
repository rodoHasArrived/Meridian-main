using System.Text.Json;
using Meridian.Contracts.Integrity;
using Meridian.Execution.Sdk;
using Meridian.Execution.Serialization;
using Meridian.Storage.Archival;
using Meridian.Ledger;

namespace Meridian.Execution.Services;

/// <summary>Immutable execution evidence; operational reporting never posts accounting journals.</summary>
public sealed record ExecutionJournalEntry(
    int Version, string EventId, string Scope, Guid? AccountId, string OrderId, string Kind,
    DateTimeOffset RecordedAt, OrderRequest? Request = null, ExecutionReport? Report = null,
    OrderState? State = null, OrderMarketReference? Reference = null, string? Detail = null,
    ExecutionNotification? Notification = null, ExecutionAlertSettings? Settings = null,
    ExecutionEquityObservation? Equity = null, BrokerageFillSnapshotDto? ActivityFill = null,
    BrokerageActivityCursorDto? ActivityCursor = null, JournalOpeningEvidence? Opening = null,
    string? ReportingAssetClass = null, DateTimeOffset? ReconciledThrough = null,
    BrokerageActivityEventDto? Activity = null, OrderModification? Modification = null, Guid? AttemptId = null,
    Guid? PaperBookId = null);

public sealed record JournalOpeningEvidence(bool IsComplete, bool IsFlat, DateTimeOffset ObservedAt, string Source);
public sealed record JournalAttribution(string? StrategyId, string? RunId, string? Tag, decimal GrossRealizedPnl, decimal? NetRealizedPnl);

public sealed record JournalOrder(
    string OrderId, string Scope, Guid AccountId, string Symbol, string Side, string Status,
    decimal Quantity, decimal FilledQuantity, decimal? AverageFillPrice, string? StrategyId,
    string? RunId, string? Tag, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt,
    IReadOnlyList<ExecutionJournalEntry> Events, string? AttemptId = null);

public sealed record JournalSummary(int Orders, int Fills, decimal? RealizedPnl, decimal? Commissions,
    decimal? Fees, decimal? Slippage, string Coverage, string Method = "FIFO operational attribution",
    decimal? NetRealizedPnl = null, IReadOnlyList<JournalAttribution>? Attributions = null);

/// <summary>Single-writer, crash-replayable WAL. Identity/content conflicts fail closed.</summary>
public sealed class ExecutionJournal : IAsyncDisposable
{
    private readonly WriteAheadLog _wal;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly List<ExecutionJournalEntry> _entries = [];
    private readonly Dictionary<string, string> _hashes = new(StringComparer.Ordinal);
    private bool _initialized;
    public Func<Guid, string, JournalOpeningEvidence?>? OpeningEvidenceSource { private get; init; }
    public Func<Guid?>? PaperBookIdentitySource { private get; init; }

    public ExecutionJournal(string root) => _wal = new WriteAheadLog(root, new WalOptions
    {
        SyncMode = WalSyncMode.EveryWrite,
        CorruptionMode = WalCorruptionMode.Halt,
        ArchiveAfterTruncate = false,
        MaxWalFileSizeBytes = 16 * 1024 * 1024
    });

    public Task AppendAsync(ExecutionJournalEntry entry, CancellationToken ct = default) => AppendCoreAsync(entry, false, ct);

    // Conditions (alerts) retain the first observation atomically. Economic events always
    // use AppendAsync, where changed content under an existing identity is an error.
    public Task AppendConditionAsync(ExecutionJournalEntry entry, CancellationToken ct = default) => AppendCoreAsync(entry, true, ct);

    private async Task AppendCoreAsync(ExecutionJournalEntry entry, bool firstObservationWins, CancellationToken ct)
    {
        if (entry.Version != 1 || string.IsNullOrWhiteSpace(entry.Scope) || string.IsNullOrWhiteSpace(entry.EventId))
            throw new InvalidDataException("Execution journal envelope is invalid.");
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await InitializeLockedAsync(ct).ConfigureAwait(false);
            if (entry.Kind is "Dispatch" or "OpeningCheckpoint" && entry.AccountId is { } bookAccount
                && entry.Scope.StartsWith("local-paper:", StringComparison.Ordinal)
                && PaperBookIdentitySource?.Invoke() is { } bookId)
            {
                var history = _entries.Where(item => item.Scope == entry.Scope && item.AccountId == bookAccount).ToArray();
                if (history.LastOrDefault(item => item.Kind == "PaperBook")?.PaperBookId != bookId)
                {
                    // A fresh simulator does not restore lots from the retained execution log.
                    // Keep prior evidence visible but never match those lots against the new book.
                    if (!history.Any(item => item.Kind == "EconomicCoverageGap" && item.PaperBookId == bookId)
                        && history.Any(item => item.Report?.FilledQuantity > 0m || item.State?.FilledQuantity > 0m
                            || item.ActivityFill?.Quantity > 0m))
                        await AppendRetainedLockedAsync(new(1, $"paper-book-gap:{bookAccount:D}:{bookId:D}", entry.Scope,
                            bookAccount, "", "EconomicCoverageGap", entry.RecordedAt,
                            Detail: "Local paper book restarted without restoring prior positions; FIFO continuity is unavailable.",
                            PaperBookId: bookId), ct).ConfigureAwait(false);
                    await AppendRetainedLockedAsync(new(1, $"paper-book:{bookAccount:D}:{bookId:D}", entry.Scope,
                        bookAccount, "", "PaperBook", entry.RecordedAt, PaperBookId: bookId), ct).ConfigureAwait(false);
                }
            }
            if (entry.Kind is "Dispatch" or "OpeningCheckpoint" && entry.AccountId is { } account
                && !_entries.Any(item => item.Scope == entry.Scope && item.AccountId == account && item.Opening is not null))
            {
                var opening = OpeningEvidenceSource?.Invoke(account, entry.Scope)
                    ?? new JournalOpeningEvidence(false, false, entry.RecordedAt, "Opening position evidence unavailable before first dispatch.");
                var seed = new ExecutionJournalEntry(1, $"opening:{account:D}", entry.Scope, account, "", "OpeningEvidence", entry.RecordedAt, Opening: opening);
                await AppendRetainedLockedAsync(seed, ct).ConfigureAwait(false);
            }
            var key = Key(entry);
            var hash = ContentHash(entry);
            if (_hashes.TryGetValue(key, out var retained))
            {
                if (retained != hash && !firstObservationWins)
                    throw new InvalidDataException("Execution event identity was reused with conflicting content.");
                return;
            }
            await AppendRetainedLockedAsync(entry, ct).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    private async Task AppendRetainedLockedAsync(ExecutionJournalEntry entry, CancellationToken ct)
    {
        await _wal.AppendAsync(Serialize(entry), "ExecutionJournalV1", ct).ConfigureAwait(false);
        _entries.Add(entry);
        _hashes.Add(Key(entry), ContentHash(entry));
    }

    public async Task<IReadOnlyList<ExecutionJournalEntry>> ReadAsync(Guid? accountId = null, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await InitializeLockedAsync(ct).ConfigureAwait(false);
            return _entries.Where(entry => accountId is null || entry.AccountId == accountId).ToArray();
        }
        finally { _gate.Release(); }
    }

    public async Task<IReadOnlyList<JournalOrder>> OrdersAsync(Guid accountId, string? symbol = null,
        string? strategy = null, string? tag = null, DateTimeOffset? from = null, DateTimeOffset? to = null,
        int take = 100, CancellationToken ct = default, string? scope = null, string? orderId = null, string? status = null,
        string? attemptId = null)
    {
        var entries = await ReadAsync(accountId, ct).ConfigureAwait(false);
        var matches = GroupAttempts(entries, scope)
            .Where(attempt => (orderId is null || attempt.OrderId == orderId) && (attemptId is null || attempt.Id == attemptId))
            .Select(attempt => (Attempt: attempt, Order: ProjectOrder(accountId, attempt)))
            .Where(item => Matches(item.Order));
        var sorted = orderId is null
            ? matches.OrderByDescending(item => item.Order.UpdatedAt).ThenBy(item => item.Order.OrderId, StringComparer.Ordinal)
            : matches.OrderBy(item => item.Attempt.IsDuplicateSubmission).ThenByDescending(item => item.Attempt.FirstIndex);
        return sorted.Take(Math.Max(take, 1)).Select(item => item.Order).ToArray();

        bool Matches(JournalOrder order) => (string.IsNullOrWhiteSpace(status) || order.Status.Equals(status, StringComparison.OrdinalIgnoreCase))
            && (string.IsNullOrWhiteSpace(symbol) || order.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase))
            && (string.IsNullOrWhiteSpace(strategy) || order.StrategyId == strategy || order.RunId == strategy)
            && (string.IsNullOrWhiteSpace(tag) || order.Tag == tag)
            && (from is null || order.UpdatedAt >= from) && (to is null || order.CreatedAt <= to);
    }

    private static IReadOnlyList<OrderAttempt> GroupAttempts(IReadOnlyList<ExecutionJournalEntry> entries, string? scope)
    {
        var attempts = new List<OrderAttempt>();
        var byRoot = new Dictionary<(string Scope, string OrderId), List<OrderAttempt>>();
        var byAttempt = new Dictionary<(string Scope, string OrderId, Guid AttemptId), OrderAttempt>();
        var byBroker = new Dictionary<(string Scope, string BrokerId), OrderAttempt?>();
        var activities = new List<(int Index, ExecutionJournalEntry Entry)>();

        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            if ((scope is not null && entry.Scope != scope) || string.IsNullOrWhiteSpace(entry.OrderId)
                || entry.Kind is "Notification" or "WebhookDelivered" or "WebhookAttempt")
                continue;
            if (entry.ActivityFill is not null)
            {
                activities.Add((index, entry));
                continue;
            }

            var root = (entry.Scope, entry.OrderId);
            if (!byRoot.TryGetValue(root, out var rootAttempts))
                byRoot[root] = rootAttempts = [];
            OrderAttempt attempt;
            if (entry.AttemptId is { } attemptId)
            {
                var key = (entry.Scope, entry.OrderId, attemptId);
                if (!byAttempt.TryGetValue(key, out var existing))
                    byAttempt[key] = existing = AddAttempt(entry, attemptId.ToString("N"), index);
                attempt = existing;
            }
            else
            {
                // Replay older journals by their retained intent and dispatch boundaries.
                var existing = rootAttempts.LastOrDefault(item => item.Id.StartsWith("legacy:", StringComparison.Ordinal));
                attempt = existing is null || entry.Kind == "Intent"
                    || entry.Kind == "OpeningCheckpoint" && existing.HasPreflightRefusal
                    || entry.Kind == "Dispatch" && (existing.HasDispatch || existing.HasPreflightRefusal)
                    ? AddAttempt(entry, $"legacy:{rootAttempts.Count}", index) : existing;
            }
            attempt.Events.Add((index, entry));
            if (BrokerOrderId(entry) is { } brokerId)
            {
                var key = (entry.Scope, brokerId);
                if (!byBroker.TryAdd(key, attempt) && byBroker[key] != attempt)
                    byBroker[key] = null;
            }
        }

        var external = new Dictionary<(string Scope, string OrderId, string ProviderId), OrderAttempt>();
        foreach (var (index, entry) in activities)
        {
            var fill = entry.ActivityFill!;
            OrderAttempt? attempt = null;
            if (fill.OrderId is { Length: > 0 } brokerId && !byBroker.TryGetValue((entry.Scope, brokerId), out attempt))
            {
                // A snapshot can establish the client ID before an execution report exists.
                // Link by client ID only when there is a single possible routed incarnation.
                if (entry.OrderId != $"external:{fill.OrderId ?? fill.FillId}"
                    && byRoot.TryGetValue((entry.Scope, entry.OrderId), out var rootAttempts))
                {
                    var candidates = rootAttempts.Where(item => item.HasDispatchOrLegacyState && !item.HasBrokerIdentity
                        && item.Matches(fill)).Take(2).ToArray();
                    if (candidates.Length == 1)
                        attempt = candidates[0];
                }
            }
            if (attempt is null || !attempt.Matches(fill))
            {
                var providerId = fill.OrderId ?? fill.FillId;
                var key = (entry.Scope, entry.OrderId, providerId);
                if (!external.TryGetValue(key, out attempt))
                {
                    external[key] = attempt = new(entry.Scope, entry.OrderId, $"activity:{providerId}", index);
                    attempts.Add(attempt);
                }
            }
            attempt.Events.Add((index, entry));
        }
        return attempts;

        OrderAttempt AddAttempt(ExecutionJournalEntry entry, string id, int index)
        {
            var attempt = new OrderAttempt(entry.Scope, entry.OrderId, id, index);
            attempts.Add(attempt);
            byRoot[(entry.Scope, entry.OrderId)].Add(attempt);
            return attempt;
        }
    }

    private static JournalOrder ProjectOrder(Guid accountId, OrderAttempt attempt)
    {
        var events = attempt.Events.OrderBy(item => item.Index).Select(item => item.Entry).ToArray();
        var request = events.FirstOrDefault(entry => entry.Request is not null)?.Request;
        var state = events.Where(entry => entry.State is not null).Select(entry => entry.State!)
            .OrderBy(state => state.LastUpdatedAt ?? state.CreatedAt).ThenBy(state => state.FilledQuantity)
            .ThenBy(state => state.Status is OrderStatus.Filled or OrderStatus.Cancelled or OrderStatus.Rejected or OrderStatus.Expired).LastOrDefault();
        var reports = events.Where(entry => entry.Report is not null).Select(entry => entry.Report!).ToArray();
        var filled = reports.Select(report => report.FilledQuantity).DefaultIfEmpty(0m).Max();
        var last = reports.LastOrDefault();
        var activity = events.LastOrDefault(entry => entry.ActivityFill is not null)?.ActivityFill;
        var activityQuantity = events.Where(entry => entry.ActivityFill is not null).DistinctBy(entry => entry.ActivityFill!.FillId)
            .Sum(entry => entry.ActivityFill!.Quantity);
        return new JournalOrder(attempt.OrderId, attempt.Scope, accountId,
            request?.Symbol ?? state?.Symbol ?? last?.Symbol ?? activity?.Symbol ?? "unknown",
            (request?.Side ?? state?.Side ?? last?.Side ?? activity?.Side)?.ToString() ?? "unknown",
            state?.Status.ToString() ?? last?.OrderStatus.ToString() ?? (attempt.IsDuplicateSubmission ? "Rejected" : events[^1].Kind),
            state?.Quantity ?? request?.Quantity ?? last?.OrderQuantity ?? activityQuantity,
            Math.Max(activityQuantity, Math.Max(filled, state?.FilledQuantity ?? 0m)), state?.AverageFillPrice,
            request?.StrategyId ?? state?.StrategyId, state?.RunId ?? Metadata(request, "runId"),
            Metadata(request, "tag"), events.Min(entry => entry.RecordedAt), events.Max(entry => entry.RecordedAt), events, attempt.Id);
    }

    private static string? BrokerOrderId(ExecutionJournalEntry entry) => entry.Report switch
    {
        { GatewayOrderId: { Length: > 0 } brokerId } => brokerId,
        { OrderId: { Length: > 0 } brokerId } report when brokerId != entry.OrderId && brokerId != report.ClientOrderId => brokerId,
        _ => null
    };

    private sealed class OrderAttempt(string scope, string orderId, string id, int firstIndex)
    {
        public string Scope { get; } = scope;
        public string OrderId { get; } = orderId;
        public string Id { get; } = id;
        public int FirstIndex { get; } = firstIndex;
        public List<(int Index, ExecutionJournalEntry Entry)> Events { get; } = [];
        public bool HasDispatch => Events.Any(item => item.Entry.Kind == "Dispatch");
        public bool HasPreflightRefusal => !HasDispatch && Events.Any(item => item.Entry.Kind == "Rejected" && item.Entry.Request is not null);
        public bool HasDispatchOrLegacyState => Events.Any(item => item.Entry.Kind is "Dispatch" or "LegacyOrder");
        public bool HasBrokerIdentity => Events.Any(item => BrokerOrderId(item.Entry) is not null);
        public bool IsDuplicateSubmission => Events.Any(item => item.Entry.Kind == "DuplicateSubmission");

        public bool Matches(BrokerageFillSnapshotDto fill)
        {
            var request = Events.FirstOrDefault(item => item.Entry.Kind == "Dispatch").Entry?.Request;
            var state = Events.FirstOrDefault(item => item.Entry.State is not null).Entry?.State;
            var report = Events.FirstOrDefault(item => item.Entry.Report is not null).Entry?.Report;
            return string.Equals(request?.Symbol ?? state?.Symbol ?? report?.Symbol, fill.Symbol, StringComparison.OrdinalIgnoreCase)
                && (request?.Side ?? state?.Side ?? report?.Side) == fill.Side;
        }
    }

    public async Task<JournalSummary> SummaryAsync(Guid accountId, CancellationToken ct = default, string? scope = null,
        DateTimeOffset? from = null, DateTimeOffset? to = null)
    {
        var orders = await OrdersAsync(accountId, take: int.MaxValue, ct: ct, scope: scope).ConfigureAwait(false);
        var fills = new List<(JournalOrder Order, decimal Quantity, decimal Price, ExecutionReport Report, OrderMarketReference? Reference)>();
        var economicsComplete = orders.Count > 0;
        foreach (var order in orders)
        {
            var activities = order.Events.Where(entry => entry.ActivityFill is not null).Select(entry => entry.ActivityFill!)
                .DistinctBy(fill => fill.FillId).OrderBy(fill => fill.FilledAt).ToArray();
            if (activities.Length > 0 && activities.Sum(fill => fill.Quantity) >= order.FilledQuantity)
            {
                foreach (var fill in activities)
                {
                    if (fill.Quantity <= 0m || fill.Price <= 0m)
                    { economicsComplete = false; continue; }
                    fills.Add((order, fill.Quantity, fill.Price, new ExecutionReport
                    {
                        OrderId = order.OrderId,
                        Symbol = fill.Symbol,
                        Side = fill.Side,
                        OrderStatus = OrderStatus.Filled,
                        ReportType = ExecutionReportType.Fill,
                        FilledQuantity = fill.Quantity,
                        FillPrice = fill.Price,
                        Commission = fill.Commission,
                        Timestamp = fill.FilledAt
                    }, order.Events.FirstOrDefault(entry => entry.Reference is not null)?.Reference));
                }
                continue;
            }
            var watermark = 0m;
            var value = 0m;
            var reference = order.Events.FirstOrDefault(entry => entry.Reference is not null)?.Reference;
            foreach (var entry in order.Events.Where(entry => entry.Report is not null).OrderBy(entry => entry.Report!.Timestamp))
            {
                var report = entry.Report!;
                if (report.FilledQuantity <= watermark || report.FillPrice is not > 0m)
                    continue;
                var price = report.FillPrice.Value;
                var quantity = report.FilledQuantity - watermark;
                if (report.FillPriceIsCumulativeAverage)
                    price = (report.FilledQuantity * price - value) / quantity;
                if (price <= 0m)
                { economicsComplete = false; continue; }
                fills.Add((order, quantity, price, report, reference));
                value += quantity * price;
                watermark = report.FilledQuantity;
            }
            if (watermark < order.FilledQuantity)
                economicsComplete = false;
        }
        // Report only evidence-backed costs. Null is distinct from a broker-reported zero.
        bool InWindow(DateTimeOffset at) => (from is null || at >= from) && (to is null || at < to);
        var windowFills = fills.Where(fill => InWindow(fill.Report.Timestamp)).ToArray();
        decimal? commissions = windowFills.All(fill => fill.Report.Commission.HasValue) ? windowFills.Sum(fill => fill.Report.Commission!.Value) : null;
        decimal? fees = windowFills.All(fill => fill.Report.Fees.HasValue) ? windowFills.Sum(fill => fill.Report.Fees!.Value) : null;
        decimal? slippage = windowFills.All(fill => fill.Reference is not null)
            ? windowFills.Sum(fill => (fill.Price - fill.Reference!.Price) * fill.Quantity * (fill.Report.Side == OrderSide.Buy ? 1m : -1m)) : null;
        var economicOrders = orders.Where(order => order.FilledQuantity > 0m).ToArray();
        economicsComplete &= economicOrders.Length > 0;
        var supportedEconomics = economicOrders.All(order => order.Events.Any(entry => entry.Kind == "Dispatch" && entry.ReportingAssetClass == "USD_US_EQUITY"));
        if (!supportedEconomics)
            slippage = null;
        // An initial holding or unknown history cannot be reconstructed from executions alone.
        var lots = new Dictionary<string, List<ReportingLot>>(StringComparer.OrdinalIgnoreCase);
        var realized = 0m;
        var retained = (await ReadAsync(accountId, ct).ConfigureAwait(false)).Where(entry => scope is null || entry.Scope == scope).ToArray();
        var opening = retained.Where(entry => entry.Opening is not null).ToArray();
        var complete = economicsComplete && supportedEconomics && !retained.Any(entry => entry.Kind == "EconomicCoverageGap")
            && economicOrders.All(order => opening.Any(entry => entry.Scope == order.Scope && entry.Opening is { IsComplete: true, IsFlat: true }));
        if (!economicsComplete)
        { commissions = null; fees = null; slippage = null; }
        var attributed = new List<JournalAttribution>();
        foreach (var fill in fills.OrderBy(fill => fill.Report.Timestamp))
        {
            var symbolScope = $"{fill.Order.Scope}\0{fill.Order.Symbol}";
            if (!lots.TryGetValue(symbolScope, out var book))
                lots[symbolScope] = book = [];
            decimal? costs = fill.Report.Commission is { } commission && fill.Report.Fees is { } fee ? commission + fee : null;
            if (fill.Report.Side == OrderSide.Buy)
            { book.Add(new(fill.Quantity, fill.Quantity * fill.Price, costs, fill.Order.StrategyId, fill.Order.RunId, fill.Order.Tag)); continue; }
            var consumption = LotConsumption.Consume(book, fill.Quantity, lot => lot.Quantity);
            var relief = LotBasisRelief.Allocate(consumption.Slices, lot => lot.Quantity, lot => lot.Basis);
            foreach (var slice in relief)
            {
                var lot = slice.Lot;
                var gross = slice.Quantity * fill.Price - slice.CostBasis;
                if (InWindow(fill.Report.Timestamp))
                    realized += gross;
                decimal? allocated = lot.Costs.HasValue && costs.HasValue
                    ? lot.Costs.Value * slice.Quantity / lot.Quantity + costs.Value * slice.Quantity / fill.Quantity : null;
                if (InWindow(fill.Report.Timestamp))
                    attributed.Add(new(lot.StrategyId, lot.RunId, lot.Tag, gross, allocated.HasValue ? gross - allocated.Value : null));
                if (slice.ClosesLot)
                    book.Remove(lot);
                else
                    book[book.IndexOf(lot)] = lot with
                    {
                        Quantity = lot.Quantity - slice.Quantity,
                        Basis = lot.Basis - slice.CostBasis,
                        Costs = lot.Costs is { } openCost ? openCost - openCost * slice.Quantity / lot.Quantity : null
                    };
            }
            if (consumption.Shortfall > 0m)
                complete = false;
        }
        var attribution = attributed.GroupBy(row => (row.StrategyId, row.RunId, row.Tag)).Select(group => new JournalAttribution(
            group.Key.StrategyId, group.Key.RunId, group.Key.Tag, group.Sum(row => row.GrossRealizedPnl),
            complete && group.All(row => row.NetRealizedPnl.HasValue) ? group.Sum(row => row.NetRealizedPnl!.Value) : null)).ToArray();
        return new(orders.Count(order => order.Events.Any(entry => InWindow(entry.Report?.Timestamp ?? entry.ActivityFill?.FilledAt ?? entry.RecordedAt))),
            windowFills.Length, complete ? realized : null, commissions, fees, slippage,
            complete && commissions.HasValue && fees.HasValue ? "retained-window" : "incomplete",
            NetRealizedPnl: complete && attributed.All(row => row.NetRealizedPnl.HasValue) ? attributed.Sum(row => row.NetRealizedPnl!.Value) : null,
            Attributions: complete ? attribution : []);
    }

    private async Task InitializeLockedAsync(CancellationToken ct)
    {
        if (_initialized)
            return;
        await _wal.InitializeAsync(ct).ConfigureAwait(false);
        var entries = new List<ExecutionJournalEntry>();
        var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
        await foreach (var record in _wal.GetUncommittedRecordsAsync(ct).ConfigureAwait(false))
        {
            if (record.RecordType != "ExecutionJournalV1")
                throw new InvalidDataException("Unknown execution journal record type.");
            var entry = JsonSerializer.Deserialize(record.DeserializePayload<string>()!, ExecutionJsonContext.Default.ExecutionJournalEntry)
                ?? throw new InvalidDataException("Execution journal contains an empty record.");
            if (entry.Version != 1)
                throw new InvalidDataException("Unsupported execution journal version.");
            var key = Key(entry);
            var hash = ContentHash(entry);
            if (hashes.TryGetValue(key, out var previous))
            { if (previous != hash) throw new InvalidDataException("Conflicting retained execution event."); continue; }
            hashes.Add(key, hash);
            entries.Add(entry);
        }
        _entries.Clear();
        _entries.AddRange(entries);
        _hashes.Clear();
        foreach (var pair in hashes)
            _hashes.Add(pair.Key, pair.Value);
        _initialized = true;
    }

    private static string? Metadata(OrderRequest? request, string key) => request?.Metadata?.GetValueOrDefault(key);
    private sealed record ReportingLot(decimal Quantity, decimal Basis, decimal? Costs, string? StrategyId, string? RunId, string? Tag);
    private static string Key(ExecutionJournalEntry entry) => $"{entry.Scope.Length}:{entry.Scope}:{entry.AccountId}:{entry.EventId}";
    private static string Serialize(ExecutionJournalEntry entry) => JsonSerializer.Serialize(entry, ExecutionJsonContext.Default.ExecutionJournalEntry);
    private static string ContentHash(ExecutionJournalEntry entry) => Sha256Digest.ComputeUtf8(Serialize(entry with { RecordedAt = default }));
    public async ValueTask DisposeAsync() { await _wal.DisposeAsync().ConfigureAwait(false); _gate.Dispose(); }
}

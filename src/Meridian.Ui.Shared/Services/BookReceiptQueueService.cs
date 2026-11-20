using System.Globalization;
using System.Text.Json;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.Reconciliation;
using Meridian.Strategies.Services;

namespace Meridian.Ui.Shared.Services;

/// <summary>
/// Retains exact-book receipt observations over authoritative source evidence. This service
/// neither receives money nor posts journals; both remain separately inspectable source facts.
/// </summary>
public sealed partial class BookReceiptQueueService(
    IBookReceiptSource source,
    IBookReceiptRunStore store,
    IReconciliationBreakQueueRepository? exceptions = null) : IBookReceiptQueueService
{
    public const string EngineVersion = "book-receipts-v1";
    private static readonly string[] RequiredCoverage = ["Holdings", "Projections", "Receipts", "Postings"];

    public Task<IReadOnlyList<BookReceiptRunDto>> GetRunsAsync(BookReceiptScopeDto scope, CancellationToken ct = default)
        => store.ReadAsync(scope, ct);

    public async Task<BookReceiptRunDto?> GetRunAsync(BookReceiptScopeDto scope, string runId, CancellationToken ct = default)
        => (await store.ReadAsync(scope, ct).ConfigureAwait(false)).SingleOrDefault(run => run.RunId == runId);

    public async Task<BookReceiptRunDto> RunAsync(BookReceiptQueryDto query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.AsOfDate == default)
            throw new ArgumentException("A receipt run requires an explicit business date.", nameof(query));
        await using var lease = await store.AcquireAsync(query.Scope, ct).ConfigureAwait(false);
        var runs = await store.ReadAsync(query.Scope, ct).ConfigureAwait(false);
        var prior = runs.Where(run => run.AsOfDate <= query.AsOfDate)
            .OrderBy(static run => run.AsOfDate).ThenBy(static run => run.Sequence).ToArray();
        var captured = Canonicalize(await source.LoadAsync(query, ct).ConfigureAwait(false));
        var coverage = RequiredCoverage.Select(area =>
            {
                var rows = captured.Coverage.Where(row => row.Area == area).ToArray();
                return rows.Length == 0
                    ? new BookReceiptCoverageDto(area, false, $"No authoritative {area.ToLowerInvariant()} coverage was supplied.")
                    : new BookReceiptCoverageDto(area, rows.All(static row => row.IsComplete),
                        rows.Any(static row => !row.IsComplete)
                            ? string.Join(" ", rows.Where(static row => !row.IsComplete).Select(row => row.Reason ?? $"{area} evidence is incomplete.").Distinct(StringComparer.Ordinal))
                            : null);
            })
            .Concat(captured.Coverage.Where(row => !RequiredCoverage.Contains(row.Area, StringComparer.Ordinal)))
            .OrderBy(static row => row.Area, StringComparer.Ordinal).ToList();
        var issues = new List<string>();
        var histories = BuildHistories(prior, captured.Expectations, query.AsOfDate, issues);
        var expectations = histories.ToDictionary(static pair => pair.Key, static pair => pair.Value[^1].Expectation, StringComparer.Ordinal);
        var rawReceipts = MergeReceipts(prior.LastOrDefault()?.SourceSnapshot.Receipts ?? [], captured.Receipts);
        var allocatable = PinRetainedReceiptAllocations(prior, rawReceipts, issues);
        var receipts = ValidateReceipts(allocatable, query.AsOfDate, issues, out var aliases);
        string CanonicalReceipt(string id) => aliases.GetValueOrDefault(id) ?? id;
        var incomingGroups = captured.Postings.Select(row => (row.JournalEntryId, Receipt: CanonicalReceipt(row.ReceiptId))).ToHashSet();
        var rawPostings = (prior.LastOrDefault()?.SourceSnapshot.Postings ?? [])
            .Where(old => !incomingGroups.Contains((old.JournalEntryId, CanonicalReceipt(old.ReceiptId))) &&
                !captured.Postings.Any(current => current.PostingId == old.PostingId))
            .Concat(captured.Postings).OrderBy(static row => row.PostingId, StringComparer.Ordinal).ToArray();
        var allocations = Allocate(receipts, expectations, issues);
        var recognized = PinRetainedPostingAllocations(prior, rawPostings, aliases, issues);
        var postings = ValidatePostings(recognized, allocations, receipts, expectations, query.AsOfDate, issues);
        if (issues.Count > 0)
            coverage.Add(new BookReceiptCoverageDto("Validation", false, string.Join(" ", issues.Distinct(StringComparer.Ordinal))));
        var snapshot = new BookReceiptSourceSnapshotDto(captured.Expectations, rawReceipts, rawPostings, coverage);
        var scope = new ReconciliationBreakQueueScope(query.Scope.TenantId, query.Scope.CompanyId);
        var cases = exceptions is null ? [] : await exceptions.GetAllAsync(scope, ct: ct).ConfigureAwait(false);
        var caseById = cases.Where(row => row.LedgerBookId == query.Scope.LedgerBookId)
            .ToDictionary(static row => row.BreakId, StringComparer.Ordinal);
        var items = new List<BookReceiptQueueItemDto>();
        var latestDate = runs.Count == 0 ? query.AsOfDate : runs.Max(static run => run.AsOfDate);
        var priorCurrentObservations = new List<BookReceiptRunDto>();
        DateOnly? highWaterDate = null;
        foreach (var observed in runs.OrderBy(static run => run.Sequence))
        {
            if ((highWaterDate is null || observed.AsOfDate >= highWaterDate.Value) && observed.AsOfDate <= query.AsOfDate)
                priorCurrentObservations.Add(observed);
            if (highWaterDate is null || observed.AsOfDate > highWaterDate.Value)
                highWaterDate = observed.AsOfDate;
        }
        string JournalReversalIdentity(BookReceiptPostingDto posting) => ReconciliationMatchKernel.CreateDeterministicId(
            "journal-reversal", [posting.JournalEntryId, CanonicalReceipt(posting.ReceiptId), posting.ObligationKey]);
        foreach (var expected in expectations.Values.OrderBy(static row => row.DueDate).ThenBy(static row => row.ObligationKey, StringComparer.Ordinal))
        {
            ct.ThrowIfCancellationRequested();
            var key = expected.ObligationKey;
            var linked = receipts.Where(receipt => allocations.TryGetValue(receipt.ReceiptId, out var legs) && legs.ContainsKey(key))
                .Select(receipt => receipt with
                {
                    Allocations = allocations[receipt.ReceiptId].OrderBy(static leg => leg.Key, StringComparer.Ordinal)
                        .Select(static leg => new BookReceiptAllocationDto(leg.Key, Math.Abs(leg.Value))).ToArray()
                }).ToArray();
            var received = allocations.Values.Sum(legs => legs.GetValueOrDefault(key));
            var linkedPostings = postings.Where(posting => posting.ObligationKey == key).ToArray();
            var posted = linkedPostings.Where(static posting => !posting.IsReversed).Sum(static posting => posting.Amount);
            var remaining = Math.Max(0m, expected.ExpectedAmount - received);
            var exceptionId = ReconciliationMatchKernel.CreateDeterministicId("receipt",
                [query.Scope.TenantId, query.Scope.CompanyId, query.Scope.LedgerBookId.ToString("D"), key]);
            caseById.TryGetValue(exceptionId, out var existing);
            var reversals = linked.Where(static receipt => receipt.ReversesReceiptId is not null).ToArray();
            var reversedPostings = linkedPostings.Where(static posting => posting.IsReversed).ToArray();
            var previouslyObserved = priorCurrentObservations.SelectMany(static run => run.Items).Where(row => row.ObligationKey == key).ToArray();
            var priorReversalIds = previouslyObserved.SelectMany(static row => row.Receipts)
                .Where(static row => row.ReversesReceiptId is not null).Select(row => CanonicalReceipt(row.ReceiptId))
                .Concat(previouslyObserved.SelectMany(static row => row.Postings).Where(static row => row.IsReversed).Select(JournalReversalIdentity))
                .ToHashSet(StringComparer.Ordinal);
            // A human disposition after a retained reversal is not undone by observing that same
            // reversal tomorrow. Imports/aliases do not introduce a new economic reversal.
            var reversalIds = reversals.Select(row => CanonicalReceipt(row.ReceiptId))
                .Concat(reversedPostings.Select(JournalReversalIdentity)).Where(id => !priorReversalIds.Contains(id))
                .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            var reversalLinks = reversals.SelectMany(static row => row.EvidenceLinks)
                .Concat(reversedPostings.SelectMany(static row => row.EvidenceLinks))
                .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            if (exceptions is not null && existing is not null && query.AsOfDate >= latestDate && reversalIds.Length > 0 &&
                (received != expected.ExpectedAmount || posted != expected.ExpectedAmount) &&
                existing.Status is ReconciliationBreakQueueStatus.Resolved or ReconciliationBreakQueueStatus.SignedOff or ReconciliationBreakQueueStatus.Dismissed)
            {
                var commandId = ReconciliationMatchKernel.CreateDeterministicId("receipt-reversal",
                    [exceptionId, string.Join("|", reversalIds), existing.Version.ToString(CultureInfo.InvariantCulture)]);
                var result = await exceptions.ApplyBookReceiptReversalAsync(scope, new BookReceiptReversalCommand(
                    exceptionId, query.Scope.LedgerBookId, commandId, existing.Version, reversalIds, reversalLinks, query.AsOfDate), ct).ConfigureAwait(false);
                if (result.Item is null || result.Status != ReconciliationBreakQueueTransitionStatus.Success)
                    throw new InvalidOperationException($"Receipt reversal could not reopen linked exception '{exceptionId}': {result.Error}");
                existing = result.Item;
            }
            var evidenceIssues = coverage.Where(static row => !row.IsComplete).Select(row => row.Reason ?? $"{row.Area} evidence is incomplete.").ToArray();
            var complete = evidenceIssues.Length == 0;
            var disputed = received > expected.ExpectedAmount || posted > received ||
                existing?.LifecycleState == ReconciliationCaseLifecycleState.Investigating ||
                string.Equals(existing?.RootCauseCode, "disputed", StringComparison.OrdinalIgnoreCase);
            var status = disputed ? "Disputed"
                : received > 0m && received < expected.ExpectedAmount ? "PartiallyReceived"
                : received == expected.ExpectedAmount && posted == expected.ExpectedAmount ? complete ? "Reconciled" : "IncompleteEvidence"
                : received >= expected.ExpectedAmount ? "ReceivedButUnposted"
                : expected.DueDate < query.AsOfDate ? "Overdue"
                : expected.DueDate == query.AsOfDate ? "Due" : "Scheduled";
            var links = expected.EvidenceLinks.Concat(linked.SelectMany(static row => row.EvidenceLinks))
                .Concat(linkedPostings.SelectMany(static row => row.EvidenceLinks)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            if (exceptions is not null && existing is null && query.AsOfDate >= latestDate &&
                status is "Overdue" or "PartiallyReceived" or "Disputed" or "ReceivedButUnposted")
            {
                var observedAt = new DateTimeOffset(query.AsOfDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
                var item = new ReconciliationBreakQueueItem(exceptionId, $"book-receipts:{query.AsOfDate:yyyy-MM-dd}",
                    expected.DisplayName, ReconciliationBreakCategory.CashMismatch, ReconciliationBreakQueueStatus.Open,
                    remaining, $"{expected.FlowType} receipt is {status}.", null, observedAt, observedAt,
                    EvidenceLinks: links, SourceType: "BookReceipt", SourceSystem: EngineVersion, SourceReference: key,
                    LedgerBookId: query.Scope.LedgerBookId, AsOfDate: query.AsOfDate)
                { TenantId = query.Scope.TenantId, CompanyId = query.Scope.CompanyId };
                await exceptions.CreateIfMissingAsync(scope, item, ct).ConfigureAwait(false);
                existing = await exceptions.GetByIdAsync(scope, exceptionId, ct).ConfigureAwait(false);
            }
            items.Add(new BookReceiptQueueItemDto(key, expected.PositionId, expected.SecurityId, expected.DisplayName,
                expected.FlowType, expected.DueDate, expected.Currency, status, expected.ExpectedAmount, received, posted, remaining,
                Math.Max(0, query.AsOfDate.DayNumber - expected.DueDate.DayNumber),
                existing?.AssigneeDisplayName ?? existing?.AssignedTo ?? existing?.AssigneeId, existing?.BreakId, complete,
                evidenceIssues, links, histories[key], linked, linkedPostings));
        }
        var rows = items.ToArray();
        var hash = Sha256Digest.ComputeUtf8(string.Join("\n", EngineVersion,
            JsonSerializer.Serialize(query.Scope, BookReceiptJsonContext.Default.BookReceiptScopeDto),
            query.AsOfDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            JsonSerializer.Serialize(snapshot, BookReceiptJsonContext.Default.BookReceiptSourceSnapshotDto),
            JsonSerializer.Serialize(rows, BookReceiptJsonContext.Default.BookReceiptQueueItemDtoArray)));
        var retained = runs.FirstOrDefault(run => run.RunId == hash);
        if (retained is not null)
            return retained;
        var run = new BookReceiptRunDto(hash, EngineVersion, query.Scope, query.AsOfDate, hash, rows, coverage, snapshot,
            runs.Count == 0 ? 1 : runs.Max(static row => row.Sequence) + 1);
        await store.SaveAsync(run, ct).ConfigureAwait(false);
        return run;
    }
}

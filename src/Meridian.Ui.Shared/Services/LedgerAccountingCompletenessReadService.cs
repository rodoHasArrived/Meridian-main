using System.Text.Json;
using Meridian.Application.DirectLending;
using Meridian.Execution.Services;
using Meridian.Ledger;
using Meridian.Storage.DirectLending;
using Meridian.Storage.AssetOperations;
using Meridian.Contracts.AssetOperations;
using Meridian.Storage.Ledger;

namespace Meridian.Ui.Shared.Services;

/// <summary>One exact retained posting population. Source kinds are explicitly selected, never inferred from journal presence.</summary>
public sealed record LedgerAccountingCompletenessQuery(
    Guid LedgerBookId,
    Guid PeriodId,
    Guid AggregateId,
    string FinancialAccountId,
    IReadOnlyList<LedgerAccountingSourceKind> SourceKinds,
    string? SessionId = null);

public sealed record LedgerAccountingSourceCoverage(
    LedgerAccountingSourceKind SourceKind,
    bool IsComplete,
    string? Limitation);

public sealed record LedgerAccountingSourcePopulation(
    IReadOnlyList<LedgerAccountingExpectation> Expectations,
    bool IsComplete,
    string? Limitation = null);

public sealed record LedgerAccountingCompletenessReadResult(
    LedgerAccountingCompletenessQuery Scope,
    LedgerAccountingCompletenessReport Report,
    IReadOnlyList<LedgerAccountingSourceCoverage> Coverage);

/// <summary>
/// Reads independent retained source evidence. Implementations must attest coverage for the exact
/// requested book/period/aggregate/account and must not derive the event population from journals.
/// </summary>
public interface ILedgerAccountingSourceReader
{
    LedgerAccountingSourceKind SourceKind { get; }
    Task<LedgerAccountingSourcePopulation> ReadAsync(
        LedgerAccountingCompletenessQuery query,
        LedgerAccountingPeriod period,
        CancellationToken ct = default);
}

/// <summary>Read-only control over durable sources and the authoritative journal; grants no posting authority.</summary>
public sealed class LedgerAccountingCompletenessReadService
{
    private readonly ILedgerJournalStore _journalStore;
    private readonly IPaperSessionStore? _sessions;
    private readonly IReadOnlyDictionary<LedgerAccountingSourceKind, ILedgerAccountingSourceReader> _readers;

    public LedgerAccountingCompletenessReadService(
        ILedgerJournalStore journalStore,
        IDirectLendingStateStore? lending = null,
        IEnumerable<ILedgerAccountingSourceReader>? readers = null,
        IPaperSessionStore? sessions = null,
        IAssetAccountingEventProjectionStore? assets = null)
    {
        _journalStore = journalStore ?? throw new ArgumentNullException(nameof(journalStore));
        _sessions = sessions;
        var configured = (readers ?? []).ToList();
        if (lending is not null && configured.All(reader => reader.SourceKind != LedgerAccountingSourceKind.LoanEvent))
            configured.Add(new LoanAccountingSourceReader(lending, journalStore));
        if (assets is not null && configured.All(reader => reader.SourceKind != LedgerAccountingSourceKind.AssetEvent))
            configured.Add(new AssetAccountingSourceReader(assets));
        _readers = configured.ToDictionary(reader => reader.SourceKind);
    }

    public async Task<LedgerAccountingCompletenessReadResult> ReadAsync(
        LedgerAccountingCompletenessQuery query,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.LedgerBookId == Guid.Empty || query.PeriodId == Guid.Empty || query.AggregateId == Guid.Empty
            || string.IsNullOrWhiteSpace(query.FinancialAccountId)
            || query.FinancialAccountId != query.FinancialAccountId.Trim())
            throw new ArgumentException("Completeness requires exact book, period, aggregate and account identities.", nameof(query));
        if (query.SourceKinds is null || query.SourceKinds.Count == 0
            || query.SourceKinds.Distinct().Count() != query.SourceKinds.Count
            || query.SourceKinds.Any(kind => !Enum.IsDefined(kind)))
            throw new ArgumentException("Completeness requires distinct, recognized source populations.", nameof(query));
        var period = await _journalStore.GetPeriodAsync(query.PeriodId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The requested accounting period does not exist.");
        if (period.LedgerBookId != query.LedgerBookId)
            throw new InvalidOperationException("The requested accounting period belongs to a different ledger book.");
        PaperSessionAccountingBinding? sessionBinding = null;
        if (_sessions is not null && !string.IsNullOrWhiteSpace(query.SessionId))
        {
            var sessions = await _sessions.LoadAllSessionsAsync(ct).ConfigureAwait(false);
            var session = sessions.SingleOrDefault(item => item.SessionId == query.SessionId);
            if (session?.AccountingContext is { } context && context.AggregateId == query.AggregateId
                && context.LedgerBookId == query.LedgerBookId && context.PeriodId == query.PeriodId
                && session.FinancialAccountId == query.FinancialAccountId)
                sessionBinding = new(session.SessionId, context.AggregateId, context.LedgerBookId, context.PeriodId, session.FinancialAccountId);
        }
        var sources = new List<LedgerAccountingExpectation>();
        var coverage = new List<LedgerAccountingSourceCoverage>();
        foreach (var kind in query.SourceKinds.Order())
        {
            _readers.TryGetValue(kind, out var reader);
            if (kind == LedgerAccountingSourceKind.Fill && sessionBinding is not null && _sessions is not null)
                reader = new PaperSessionAccountingSourceReader(_sessions, sessionBinding);
            if (reader is null)
            {
                coverage.Add(new(kind, false, "No complete retained source reader is configured for this population."));
                continue;
            }
            var population = await reader.ReadAsync(query, period, ct).ConfigureAwait(false);
            sources.AddRange(population.Expectations);
            coverage.Add(new(kind, population.IsComplete, population.Limitation));
        }
        var records = await _journalStore.QueryAsync(new(
            LedgerBookId: query.LedgerBookId, PeriodId: query.PeriodId, AggregateId: query.AggregateId), ct).ConfigureAwait(false);
        var observations = new List<LedgerAccountingJournalObservation>();
        var journalPopulationComplete = true;
        foreach (var record in records)
        {
            // Never permit a faulty adapter to widen a supposedly exact read.
            if (record.AggregateId != query.AggregateId || record.PeriodId != query.PeriodId)
                throw new InvalidDataException("The journal store returned a record outside the requested accounting scope.");
            var metadata = record.Entry.Metadata;
            var kind = Classify(record);
            if (metadata.Tags?.ContainsKey(LedgerJournalReversal.ReversalOfTag) == true
                && kind != LedgerAccountingSourceKind.LoanEvent)
                continue;
            if (kind is null)
            {
                if (metadata.FinancialAccountId is { } unrelatedAccount && unrelatedAccount != query.FinancialAccountId)
                    continue;
                // An event-linked journal whose source family cannot be established is not proof
                // of complete population coverage. Unrelated manual journals are outside this control.
                if (record.SourceEventId is not null)
                    journalPopulationComplete = false;
                continue;
            }
            if (!query.SourceKinds.Contains(kind.Value))
                continue;
            // Loan reversal/adjustment events have their own independently retained source event
            // and may legitimately use an adjustment posting kind. A correction receipt linked
            // to an original trade/asset is outside its originating source population.
            if (record.PostingKind != Meridian.Contracts.Ledger.LedgerPostingKindDto.Originating
                && kind != LedgerAccountingSourceKind.LoanEvent)
                continue;
            var account = metadata.FinancialAccountId
                ?? record.Entry.Lines.Select(line => line.Account.FinancialAccountId)
                    .Where(value => value is not null).Distinct(StringComparer.Ordinal).SingleOrDefault();
            if (account is null && kind == LedgerAccountingSourceKind.Fill
                && sessionBinding?.FinancialAccountId == "default")
                account = "default";
            if (account is null)
            {
                journalPopulationComplete = false;
                continue;
            }
            var scope = new LedgerAccountingScope(query.LedgerBookId.ToString("D"), account);
            var sourceId = kind == LedgerAccountingSourceKind.Fill ? metadata.FillId?.ToString("D")
                : record.SourceEventId?.ToString("D");
            if (account != query.FinancialAccountId
                && !sources.Any(source => source.SourceKind == kind.Value && source.SourceId == sourceId))
                continue;
            var postingKey = kind is LedgerAccountingSourceKind.Fill or LedgerAccountingSourceKind.AssetEvent
                ? metadata.ActivityType : record.RuleId ?? metadata.ActivityType;
            if (string.IsNullOrWhiteSpace(postingKey))
            {
                journalPopulationComplete = false;
                continue;
            }
            observations.Add(new(record.Entry, scope, kind.Value, sourceId, postingKey));
        }
        var report = LedgerAccountingCompleteness.Reconcile(sources, observations,
            coverage.All(item => item.IsComplete), journalPopulationComplete);
        return new(query, report, coverage.AsReadOnly());
    }

    private static LedgerAccountingSourceKind? Classify(LedgerJournalEntryRecord record)
    {
        var metadata = record.Entry.Metadata;
        if (metadata.FillId is not null)
            return LedgerAccountingSourceKind.Fill;
        if (metadata.Institution == "DirectLending" || metadata.Tags?.ContainsKey("loanId") == true)
            return LedgerAccountingSourceKind.LoanEvent;
        if (metadata.ActivityType?.StartsWith("AssetAccounting.", StringComparison.Ordinal) == true
            || record.RuleId?.StartsWith("AssetAccounting.", StringComparison.Ordinal) == true)
            return LedgerAccountingSourceKind.AssetEvent;
        if (metadata.ActivityType == "margin-interest" || record.RuleId == "execution.margin-interest")
            return LedgerAccountingSourceKind.FinancingCharge;
        return null;
    }
}

/// <summary>Uses retained loan history and the same impact kernel as originating loan posting.</summary>
public sealed class LoanAccountingSourceReader(IDirectLendingStateStore store, ILedgerJournalStore journals) : ILedgerAccountingSourceReader
{
    public LedgerAccountingSourceKind SourceKind => LedgerAccountingSourceKind.LoanEvent;

    public async Task<LedgerAccountingSourcePopulation> ReadAsync(
        LedgerAccountingCompletenessQuery query, LedgerAccountingPeriod period, CancellationToken ct = default)
    {
        if (query.FinancialAccountId != query.AggregateId.ToString("D"))
            return new([], false, "The selected financial account is not the retained loan identity.");
        // The loan store is not tenant scoped. Prove a retained loan-to-book binding through
        // the caller-authorized book before reading independently retained loan history.
        // A loan without that binding is unavailable, never an inferred zero population.
        var binding = await journals.QueryAsync(new(LedgerBookId: query.LedgerBookId,
            AggregateId: query.AggregateId), ct).ConfigureAwait(false);
        if (!binding.Any(record => record.AggregateId == query.AggregateId &&
                (record.Entry.Metadata.Institution == "DirectLending" ||
                 record.Entry.Metadata.Tags?.GetValueOrDefault("loanId") == query.AggregateId.ToString("D")) &&
                (record.Entry.Metadata.FinancialAccountId == query.FinancialAccountId ||
                 record.Entry.Lines.Any(line => line.Account.FinancialAccountId == query.FinancialAccountId))))
            return new([], false, "No authoritative loan-to-book binding is retained; the loan source population is unavailable.");
        var contract = await store.LoadContractProjectionAsync(query.AggregateId, ct).ConfigureAwait(false);
        if (contract is null)
            return new([], false, "The retained loan contract is unavailable.");
        var history = await store.GetHistoryAsync(query.AggregateId, ct).ConfigureAwait(false);
        var scope = new LedgerAccountingScope(query.LedgerBookId.ToString("D"), query.FinancialAccountId);
        var expectations = new List<LedgerAccountingExpectation>();
        foreach (var source in history)
        {
            var accountingDate = source.EffectiveDate ?? contract.EffectiveDate;
            if (accountingDate < period.StartDate || accountingDate > period.EndDate)
                continue;
            using var payload = JsonDocument.Parse(source.PayloadJson);
            var hasImpact = LoanAccountingProjector.HasAccountingImpact(source.EventType, payload.RootElement);
            expectations.Add(new(scope, SourceKind, source.EventId.ToString("D"), hasImpact ? [source.EventType] : []));
        }
        return new(expectations.AsReadOnly(), true);
    }
}

/// <summary>A server-resolved source-to-book binding, never a caller's assertion of account ownership.</summary>
public sealed record PaperSessionAccountingBinding(
    string SessionId, Guid AggregateId, Guid LedgerBookId, Guid PeriodId, string FinancialAccountId);

/// <summary>
/// Reads full retained paper-session fills, not the compacting pending-fill WAL. The binding must
/// come from the owning session's trusted execution composition. Variation-margin populations need
/// retained realized-P&amp;L/settlement policy and remain explicitly incomplete here.
/// </summary>
public sealed class PaperSessionAccountingSourceReader(
    IPaperSessionStore store,
    PaperSessionAccountingBinding binding) : ILedgerAccountingSourceReader
{
    public LedgerAccountingSourceKind SourceKind => LedgerAccountingSourceKind.Fill;

    public async Task<LedgerAccountingSourcePopulation> ReadAsync(
        LedgerAccountingCompletenessQuery query, LedgerAccountingPeriod period, CancellationToken ct = default)
    {
        if (query.AggregateId != binding.AggregateId || query.LedgerBookId != binding.LedgerBookId
            || query.PeriodId != binding.PeriodId || query.FinancialAccountId != binding.FinancialAccountId)
            return new([], false, "The paper session is not bound to this exact accounting scope.");
        var sessions = await store.LoadAllSessionsAsync(ct).ConfigureAwait(false);
        var matches = sessions.Where(session => session.SessionId == binding.SessionId).ToArray();
        if (matches.Length != 1)
            return new([], false, "The exact retained paper session is unavailable or ambiguous.");
        var session = matches[0];
        if (session.AccountingContext is not { } context || context.AggregateId != binding.AggregateId
            || context.LedgerBookId != binding.LedgerBookId || context.PeriodId != binding.PeriodId
            || session.FinancialAccountId != binding.FinancialAccountId)
            return new([], false, "The retained paper session lacks the exact authoritative accounting binding.");
        var records = await store.LoadFillRecordsAsync(binding.SessionId, ct).ConfigureAwait(false);
        var scope = new LedgerAccountingScope(query.LedgerBookId.ToString("D"), query.FinancialAccountId);
        var expectations = new List<LedgerAccountingExpectation>();
        var complete = true;
        foreach (var source in records)
        {
            source.Validate();
            if (source.FillId != PaperSessionFillRecord.ComputeCanonicalFillId(source.Fill))
                throw new InvalidDataException("A retained paper fill does not carry its canonical execution identity.");
            var date = DateOnly.FromDateTime(source.Fill.Timestamp.UtcDateTime);
            if (date < period.StartDate || date > period.EndDate)
                continue;
            if (source.Fill.BookedVariationMargin == true)
            {
                complete = false;
                continue;
            }
            var keys = new List<string> { "trade-fill" };
            if (source.Fill.Commission > 0m)
                keys.Add("commission");
            expectations.Add(new(scope, SourceKind, source.FillId.ToString("D"), keys.AsReadOnly()));
        }
        return new(expectations.AsReadOnly(), complete,
            complete ? null : "Variation-margin fills require retained settlement and realized-P&L policy coverage.");
    }
}

/// <summary>Enumerates the independently retained asset-event spine for the exact book and period.</summary>
public sealed class AssetAccountingSourceReader(IAssetAccountingEventProjectionStore store) : ILedgerAccountingSourceReader
{
    public LedgerAccountingSourceKind SourceKind => LedgerAccountingSourceKind.AssetEvent;

    public async Task<LedgerAccountingSourcePopulation> ReadAsync(
        LedgerAccountingCompletenessQuery query, LedgerAccountingPeriod period, CancellationToken ct = default)
    {
        if (query.AggregateId != query.LedgerBookId)
            return new([], false, "Canonical asset journals use their ledger book as aggregate identity.");
        IReadOnlyList<AssetAccountingEventProjectionRecord> records;
        try
        {
            records = await store.ListByBookPeriodAsync(query.LedgerBookId, query.PeriodId, ct).ConfigureAwait(false);
        }
        catch (NotSupportedException)
        {
            return new([], false, "The asset-event store cannot enumerate this complete source population.");
        }
        var expectations = new List<LedgerAccountingExpectation>();
        var complete = true;
        var seen = new HashSet<Guid>();
        foreach (var record in records)
        {
            var source = record.Projection;
            if (source.Scope.LedgerBookId != query.LedgerBookId || source.Scope.PeriodId != query.PeriodId)
                throw new InvalidDataException("The asset-event store returned a source outside the requested accounting scope.");
            // Source-event version reuse needs a versioned originating/correction posting manifest.
            // Never collapse two versions into one exact-once assertion.
            if (!seen.Add(source.EventId))
            {
                complete = false;
                continue;
            }
            var account = source.Scope.Dimensions?.AccountId;
            if (string.IsNullOrWhiteSpace(account))
            {
                complete = false;
                continue;
            }
            if (account != query.FinancialAccountId)
                continue;
            if (source.ProjectedEffect is not { } effect)
            {
                // Cash/event amount cannot prove the accounting effect of a noncash event.
                // An Expected spine is pending projection, never an attested zero journal.
                complete = false;
                continue;
            }
            if (!effect.IsBalanced || effect.Lines.Count == 0 ||
                effect.Lines.Sum(line => line.Debit) != effect.TotalDebits ||
                effect.Lines.Sum(line => line.Credit) != effect.TotalCredits ||
                !string.Equals(effect.Currency, source.Currency, StringComparison.OrdinalIgnoreCase) ||
                effect.Lines.Any(line => line.Debit < 0m || line.Credit < 0m ||
                    (line.Debit == 0m) == (line.Credit == 0m) ||
                    !string.Equals(line.Currency, effect.Currency, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("A retained asset-event accounting effect is unbalanced or inconsistent with its lines and currency.");
            var hasImpact = effect.TotalDebits != 0m || effect.TotalCredits != 0m;
            expectations.Add(new(new(query.LedgerBookId.ToString("D"), account), SourceKind,
                source.EventId.ToString("D"), hasImpact ? [AssetAccountingEventTypeNames.For(source.EventKind)] : []));
        }
        return new(expectations.AsReadOnly(), complete, complete ? null
            : "Some retained asset sources lack account lineage or a projected accounting effect, or require an explicit versioned correction posting manifest.");
    }
}

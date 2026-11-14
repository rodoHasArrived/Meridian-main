using System.Text.Json;
using Meridian.Application.DirectLending;
using Meridian.Execution.Events;
using Meridian.Execution.Services;
using Meridian.Ledger;
using Meridian.Storage.DirectLending;
using Meridian.Storage.AssetOperations;
using Meridian.Contracts.AssetOperations;
using Meridian.Storage.Ledger;

namespace Meridian.Ui.Shared.Services;

/// <summary>One exact retained account posting population. Source kinds are explicitly selected,
/// never inferred from journal presence. SessionId is an optional verified scope anchor; all
/// retained sessions sharing the account scope remain in the source population.</summary>
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
    string? Limitation)
{
    public IReadOnlyList<string> CoveredSessionIds { get; init; } = [];
}

public sealed record LedgerAccountingSourcePopulation(
    IReadOnlyList<LedgerAccountingExpectation> Expectations,
    bool IsComplete,
    string? Limitation = null)
{
    public IReadOnlyList<string> CoveredSessionIds { get; init; } = [];
}

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
    private readonly PaperFinancingPostingService? _financing;
    private readonly IReadOnlyDictionary<LedgerAccountingSourceKind, ILedgerAccountingSourceReader> _readers;

    public LedgerAccountingCompletenessReadService(
        ILedgerJournalStore journalStore,
        IDirectLendingStateStore? lending = null,
        IEnumerable<ILedgerAccountingSourceReader>? readers = null,
        IPaperSessionStore? sessions = null,
        IAssetAccountingEventProjectionStore? assets = null,
        PaperFinancingPostingService? financing = null)
    {
        _journalStore = journalStore ?? throw new ArgumentNullException(nameof(journalStore));
        _sessions = sessions;
        _financing = financing;
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
        if (_sessions is not null)
        {
            var sessions = await _sessions.LoadAllSessionsAsync(ct).ConfigureAwait(false);
            var session = string.IsNullOrWhiteSpace(query.SessionId)
                ? sessions.Where(item => PaperAccountingSessionPopulation.MatchesScope(item, query))
                    .OrderBy(item => item.SessionId, StringComparer.Ordinal).FirstOrDefault()
                : sessions.SingleOrDefault(item => item.SessionId == query.SessionId);
            if (session is not null && PaperAccountingSessionPopulation.MatchesScope(session, query)
                && session.AccountingContext is { } context)
                sessionBinding = new(session.SessionId, context.AggregateId, context.LedgerBookId, context.PeriodId, session.FinancialAccountId);
        }
        var sources = new List<LedgerAccountingExpectation>();
        var coverage = new List<LedgerAccountingSourceCoverage>();
        foreach (var kind in query.SourceKinds.Order())
        {
            if ((kind is LedgerAccountingSourceKind.Fill or LedgerAccountingSourceKind.FinancingCharge)
                && !string.IsNullOrWhiteSpace(query.SessionId) && sessionBinding is null)
            {
                coverage.Add(new(kind, false, "The requested paper-session anchor has no matching retained accounting binding."));
                continue;
            }
            _readers.TryGetValue(kind, out var reader);
            if (kind == LedgerAccountingSourceKind.Fill && sessionBinding is not null && _sessions is not null)
                reader = new PaperSessionAccountingSourceReader(_sessions, sessionBinding);
            if (kind == LedgerAccountingSourceKind.FinancingCharge && sessionBinding is not null && _sessions is not null && _financing is not null)
                reader = new PaperFinancingAccountingSourceReader(_sessions, sessionBinding, _financing);
            if (reader is null)
            {
                coverage.Add(new(kind, false, "No complete retained source reader is configured for this population."));
                continue;
            }
            var population = await reader.ReadAsync(query, period, ct).ConfigureAwait(false);
            sources.AddRange(population.Expectations);
            coverage.Add(new(kind, population.IsComplete, population.Limitation)
            { CoveredSessionIds = population.CoveredSessionIds });
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
            if (account is null && (kind is LedgerAccountingSourceKind.Fill or LedgerAccountingSourceKind.FinancingCharge)
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

internal static class PaperAccountingSessionPopulation
{
    internal sealed record Selection(IReadOnlyList<PersistedSessionRecord> Sessions, bool IsComplete, string? Limitation = null);

    private static bool MatchesLedgerScope(PersistedSessionRecord session, LedgerAccountingCompletenessQuery query)
        => session.AccountingContext is { } context && context.AggregateId == query.AggregateId
            && context.LedgerBookId == query.LedgerBookId && context.PeriodId == query.PeriodId;

    internal static bool MatchesScope(PersistedSessionRecord session, LedgerAccountingCompletenessQuery query)
        => MatchesLedgerScope(session, query) && session.FinancialAccountId == query.FinancialAccountId;

    internal static async Task<Selection> ReadAsync(
        IPaperSessionStore store, PaperSessionAccountingBinding binding,
        LedgerAccountingCompletenessQuery query, CancellationToken ct)
    {
        if (query.AggregateId != binding.AggregateId || query.LedgerBookId != binding.LedgerBookId
            || query.PeriodId != binding.PeriodId || query.FinancialAccountId != binding.FinancialAccountId)
            return new([], false, "The paper session is not bound to this exact accounting scope.");
        var sessions = await store.LoadAllSessionsAsync(ct).ConfigureAwait(false);
        var anchors = sessions.Where(session => session.SessionId == binding.SessionId).ToArray();
        if (anchors.Length != 1 || !MatchesScope(anchors[0], query))
            return new([], false, "The retained paper session lacks the exact authoritative accounting binding.");
        if (!string.IsNullOrWhiteSpace(query.SessionId) &&
            sessions.Count(session => session.SessionId == query.SessionId && MatchesScope(session, query)) != 1)
            return new([], false, "The requested paper-session anchor has no matching retained accounting binding.");
        var population = sessions.Where(session => MatchesScope(session, query))
            .OrderBy(session => session.SessionId, StringComparer.Ordinal).ToArray();
        if (population.Any(session => string.IsNullOrWhiteSpace(session.SessionId)) ||
            population.Any(selected => sessions.Count(session => session.SessionId == selected.SessionId) != 1))
            throw new InvalidDataException("The retained paper-session accounting population has an ambiguous session identity.");
        var ambiguous = sessions.Any(session => MatchesLedgerScope(session, query) && string.IsNullOrWhiteSpace(session.FinancialAccountId));
        return new(population, !ambiguous, ambiguous
            ? "Some retained sessions in this ledger scope lack financial-account lineage." : null);
    }
}

/// <summary>
/// Reads full retained fills from every session sharing the exact account posting scope, not the
/// compacting pending-fill WAL. The anchor binding must come from trusted retained session metadata.
/// Closed sessions remain part of the population. Variation-margin populations need
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
        var population = await PaperAccountingSessionPopulation.ReadAsync(store, binding, query, ct).ConfigureAwait(false);
        var sessions = population.Sessions;
        if (sessions.Count == 0)
            return new([], false, population.Limitation);
        var scope = new LedgerAccountingScope(query.LedgerBookId.ToString("D"), query.FinancialAccountId);
        var expectations = new List<LedgerAccountingExpectation>();
        var complete = population.IsComplete;
        var limitation = population.Limitation;
        foreach (var session in sessions)
        {
            var records = await store.LoadFillRecordsAsync(session.SessionId, ct).ConfigureAwait(false);
            foreach (var source in records)
            {
                source.Validate();
                if (source.FillId != PaperSessionFillRecord.ComputeCanonicalFillId(source.Fill))
                    throw new InvalidDataException("A retained paper fill does not carry its canonical execution identity.");
                var date = DateOnly.FromDateTime(source.Fill.Timestamp.UtcDateTime);
                if (date < period.StartDate || date > period.EndDate)
                {
                    complete = false;
                    limitation ??= "A retained fill falls outside the session's bound accounting period.";
                    continue;
                }
                if (source.Fill.BookedVariationMargin == true)
                {
                    complete = false;
                    limitation ??= "Variation-margin fills require retained settlement and realized-P&L policy coverage.";
                    continue;
                }
                var keys = new List<string> { "trade-fill" };
                if (source.Fill.Commission > 0m)
                    keys.Add("commission");
                expectations.Add(new(scope, SourceKind, source.FillId.ToString("D"), keys.AsReadOnly()));
            }
        }
        return new(expectations.AsReadOnly(), complete, limitation)
        { CoveredSessionIds = Array.AsReadOnly(sessions.Select(session => session.SessionId).ToArray()) };
    }
}

/// <summary>Reads independent financing assessments for the same exhaustive retained session population.</summary>
public sealed class PaperFinancingAccountingSourceReader(
    IPaperSessionStore store, PaperSessionAccountingBinding binding,
    PaperFinancingPostingService financing) : ILedgerAccountingSourceReader
{
    public LedgerAccountingSourceKind SourceKind => LedgerAccountingSourceKind.FinancingCharge;

    public async Task<LedgerAccountingSourcePopulation> ReadAsync(
        LedgerAccountingCompletenessQuery query, LedgerAccountingPeriod period, CancellationToken ct = default)
    {
        var population = await PaperAccountingSessionPopulation.ReadAsync(store, binding, query, ct).ConfigureAwait(false);
        var sessions = population.Sessions;
        if (sessions.Count == 0)
            return new([], false, population.Limitation);
        var ownedSessions = sessions.Where(session => session.AccountingContext == financing.PostingContext).ToArray();
        var complete = population.IsComplete && ownedSessions.Length == sessions.Count;
        var limitation = ownedSessions.Length == sessions.Count ? population.Limitation
            : "Not every retained session in this account scope has its financing source owner available.";
        var scope = new LedgerAccountingScope(query.LedgerBookId.ToString("D"), query.FinancialAccountId);
        var expectations = new List<LedgerAccountingExpectation>();
        foreach (var session in ownedSessions)
        {
            var charges = await financing.LoadSourcesAsync(session.SessionId, ct).ConfigureAwait(false);
            foreach (var charge in charges)
            {
                charge.Validate();
                if (charge.SessionId != session.SessionId || charge.AccountId != query.FinancialAccountId)
                    throw new InvalidDataException("The financing source owner returned an assessment outside the retained session/account scope.");
                if (charge.AccrualDate < period.StartDate || charge.AccrualDate > period.EndDate)
                    throw new InvalidDataException("A bound financing assessment falls outside its retained accounting period.");
                expectations.Add(new(scope, SourceKind, charge.SourceEventId.ToString("D"), ["execution.margin-interest"]));
            }
        }
        return new(expectations.AsReadOnly(), complete, limitation)
        { CoveredSessionIds = Array.AsReadOnly(ownedSessions.Select(session => session.SessionId).ToArray()) };
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

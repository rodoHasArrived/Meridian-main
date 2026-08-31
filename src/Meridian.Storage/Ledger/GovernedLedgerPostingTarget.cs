using Meridian.Ledger;
using Npgsql;
using static Meridian.Contracts.Text.TextPrimitives;
using static Meridian.Storage.Ledger.LedgerRetainedValueComparison;

namespace Meridian.Storage.Ledger;

/// <summary>
/// Durable posting seam used after a governed workflow has approved a journal write.
/// The workflow remains responsible for validation and approval; this target owns the
/// idempotent handoff to the authoritative journal store.
/// </summary>
public interface IGovernedLedgerPostingTarget
{
    Task<GovernedLedgerPostingResult> PostAsync(
        LedgerJournalEntryWrite write,
        CancellationToken ct = default);
}

public sealed record GovernedLedgerPostingResult(
    Guid JournalEntryId,
    bool WasAppended);

/// <summary>
/// Serializes the check-and-append handoff for one process and treats an equivalent
/// retained posting identity as a successful retry. Global journal/command collisions
/// and aggregate-scoped source/idempotency collisions with different accounting content
/// fail closed.
/// </summary>
public sealed class DurableLedgerPostingTarget : IGovernedLedgerPostingTarget, IDisposable
{
    private readonly ILedgerJournalStore _store;
    private readonly SemaphoreSlim _writeGate = new(1, 1);

    public DurableLedgerPostingTarget(ILedgerJournalStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public async Task<GovernedLedgerPostingResult> PostAsync(
        LedgerJournalEntryWrite write,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(write);
        ArgumentNullException.ThrowIfNull(write.Entry);
        write = NormalizeWrite(AccountingPostingCommandValidator.NormalizeAndValidate(write));

        await _writeGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var identity = LedgerPostingIdentity.FromWrite(write);
            var collisions = await _store
                .FindPostingIdentityCollisionsAsync(identity, ct)
                .ConfigureAwait(false);
            if (collisions.Count > 0)
            {
                return ResolveRetainedCollision(collisions, write);
            }

            try
            {
                await _store.AppendAsync(write, ct).ConfigureAwait(false);
            }
            catch (PostgresException exception)
                when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
            {
                collisions = await _store
                    .FindPostingIdentityCollisionsAsync(identity, ct)
                    .ConfigureAwait(false);
                if (collisions.Count == 0)
                    throw;

                return ResolveRetainedCollision(collisions, write);
            }

            return new GovernedLedgerPostingResult(write.Entry.JournalEntryId, WasAppended: true);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public void Dispose() => _writeGate.Dispose();

    private static GovernedLedgerPostingResult ResolveRetainedCollision(
        IReadOnlyList<LedgerJournalEntryRecord> collisions,
        LedgerJournalEntryWrite requested)
    {
        foreach (var collision in collisions)
        {
            EnsureEquivalent(collision, requested);
        }

        var retained = collisions
            .OrderBy(static record => record.GlobalSequence)
            .ThenBy(static record => record.CreatedAt)
            .ThenBy(static record => record.Entry.JournalEntryId)
            .First();
        return new GovernedLedgerPostingResult(retained.Entry.JournalEntryId, WasAppended: false);
    }

    private static void EnsureEquivalent(
        LedgerJournalEntryRecord existing,
        LedgerJournalEntryWrite requested)
    {
        var retained = existing.Entry;
        var candidate = requested.Entry;
        var equivalent = existing.AggregateId == requested.AggregateId
            && existing.PeriodId == requested.PeriodId
            && existing.CommandId == requested.CommandId
            && existing.CorrelationId == requested.CorrelationId
            && existing.AccountingBasis == requested.AccountingBasis
            && string.Equals(existing.AccountingPolicyId, requested.AccountingPolicyId, StringComparison.Ordinal)
            && string.Equals(existing.AccountingPolicyVersion, requested.AccountingPolicyVersion, StringComparison.Ordinal)
            && string.Equals(existing.RuleId, requested.RuleId, StringComparison.Ordinal)
            && string.Equals(existing.RuleVersion, requested.RuleVersion, StringComparison.Ordinal)
            && existing.SourceEventId == requested.SourceEventId
            && existing.SourceJournalEntryId == requested.SourceJournalEntryId
            && existing.PostingKind == requested.PostingKind
            && existing.AdjustmentApproval == requested.AdjustmentApproval
            && TimestampsMatch(retained.Timestamp, candidate.Timestamp)
            && string.Equals(retained.Description, candidate.Description, StringComparison.Ordinal)
            && MetadataEquivalent(retained.Metadata, candidate.Metadata)
            && JournalLinesEquivalent(retained.Lines, candidate.Lines);

        if (!equivalent)
        {
            throw new LedgerValidationException(
                $"Journal entry '{candidate.JournalEntryId}' is already retained with different accounting content.");
        }
    }

    /// <summary>
    /// Pairs every retained line with a distinct candidate line, exactly and in bounded time.
    /// <para>
    /// Line order is not durable here, so lines are compared as a multiset. Currency compatibility
    /// is deliberately one-directional — a retained identity translation is equivalent to a
    /// candidate that declares nothing, but not the reverse — which makes line equivalence a
    /// non-symmetric relation. Taking the first available match over a non-symmetric relation is
    /// order-dependent: it can spend a blind candidate on a retained line that carries detail,
    /// then strand a retained blind line against the detailed candidate left over, and report a
    /// multiset that pairs up perfectly as different accounting content.
    /// </para>
    /// <para>
    /// Everything except currency <i>is</i> an equivalence relation, so the lines partition into
    /// groups that can only pair within themselves, and the asymmetry is confined to one small
    /// question inside each group. Solving it there by counting needs no search: journals are not
    /// bounded above — <c>AccountingJournalDraftService.BuildDraftEntry</c> accepts as many lines
    /// as a request carries — so a general matching over mutually-equivalent legs would let an
    /// allocation-sized journal monopolise the posting gate, and a recursive one would run out of
    /// stack before that.
    /// </para>
    /// </summary>
    private static bool JournalLinesEquivalent(
        IReadOnlyList<LedgerEntry> retained,
        IReadOnlyList<LedgerEntry> candidate)
    {
        if (retained.Count != candidate.Count)
            return false;

        var groups = new List<LineGroup>();
        foreach (var line in retained)
        {
            var group = FindGroup(groups, line);
            if (group is null)
            {
                group = new LineGroup(line);
                groups.Add(group);
            }

            group.Retained.Add(line);
        }

        foreach (var line in candidate)
        {
            // No retained line this candidate could pair with. Counts are equal, so some retained
            // line is unpairable too.
            if (FindGroup(groups, line) is not { } group)
                return false;

            group.Candidates.Add(line);
        }

        foreach (var group in groups)
        {
            if (!GroupPairsUp(group))
                return false;
        }

        return true;
    }

    private static LineGroup? FindGroup(List<LineGroup> groups, LedgerEntry line)
    {
        foreach (var group in groups)
        {
            if (LinesEquivalentApartFromCurrency(group.Representative, line))
                return group;
        }

        return null;
    }

    /// <summary>
    /// Resolves one group's currency question by counting rather than searching.
    /// <para>
    /// Within a group every line agrees on its functional amounts, so a detail is either a label
    /// for those amounts or a claim about a different denomination. Retained blind lines are the
    /// most constrained — only a blind candidate will do — so they are served first; a detailed
    /// retained line takes a candidate carrying the same detail, and only a label may fall back to
    /// a blind candidate none of the above needed.
    /// </para>
    /// </summary>
    private static bool GroupPairsUp(LineGroup group)
    {
        if (group.Retained.Count != group.Candidates.Count)
            return false;

        var spareBlind = 0;
        var candidateDetails = new List<(LedgerEntryCurrency Detail, int Remaining)>();
        foreach (var line in group.Candidates)
        {
            if (line.Currency is null)
            {
                spareBlind++;
                continue;
            }

            AddDetail(candidateDetails, line.Currency);
        }

        var blindRetained = group.Retained.Count(line => line.Currency is null);
        if (blindRetained > spareBlind)
            return false;

        spareBlind -= blindRetained;

        foreach (var line in group.Retained)
        {
            if (line.Currency is null || TryTakeDetail(candidateDetails, line.Currency))
                continue;

            if (spareBlind == 0
                || !IsIdentityTranslation(line.Currency, group.Representative.Debit, group.Representative.Credit))
            {
                return false;
            }

            spareBlind--;
        }

        return true;
    }

    private static void AddDetail(
        List<(LedgerEntryCurrency Detail, int Remaining)> details,
        LedgerEntryCurrency detail)
    {
        for (var index = 0; index < details.Count; index++)
        {
            if (CurrencyDetailsMatch(details[index].Detail, detail))
            {
                details[index] = (details[index].Detail, details[index].Remaining + 1);
                return;
            }
        }

        details.Add((detail, 1));
    }

    private static bool TryTakeDetail(
        List<(LedgerEntryCurrency Detail, int Remaining)> details,
        LedgerEntryCurrency detail)
    {
        for (var index = 0; index < details.Count; index++)
        {
            if (details[index].Remaining > 0 && CurrencyDetailsMatch(details[index].Detail, detail))
            {
                details[index] = (details[index].Detail, details[index].Remaining - 1);
                return true;
            }
        }

        return false;
    }

    /// <summary>Lines that agree on everything except transaction-currency detail.</summary>
    private sealed class LineGroup(LedgerEntry representative)
    {
        public LedgerEntry Representative { get; } = representative;

        public List<LedgerEntry> Retained { get; } = [];

        public List<LedgerEntry> Candidates { get; } = [];
    }

    /// <summary>
    /// Everything a line comparison looks at except transaction-currency detail. Each part is
    /// symmetric and transitive, so this is an equivalence relation and lines partition by it —
    /// which is what lets the currency question be settled group by group rather than by searching
    /// the whole pairing.
    /// </summary>
    private static bool LinesEquivalentApartFromCurrency(LedgerEntry retained, LedgerEntry candidate)
        => TimestampsMatch(retained.Timestamp, candidate.Timestamp)
           && retained.Account.AccountType == candidate.Account.AccountType
           && string.Equals(retained.Account.Name, candidate.Account.Name, StringComparison.Ordinal)
           && string.Equals(retained.Account.Symbol, candidate.Account.Symbol, StringComparison.OrdinalIgnoreCase)
           && string.Equals(
               retained.Account.FinancialAccountId,
               candidate.Account.FinancialAccountId,
               StringComparison.OrdinalIgnoreCase)
           && AmountsMatch(retained.Debit, candidate.Debit)
           && AmountsMatch(retained.Credit, candidate.Credit)
           && string.Equals(retained.Description, candidate.Description, StringComparison.Ordinal)
           && DimensionsEquivalent(retained.Dimensions, candidate.Dimensions);

    private static LedgerJournalEntryWrite NormalizeWrite(LedgerJournalEntryWrite write)
    {
        var metadata = write.Entry.Metadata;
        if (write.LedgerBookId is { } ledgerBookId && string.IsNullOrWhiteSpace(metadata.LedgerBook))
        {
            metadata = metadata with { LedgerBook = ledgerBookId.ToString("D") };
        }
        else if (write.LedgerBookId is { } requestedLedgerBookId &&
                 (!Guid.TryParse(metadata.LedgerBook, out var metadataLedgerBookId) ||
                  metadataLedgerBookId != requestedLedgerBookId))
        {
            throw new LedgerValidationException(
                $"Ledger write ledger book '{requestedLedgerBookId:D}' conflicts with journal metadata ledger book '{metadata.LedgerBook}'.");
        }

        var entry = ReferenceEquals(metadata, write.Entry.Metadata)
            ? write.Entry
            : new JournalEntry(
                write.Entry.JournalEntryId,
                write.Entry.Timestamp,
                write.Entry.Description,
                write.Entry.Lines,
                metadata);

        return write with
        {
            Entry = entry,
            AccountingPolicyId = RequireText(write.AccountingPolicyId, nameof(write.AccountingPolicyId)),
            AccountingPolicyVersion = RequireText(write.AccountingPolicyVersion, nameof(write.AccountingPolicyVersion)),
            RuleId = NormalizeOptional(write.RuleId),
            RuleVersion = NormalizeOptional(write.RuleVersion)
        };
    }

    private static bool MetadataEquivalent(JournalEntryMetadata retained, JournalEntryMetadata candidate)
    {
        retained = retained.Normalize();
        candidate = candidate.Normalize();

        return TextEquals(retained.ActivityType, candidate.ActivityType)
            && TextEquals(retained.Symbol, candidate.Symbol)
            && retained.SecurityId == candidate.SecurityId
            && retained.OrderId == candidate.OrderId
            && retained.FillId == candidate.FillId
            && TextEquals(retained.ProjectId, candidate.ProjectId)
            && TextEquals(retained.LedgerBook, candidate.LedgerBook)
            && retained.LedgerView == candidate.LedgerView
            && TextEquals(retained.ScenarioId, candidate.ScenarioId)
            && TextEquals(retained.StrategyId, candidate.StrategyId)
            && TextEquals(retained.FinancialAccountId, candidate.FinancialAccountId)
            && TextEquals(retained.CounterpartyAccountId, candidate.CounterpartyAccountId)
            && TextEquals(retained.Institution, candidate.Institution)
            && retained.EffectiveDate == candidate.EffectiveDate
            && TextEquals(retained.IdempotencyKey, candidate.IdempotencyKey)
            && TextEquals(retained.FundEventId, candidate.FundEventId)
            && TextEquals(retained.FundEventType, candidate.FundEventType)
            && TextEquals(retained.CapitalAccountId, candidate.CapitalAccountId)
            && TextEquals(retained.InvestorId, candidate.InvestorId)
            && TextEquals(retained.PaymentIntentId, candidate.PaymentIntentId)
            && TextEquals(retained.SettlementReference, candidate.SettlementReference)
            && TagsEquivalent(retained.Tags, candidate.Tags)
            && EvidenceEquivalent(retained.EvidenceReferences, candidate.EvidenceReferences);
    }

    private static bool TagsEquivalent(
        IReadOnlyDictionary<string, string>? retained,
        IReadOnlyDictionary<string, string>? candidate)
    {
        var ignoreLineDimensionCompatibilityTags =
            ContainsTag(retained, AccountingPostingCommandValidator.PostingCommandFingerprintTag) &&
            ContainsTag(candidate, AccountingPostingCommandValidator.PostingCommandFingerprintTag);
        var retainedPairs = (retained ?? new Dictionary<string, string>())
            .Where(pair => !ignoreLineDimensionCompatibilityTags || !IsLineDimensionCompatibilityTag(pair.Key))
            .ToArray();
        var candidatePairs = (candidate ?? new Dictionary<string, string>())
            .Where(pair => !ignoreLineDimensionCompatibilityTags || !IsLineDimensionCompatibilityTag(pair.Key))
            .ToArray();
        if (retainedPairs.Length != candidatePairs.Length)
            return false;

        foreach (var (key, retainedValue) in retainedPairs)
        {
            var candidatePair = candidatePairs.FirstOrDefault(pair =>
                string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase));
            if (candidatePair.Key is null ||
                !string.Equals(retainedValue, candidatePair.Value, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static bool ContainsTag(
        IReadOnlyDictionary<string, string>? tags,
        string key)
        => tags?.Keys.Any(candidate => string.Equals(candidate, key, StringComparison.OrdinalIgnoreCase)) == true;

    private static bool IsLineDimensionCompatibilityTag(string? key)
        => key?.StartsWith("lineDimensions.", StringComparison.OrdinalIgnoreCase) == true;

    private static bool EvidenceEquivalent(
        IReadOnlyList<JournalEvidenceReference> retained,
        IReadOnlyList<JournalEvidenceReference> candidate)
    {
        if (retained.Count != candidate.Count)
            return false;

        var retainedOrdered = retained
            .Select(static evidence => evidence.Normalize())
            .OrderBy(static evidence => evidence.EvidenceId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static evidence => evidence.Uri, StringComparer.Ordinal)
            .ThenBy(static evidence => evidence.Kind, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static evidence => evidence.SourceSystem, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static evidence => evidence.RetainedAtUtc)
            .ThenBy(static evidence => evidence.RetainedBy, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static evidence => evidence.SubjectId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static evidence => evidence.ContentHash, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static evidence => evidence.Description, StringComparer.Ordinal)
            .ToArray();
        var candidateOrdered = candidate
            .Select(static evidence => evidence.Normalize())
            .OrderBy(static evidence => evidence.EvidenceId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static evidence => evidence.Uri, StringComparer.Ordinal)
            .ThenBy(static evidence => evidence.Kind, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static evidence => evidence.SourceSystem, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static evidence => evidence.RetainedAtUtc)
            .ThenBy(static evidence => evidence.RetainedBy, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static evidence => evidence.SubjectId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static evidence => evidence.ContentHash, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static evidence => evidence.Description, StringComparer.Ordinal)
            .ToArray();
        return retainedOrdered.SequenceEqual(candidateOrdered);
    }

    private static bool DimensionsEquivalent(
        LedgerLineDimensionSet? retained,
        LedgerLineDimensionSet? candidate)
    {
        if (retained is null || candidate is null)
            return retained is null && candidate is null;

        return TextEquals(retained.FundId, candidate.FundId)
            && TextEquals(retained.EntityId, candidate.EntityId)
            && TextEquals(retained.SleeveId, candidate.SleeveId)
            && TextEquals(retained.StrategyId, candidate.StrategyId)
            && TextEquals(retained.InvestorId, candidate.InvestorId)
            && TextEquals(retained.CapitalAccountId, candidate.CapitalAccountId)
            && retained.InstrumentId == candidate.InstrumentId
            && retained.PositionId == candidate.PositionId
            && TextEquals(retained.TaxLotId, candidate.TaxLotId)
            && TextEquals(retained.CostCenterId, candidate.CostCenterId)
            && TextEquals(retained.CounterpartyId, candidate.CounterpartyId)
            && TextEquals(retained.OrganizationId, candidate.OrganizationId)
            && TextEquals(retained.PortfolioId, candidate.PortfolioId)
            && TextEquals(retained.BookId, candidate.BookId)
            && TextEquals(retained.AccountId, candidate.AccountId)
            && TextEquals(retained.CustomerId, candidate.CustomerId)
            && TextEquals(retained.VendorId, candidate.VendorId)
            && TextEquals(retained.ProjectId, candidate.ProjectId)
            // Was a first-match scan whose one-to-one pairing held only because the store
            // canonicalizes these keys on read. Nothing at this seam said so or checked it.
            && ExternalDimensionsMatch(retained.ExternalGlDimensions, candidate.ExternalGlDimensions);
    }

    private static bool TextEquals(string? retained, string? candidate)
        => string.Equals(retained, candidate, StringComparison.OrdinalIgnoreCase);

    private static string RequireText(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new LedgerValidationException($"{parameterName} is required for durable ledger posting.");

        return value.Trim();
    }
}

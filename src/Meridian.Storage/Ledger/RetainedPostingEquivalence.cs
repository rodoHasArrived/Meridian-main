using Meridian.Ledger;
using static Meridian.Contracts.Text.TextPrimitives;
using static Meridian.Storage.Ledger.LedgerRetainedValueComparison;

namespace Meridian.Storage.Ledger;

/// <summary>
/// Compares a retained journal against the posting a caller is asking to append, so a
/// posting-identity collision can be resolved as an exact replay rather than assumed to be one.
/// <para>
/// The comparison is deliberately restricted to canonical economic content — lineage, period,
/// policy, timing, idempotency, and the ordered lines. Generated identities are excluded because
/// they are not stable across two builds of the same request: each build mints a fresh
/// <see cref="JournalEntry.JournalEntryId"/> and fresh per-line entry ids, and the
/// <c>lineDimensions.&lt;entryId&gt;.*</c> compatibility tags are keyed by those ids. Comparing
/// them would report a conflict for a posting that is economically identical, which turns an
/// ordinary operator retry into a hard failure.
/// </para>
/// <para>
/// Approval-derived state is excluded for the same reason: approval id, approval state, the
/// fingerprint computed over the approved command, and evidence retention stamps are all recorded
/// at append time, so a later replay of the same posting legitimately carries different values.
/// That exclusion is deliberately narrow — everything else a tag carries, including the
/// real-versus-simulated provenance mark and the economic-event and projection lineage, is
/// compared. What must not differ is what the books say, and what the figures claim to be.
/// </para>
/// </summary>
public static class RetainedPostingEquivalence
{
    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="retained"/> is the journal
    /// <paramref name="candidate"/> would have produced. On <see langword="false"/>,
    /// <paramref name="difference"/> names the first field that disagreed.
    /// </summary>
    public static bool Matches(
        LedgerJournalEntryRecord retained,
        LedgerJournalEntryWrite candidate,
        out string difference)
    {
        ArgumentNullException.ThrowIfNull(retained);
        ArgumentNullException.ThrowIfNull(candidate);

        if (retained.AggregateId != candidate.AggregateId)
            return Differs("ledger aggregate", out difference);
        if (retained.PeriodId != candidate.PeriodId)
            return Differs("accounting period", out difference);
        if (retained.AccountingBasis != candidate.AccountingBasis)
            return Differs("accounting basis", out difference);
        if (!IdentityMatches(retained.AccountingPolicyId, candidate.AccountingPolicyId))
            return Differs("accounting policy", out difference);
        if (!IdentityMatches(retained.AccountingPolicyVersion, candidate.AccountingPolicyVersion))
            return Differs("accounting policy version", out difference);
        if (!IdentityMatches(retained.RuleId, candidate.RuleId))
            return Differs("posting rule", out difference);
        if (!IdentityMatches(retained.RuleVersion, candidate.RuleVersion))
            return Differs("posting rule version", out difference);
        if (retained.SourceEventId != candidate.SourceEventId)
            return Differs("source event", out difference);
        if (retained.SourceJournalEntryId != candidate.SourceJournalEntryId)
            return Differs("source journal lineage", out difference);
        if (retained.PostingKind != candidate.PostingKind)
            return Differs("posting kind", out difference);
        if (retained.CommandId != candidate.CommandId)
            return Differs("posting command identity", out difference);
        if (retained.CorrelationId != candidate.CorrelationId)
            return Differs("correlation identity", out difference);

        // Governance approval travels with the posting and is retained alongside it, so a retry
        // carrying a different approval id, approver, or reason code is a different submission
        // even when the amounts are unchanged. Structural: the metadata is a scalar-only record
        // persisted as jsonb, so it round-trips without losing precision.
        if (retained.AdjustmentApproval != candidate.AdjustmentApproval)
            return Differs("adjustment approval", out difference);

        var retainedEntry = retained.Entry;
        var candidateEntry = candidate.Entry;
        if (!TimestampsMatch(retainedEntry.Timestamp, candidateEntry.Timestamp))
            return Differs("accounting timestamp", out difference);
        if (!string.Equals(retainedEntry.Description, candidateEntry.Description, StringComparison.Ordinal))
            return Differs("journal description", out difference);

        if (MetadataDifference(retainedEntry.Metadata.Normalize(), candidateEntry.Metadata.Normalize())
            is { } metadataDifference)
        {
            return Differs(metadataDifference, out difference);
        }

        // Ordered, not set-based: two lines that swap sides are a different journal even though
        // the multiset of amounts is unchanged.
        if (retainedEntry.Lines.Count != candidateEntry.Lines.Count)
            return Differs("journal line count", out difference);
        for (var index = 0; index < retainedEntry.Lines.Count; index++)
        {
            var retainedLine = retainedEntry.Lines[index];
            var candidateLine = candidateEntry.Lines[index];
            // LedgerAccount defines its own identity and ledger balances are keyed by it, with
            // Name and Symbol compared ordinally. A line whose account differs only in casing
            // therefore lands in a different balance bucket, so the type's own equality is the
            // authority here rather than the trimmed case-insensitive helper used for free text.
            if (retainedLine.Account != candidateLine.Account)
                return Differs($"line {index} account", out difference);

            if (!AmountsMatch(retainedLine.Debit, candidateLine.Debit)
                || !AmountsMatch(retainedLine.Credit, candidateLine.Credit))
            {
                return Differs($"line {index} amount", out difference);
            }
            if (!string.Equals(retainedLine.Description, candidateLine.Description, StringComparison.Ordinal))
                return Differs($"line {index} description", out difference);
            if (!DimensionsMatch(retainedLine.Dimensions, candidateLine.Dimensions))
                return Differs($"line {index} dimensions", out difference);
            // Transaction currency, functional currency, both transaction-side amounts, and the
            // FX rate are durable per leg. Two legs can agree on functional debits and credits
            // while booking a different transaction currency or rate, which is a different
            // posting. The amounts are known equal by this point, so either side's serve as the
            // functional pair the shared comparison needs.
            if (!CurrencyMatches(
                    retainedLine.Currency,
                    candidateLine.Currency,
                    retainedLine.Debit,
                    retainedLine.Credit))
                return Differs($"line {index} currency", out difference);
        }

        difference = string.Empty;
        return true;
    }

    /// <summary>
    /// Names the first durable metadata field that disagrees, or <see langword="null"/> when every
    /// one matches. Every field on <see cref="JournalEntryMetadata"/> that describes what the
    /// posting <i>is</i> — its scope, provenance, lineage, and settlement context — participates,
    /// because a retry that keeps the same lines while changing the fund, investor, capital
    /// account, or payment intent it books against is a different posting, not a replay.
    /// <para>
    /// Tags participate too, minus a narrow exclusion set. They are not decoration: typed
    /// provenance — the data-provenance mark separating real figures from simulated ones, the
    /// economic event's version, domain, entity, and content hash, the projection lineage, and
    /// the rule pack — is persisted <i>only</i> as tags and is mirrored by none of the scalars
    /// above. Excluding them wholesale would let a retry that changes what the figures claim to
    /// be pass as a replay.
    /// </para>
    /// <para>
    /// <see cref="JournalEntryMetadata.EvidenceReferences"/> remains excluded: approval evidence
    /// is merged with a clock stamp as the posting is approved, so a rebuild carries neither the
    /// stamp nor the merged entries and comparing the list would reject ordinary retries.
    /// </para>
    /// </summary>
    private static string? MetadataDifference(JournalEntryMetadata retained, JournalEntryMetadata candidate)
    {
        if (!TextMatches(retained.ActivityType, candidate.ActivityType))
            return "activity type";
        if (!TextMatches(retained.Symbol, candidate.Symbol))
            return "symbol";
        if (retained.SecurityId != candidate.SecurityId)
            return "security";
        if (retained.OrderId != candidate.OrderId)
            return "order";
        if (retained.FillId != candidate.FillId)
            return "fill";
        if (!TextMatches(retained.ProjectId, candidate.ProjectId))
            return "project";
        if (!TextMatches(retained.LedgerBook, candidate.LedgerBook))
            return "ledger book";
        if (retained.LedgerView != candidate.LedgerView)
            return "ledger view";
        if (!TextMatches(retained.ScenarioId, candidate.ScenarioId))
            return "scenario";
        if (!TextMatches(retained.StrategyId, candidate.StrategyId))
            return "strategy";
        if (!TextMatches(retained.FinancialAccountId, candidate.FinancialAccountId))
            return "financial account";
        if (!TextMatches(retained.CounterpartyAccountId, candidate.CounterpartyAccountId))
            return "counterparty account";
        if (!TextMatches(retained.Institution, candidate.Institution))
            return "institution";
        if (retained.EffectiveDate != candidate.EffectiveDate)
            return "effective date";
        if (!TextMatches(retained.IdempotencyKey, candidate.IdempotencyKey))
            return "idempotency key";
        if (!TextMatches(retained.FundEventId, candidate.FundEventId))
            return "fund event";
        if (!TextMatches(retained.FundEventType, candidate.FundEventType))
            return "fund event type";
        if (!TextMatches(retained.CapitalAccountId, candidate.CapitalAccountId))
            return "capital account";
        if (!TextMatches(retained.InvestorId, candidate.InvestorId))
            return "investor";
        if (!TextMatches(retained.PaymentIntentId, candidate.PaymentIntentId))
            return "payment intent";
        if (!TextMatches(retained.SettlementReference, candidate.SettlementReference))
            return "settlement reference";
        return TagsDifference(retained.Tags, candidate.Tags);
    }

    /// <summary>
    /// Tag keys a rebuild cannot reproduce, and the only ones excluded from comparison. The
    /// approval state and id are stamped as the posting is approved; the fingerprint is computed
    /// over the whole approved command including its clock-stamped evidence, so it can never
    /// match across a rebuild. Line-dimension tags are keyed by generated per-line entry ids.
    /// </summary>
    private static bool IsRebuildableTag(string key)
        => !key.StartsWith("lineDimensions.", StringComparison.OrdinalIgnoreCase)
           && !string.Equals(key, "approvalState", StringComparison.OrdinalIgnoreCase)
           && !string.Equals(key, "approvalId", StringComparison.OrdinalIgnoreCase)
           && !string.Equals(
               key,
               AccountingPostingCommandValidator.PostingCommandFingerprintTag,
               StringComparison.OrdinalIgnoreCase);

    private static string? TagsDifference(
        IReadOnlyDictionary<string, string>? retained,
        IReadOnlyDictionary<string, string>? candidate)
    {
        var retainedTags = ComparableTags(retained);
        var candidateTags = ComparableTags(candidate);

        foreach (var (key, retainedValue) in retainedTags)
        {
            if (!candidateTags.TryGetValue(key, out var candidateValue) || !TextMatches(retainedValue, candidateValue))
                return $"tag '{key}'";
        }

        foreach (var key in candidateTags.Keys)
        {
            if (!retainedTags.ContainsKey(key))
                return $"tag '{key}'";
        }

        return null;
    }

    private static Dictionary<string, string> ComparableTags(IReadOnlyDictionary<string, string>? tags)
    {
        var comparable = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (tags is null)
            return comparable;

        foreach (var (key, value) in tags)
        {
            if (NormalizeOptional(key) is not { } normalizedKey || !IsRebuildableTag(normalizedKey))
                continue;
            comparable[normalizedKey] = NormalizeOptional(value) ?? string.Empty;
        }

        return comparable;
    }

    /// <summary>
    /// Structural, not record, equality. <see cref="LedgerLineDimensionSet"/> carries a
    /// dictionary, so its compiler-generated equality falls back to reference equality for that
    /// member: two structurally identical dimension sets built from separate requests would
    /// otherwise compare unequal and report a conflict for an exact replay.
    /// </summary>
    private static bool DimensionsMatch(LedgerLineDimensionSet? retained, LedgerLineDimensionSet? candidate)
    {
        if (retained is null || candidate is null)
            return retained is null && candidate is null;

        return TextMatches(retained.FundId, candidate.FundId)
            && TextMatches(retained.EntityId, candidate.EntityId)
            && TextMatches(retained.SleeveId, candidate.SleeveId)
            && TextMatches(retained.StrategyId, candidate.StrategyId)
            && TextMatches(retained.InvestorId, candidate.InvestorId)
            && TextMatches(retained.CapitalAccountId, candidate.CapitalAccountId)
            && retained.InstrumentId == candidate.InstrumentId
            && retained.PositionId == candidate.PositionId
            && TextMatches(retained.TaxLotId, candidate.TaxLotId)
            && TextMatches(retained.CostCenterId, candidate.CostCenterId)
            && TextMatches(retained.CounterpartyId, candidate.CounterpartyId)
            && TextMatches(retained.OrganizationId, candidate.OrganizationId)
            && TextMatches(retained.PortfolioId, candidate.PortfolioId)
            && TextMatches(retained.BookId, candidate.BookId)
            && TextMatches(retained.AccountId, candidate.AccountId)
            && TextMatches(retained.CustomerId, candidate.CustomerId)
            && TextMatches(retained.VendorId, candidate.VendorId)
            && TextMatches(retained.ProjectId, candidate.ProjectId)
            && ExternalDimensionsMatch(retained.ExternalGlDimensions, candidate.ExternalGlDimensions);
    }

    private static bool Differs(string field, out string difference)
    {
        difference = field;
        return false;
    }

    /// <summary>
    /// Ordinal comparison for durable lineage identifiers, after the same null/whitespace
    /// normalization the store applies on write. Policy, policy version, rule, and rule version
    /// are retained verbatim and <see cref="DurableLedgerPostingTarget"/> already resolves
    /// collisions on them ordinally, so matching them case-insensitively here would apply looser
    /// collision semantics on this path than the governed target applies on its own.
    /// </summary>
    private static bool IdentityMatches(string? retained, string? candidate)
        => string.Equals(NormalizeOptional(retained), NormalizeOptional(candidate), StringComparison.Ordinal);

    private static bool TextMatches(string? retained, string? candidate)
        => string.Equals(
            NormalizeOptional(retained),
            NormalizeOptional(candidate),
            StringComparison.OrdinalIgnoreCase);
}

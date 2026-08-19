using Meridian.Ledger;

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
/// Approval-derived state is excluded for the same reason: approval id, approval state, and
/// evidence retention stamps are recorded at append time, so a later replay of the same posting
/// legitimately carries different values. What must not differ is what the books say.
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
        if (!TextMatches(retained.AccountingPolicyId, candidate.AccountingPolicyId))
            return Differs("accounting policy", out difference);
        if (!TextMatches(retained.AccountingPolicyVersion, candidate.AccountingPolicyVersion))
            return Differs("accounting policy version", out difference);
        if (!TextMatches(retained.RuleId, candidate.RuleId))
            return Differs("posting rule", out difference);
        if (!TextMatches(retained.RuleVersion, candidate.RuleVersion))
            return Differs("posting rule version", out difference);
        if (retained.SourceEventId != candidate.SourceEventId)
            return Differs("source event", out difference);
        if (retained.SourceJournalEntryId != candidate.SourceJournalEntryId)
            return Differs("source journal lineage", out difference);
        if (retained.PostingKind != candidate.PostingKind)
            return Differs("posting kind", out difference);
        if (retained.CommandId != candidate.CommandId)
            return Differs("posting command identity", out difference);

        var retainedEntry = retained.Entry;
        var candidateEntry = candidate.Entry;
        if (retainedEntry.Timestamp != candidateEntry.Timestamp)
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
            if (retainedLine.Account.AccountType != candidateLine.Account.AccountType
                || !TextMatches(retainedLine.Account.Name, candidateLine.Account.Name)
                || !TextMatches(retainedLine.Account.Symbol, candidateLine.Account.Symbol)
                || !TextMatches(retainedLine.Account.FinancialAccountId, candidateLine.Account.FinancialAccountId))
            {
                return Differs($"line {index} account", out difference);
            }

            if (retainedLine.Debit != candidateLine.Debit || retainedLine.Credit != candidateLine.Credit)
                return Differs($"line {index} amount", out difference);
            if (!string.Equals(retainedLine.Description, candidateLine.Description, StringComparison.Ordinal))
                return Differs($"line {index} description", out difference);
            if (!DimensionsMatch(retainedLine.Dimensions, candidateLine.Dimensions))
                return Differs($"line {index} dimensions", out difference);
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
    /// <see cref="JournalEntryMetadata.Tags"/> and
    /// <see cref="JournalEntryMetadata.EvidenceReferences"/> are the two deliberate exclusions.
    /// Both carry approval-time state that a rebuild cannot reproduce: the tag set records the
    /// approval state, approval id, and a fingerprint computed over the approved command, and the
    /// evidence list is merged with a clock stamp as the posting is approved. Comparing them
    /// would reject ordinary retries. Their durable content is largely mirrored by the scalar
    /// fields above, which are compared.
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
        return TextMatches(retained.SettlementReference, candidate.SettlementReference)
            ? null
            : "settlement reference";
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

    private static bool ExternalDimensionsMatch(
        IReadOnlyDictionary<string, string> retained,
        IReadOnlyDictionary<string, string> candidate)
    {
        if (retained.Count != candidate.Count)
            return false;

        foreach (var (key, retainedValue) in retained)
        {
            var match = candidate.FirstOrDefault(pair =>
                string.Equals(pair.Key?.Trim(), key?.Trim(), StringComparison.OrdinalIgnoreCase));
            if (match.Key is null || !TextMatches(retainedValue, match.Value))
                return false;
        }

        return true;
    }

    private static bool Differs(string field, out string difference)
    {
        difference = field;
        return false;
    }

    private static bool TextMatches(string? retained, string? candidate)
        => string.Equals(
            string.IsNullOrWhiteSpace(retained) ? null : retained.Trim(),
            string.IsNullOrWhiteSpace(candidate) ? null : candidate.Trim(),
            StringComparison.OrdinalIgnoreCase);
}

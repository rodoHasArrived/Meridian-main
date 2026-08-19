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

        var retainedMetadata = retainedEntry.Metadata.Normalize();
        var candidateMetadata = candidateEntry.Metadata.Normalize();
        if (retainedMetadata.EffectiveDate != candidateMetadata.EffectiveDate)
            return Differs("effective date", out difference);
        if (!TextMatches(retainedMetadata.IdempotencyKey, candidateMetadata.IdempotencyKey))
            return Differs("idempotency key", out difference);
        if (!TextMatches(retainedMetadata.ActivityType, candidateMetadata.ActivityType))
            return Differs("activity type", out difference);
        if (!TextMatches(retainedMetadata.LedgerBook, candidateMetadata.LedgerBook))
            return Differs("ledger book", out difference);
        if (!TextMatches(retainedMetadata.FinancialAccountId, candidateMetadata.FinancialAccountId))
            return Differs("financial account", out difference);
        if (!TextMatches(retainedMetadata.CounterpartyAccountId, candidateMetadata.CounterpartyAccountId))
            return Differs("counterparty account", out difference);
        if (!TextMatches(retainedMetadata.FundEventId, candidateMetadata.FundEventId))
            return Differs("fund event", out difference);
        if (!TextMatches(retainedMetadata.SettlementReference, candidateMetadata.SettlementReference))
            return Differs("settlement reference", out difference);

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

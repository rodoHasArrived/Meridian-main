using System.Globalization;

namespace Meridian.Ledger;

/// <summary>
/// Converts a <see cref="DailyPortfolioPricingProjection"/> into a governed
/// <see cref="AutomatedJournalDraft"/> so daily fair-value marks flow through
/// <see cref="AutomatedJournalApproval"/> before posting to the books.
/// </summary>
public static class DailyPortfolioPricingDraftBuilder
{
    /// <summary>
    /// Builds a balanced fair-value adjustment draft from the projection.
    /// Returns null when the marks produced no unrealized movement to post.
    /// </summary>
    public static AutomatedJournalDraft? BuildDraft(DailyPortfolioPricingProjection projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        if (projection.JournalLines.Count == 0)
            return null;
        if (!projection.IsBalanced)
            throw new InvalidOperationException("Daily portfolio pricing projection produced unbalanced journal lines.");

        var input = projection.Input;
        var effectiveDate = DateOnly.FromDateTime(input.AsOf.UtcDateTime);
        var idempotencyKey = FormattableString.Invariant(
            $"fair-value|{input.Policy.FundId}|{input.PeriodId}|{effectiveDate:yyyy-MM-dd}");

        var evidence = projection.Lines
            .Select(line => new JournalEvidenceReference(
                EvidenceId: FormattableString.Invariant($"fair-value-mark:{line.Symbol}:{input.PeriodId}"),
                Uri: line.EvidenceReference,
                Kind: "price-mark",
                SourceSystem: line.PriceSource,
                RetainedAtUtc: input.AsOf,
                RetainedBy: input.Policy.ApprovedBy,
                SubjectId: line.Symbol,
                Description: FormattableString.Invariant(
                    $"{line.Symbol} marked at {line.MarkPrice} via {line.PriceSource}")).Normalize())
            .ToArray();

        var description = FormattableString.Invariant(
            $"Daily fair-value marks for fund {input.Policy.FundId}, period {input.PeriodId} as of {effectiveDate:yyyy-MM-dd}");

        var journalEvent = new AutomatedJournalEvent(
            AutomatedJournalEventKind.FairValueMarkAdjustment,
            input.Policy.FundId,
            projection.NetUnrealizedGainOrLoss,
            input.AsOf,
            Description: description,
            EffectiveDate: effectiveDate,
            IdempotencyKey: idempotencyKey,
            EvidenceReferences: evidence);

        var tags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["valuation.policyId"] = input.Policy.PolicyId,
            ["valuation.method"] = input.Policy.ValuationMethod,
            ["valuation.periodId"] = input.PeriodId,
            ["valuation.baseCurrency"] = input.BaseCurrency,
            ["valuation.totalMarketValue"] = projection.TotalMarketValue.ToString(CultureInfo.InvariantCulture),
            ["valuation.netUnrealizedGainOrLoss"] = projection.NetUnrealizedGainOrLoss.ToString(CultureInfo.InvariantCulture)
        };

        var metadata = new JournalEntryMetadata(
            ActivityType: "fair-value-mark",
            EffectiveDate: effectiveDate,
            IdempotencyKey: idempotencyKey,
            Tags: tags,
            EvidenceReferences: evidence);

        return new AutomatedJournalDraft(journalEvent, description, projection.JournalLines, metadata);
    }
}

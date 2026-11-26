using System.Globalization;
using System.Text.Json;
using Meridian.Contracts.Integrity;

namespace Meridian.Ledger;

/// <summary>
/// Converts a <see cref="PartnershipWaterfallAllocationProjection"/> into a governed
/// <see cref="AutomatedJournalDraft"/> (kind <see cref="AutomatedJournalEventKind.PartnershipProfitAllocated"/>),
/// so tiered profit allocations reach partners' capital only through workbench review, approval and posting.
/// The journal debits retained earnings and credits each investor's capital account; the partners' capital
/// statement reports those credits as each investor's allocated result.
/// </summary>
public static class PartnershipWaterfallDraftBuilder
{
    public const string ActivityType = "partnership-waterfall-allocation";
    public const string IdempotencyPrefix = "partnership-waterfall|";

    public static AutomatedJournalDraft BuildDraft(
        PartnershipWaterfallAllocationProjection projection,
        DateOnly effectiveDate,
        DateTimeOffset preparedAtUtc,
        string preparedBy)
    {
        ArgumentNullException.ThrowIfNull(projection);
        ArgumentException.ThrowIfNullOrWhiteSpace(preparedBy);
        if (projection.Lines.Count == 0 || projection.AllocatedProfit <= 0m)
            throw new ArgumentException("The waterfall allocated no profit; nothing is drafted.", nameof(projection));
        if (!projection.IsBalanced)
            throw new ArgumentException("The waterfall projection produced unbalanced journal lines.", nameof(projection));

        var input = projection.Input;
        var fingerprint = Fingerprint(input);
        var idempotencyKey = string.Create(CultureInfo.InvariantCulture,
            $"{IdempotencyPrefix}{input.FundId}|{input.PeriodId}|{fingerprint}");
        var retainedEarnings = projection.Lines[0].account;

        var journalEvent = new AutomatedJournalEvent(
            AutomatedJournalEventKind.PartnershipProfitAllocated,
            input.FundId,
            projection.AllocatedProfit,
            preparedAtUtc,
            FinancialAccountId: retainedEarnings.FinancialAccountId,
            Description: projection.Description,
            SourceEventId: input.PeriodId,
            EffectiveDate: effectiveDate,
            IdempotencyKey: idempotencyKey);

        var tags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["waterfall.fundId"] = input.FundId,
            ["waterfall.periodId"] = input.PeriodId,
            ["waterfall.distributableProfit"] = input.DistributableProfit.ToString(CultureInfo.InvariantCulture),
            ["waterfall.allocatedProfit"] = projection.AllocatedProfit.ToString(CultureInfo.InvariantCulture),
            ["waterfall.unallocatedProfit"] = projection.UnallocatedProfit.ToString(CultureInfo.InvariantCulture),
            ["waterfall.tierCount"] = input.Tiers.Count.ToString(CultureInfo.InvariantCulture),
            ["waterfall.investorCount"] = projection.InvestorAllocations.Count.ToString(CultureInfo.InvariantCulture),
            ["waterfall.retainedEarningsAccount"] = retainedEarnings.FinancialAccountId is { } scope
                ? $"{retainedEarnings.Name}|{scope}"
                : retainedEarnings.Name,
            ["waterfall.inputFingerprint"] = fingerprint,
            ["waterfall.preparedBy"] = preparedBy.Trim()
        };

        var metadata = new JournalEntryMetadata(
            ActivityType: ActivityType,
            EffectiveDate: effectiveDate,
            IdempotencyKey: idempotencyKey,
            Tags: tags);

        return new AutomatedJournalDraft(
            journalEvent,
            projection.Description,
            projection.Lines.Select(static line => (line.account, line.debit, line.credit, (LedgerLineDimensionSet?)null)).ToArray(),
            metadata);
    }

    /// <summary>Digest of the waterfall inputs, so a changed term or amount is a different allocation identity.</summary>
    public static string Fingerprint(PartnershipWaterfallAllocationInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return Sha256Digest.Compute(JsonSerializer.SerializeToUtf8Bytes(input))[..16];
    }
}

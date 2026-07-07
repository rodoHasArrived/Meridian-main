using System.Globalization;

namespace Meridian.Ledger;

/// <summary>
/// Converts a <see cref="PeriodCloseProjection"/> into a governed
/// <see cref="AutomatedJournalDraft"/> so closing entries flow through
/// <see cref="AutomatedJournalApproval"/> before posting.
/// </summary>
public static class PeriodCloseDraftBuilder
{
    /// <summary>
    /// Builds a balanced closing-entry draft. Returns null when the period had no
    /// temporary-account balances to close.
    /// </summary>
    public static AutomatedJournalDraft? BuildDraft(PeriodCloseProjection projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        if (!projection.HasClosingEntries)
            return null;
        if (!projection.IsBalanced)
            throw new InvalidOperationException("Period-close projection produced unbalanced journal lines.");

        var input = projection.Input;
        var effectiveDate = DateOnly.FromDateTime(input.ClosedAtUtc.UtcDateTime);
        var idempotencyKey = FormattableString.Invariant($"period-close|{input.PeriodId}");
        var description = FormattableString.Invariant(
            $"Period-close closing entries for {input.PeriodId}: net income {projection.NetIncome} to retained earnings");

        var journalEvent = new AutomatedJournalEvent(
            AutomatedJournalEventKind.PeriodCloseClosingEntries,
            input.PeriodId,
            Math.Abs(projection.NetIncome),
            input.ClosedAtUtc,
            Description: description,
            EffectiveDate: effectiveDate,
            IdempotencyKey: idempotencyKey);

        var tags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["close.periodId"] = input.PeriodId,
            ["close.closedBy"] = input.ClosedBy,
            ["close.netIncome"] = projection.NetIncome.ToString(CultureInfo.InvariantCulture),
            ["close.closedAccountCount"] = projection.Lines.Count.ToString(CultureInfo.InvariantCulture)
        };

        var metadata = new JournalEntryMetadata(
            ActivityType: "period-close",
            EffectiveDate: effectiveDate,
            IdempotencyKey: idempotencyKey,
            Tags: tags);

        return new AutomatedJournalDraft(journalEvent, description, projection.JournalLines, metadata);
    }
}

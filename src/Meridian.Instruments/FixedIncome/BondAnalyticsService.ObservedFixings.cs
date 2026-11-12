using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.SecurityMaster.Schedules;

namespace Meridian.Instruments.FixedIncome;

public sealed partial class BondAnalyticsService
{
    private async Task<(IReadOnlyDictionary<DateOnly, BondObservedFixingReceipt> Receipts, BondAnalyticsBlocker? Blocker)> ResolveObservedFixingsAsync(
        SecurityDetailDto security, StructuredCashFlowLeg leg, ScheduleGenerationResult schedule, DateOnly settlement,
        DateOnly economicDate, BondCalculationContext context, List<BondCalculationInput> inputs, CancellationToken ct)
    {
        var selected = new Dictionary<DateOnly, BondObservedFixingReceipt>();
        if (context.ObservedFixingReceiptIds is not { Count: > 0 } ids) return (selected, null);
        if (ids.Count > 64 || ids.Any(static id => id == Guid.Empty) || ids.Distinct().Count() != ids.Count)
            return (selected, new("InvalidFixingSelection", "Select at most 64 distinct retained observed fixing receipts."));
        var eligibleResets = schedule.Periods.Where(period => period.PaymentDate > settlement && period.AccrualStart <= economicDate)
            .Select(static period => period.AccrualStart).ToHashSet();
        var knownAt = context.KnownAt ?? DateTimeOffset.UtcNow;
        foreach (var id in ids)
        {
            BondObservedFixingReceipt? receipt;
            try { receipt = _observedFixings is null ? null : await _observedFixings.GetAsync(security.SecurityId, id, ct).ConfigureAwait(false); }
            catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or System.Text.Json.JsonException)
            { return (selected, new("FixingReceiptUnavailable", "Selected observed fixing evidence is unavailable or corrupt; review its retained receipt.")); }
            if (receipt is null || receipt.ReceiptId != id || receipt.Fixing.SecurityId != security.SecurityId)
                return (selected, new("FixingReceiptUnavailable", "Select a retained observed fixing receipt in this security's scope."));
            var fixing = receipt.Fixing;
            var observation = fixing.Observation;
            if (fixing.SecurityVersion != security.Version || !string.Equals(fixing.IndexName, leg.IndexName?.Trim(), StringComparison.OrdinalIgnoreCase)
                || !string.Equals(observation.SeriesId, fixing.IndexName, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(observation.Currency, security.Currency, StringComparison.OrdinalIgnoreCase))
                return (selected, new("FixingReceiptContextMismatch", "The fixing's security version, index or currency differs from this calculation. Review and retain evidence against the current terms."));
            if (receipt.RecordedAt > knownAt || observation.RetrievedAt > knownAt || observation.PublishedAt > knownAt
                || observation.VintageDate > DateOnly.FromDateTime(knownAt.UtcDateTime)
                || observation.Kind != SecurityRateObservationKind.ObservedFixing || observation.ObservationDate != fixing.ResetDate
                || !eligibleResets.Contains(fixing.ResetDate))
                return (selected, new("FixingReceiptCutoffMismatch", "The observed fixing is not eligible at these economic and recorded cutoffs, or does not address a remaining already-reset coupon."));
            var declaredFixing = leg.ResetSchedule?.SingleOrDefault(row => row.ResetDate == fixing.ResetDate)?.FixingRate;
            var scalarFixing = !leg.HasDeclaredResetSchedule && leg.CurrentIndexResetDate == fixing.ResetDate
                && !string.IsNullOrWhiteSpace(leg.CurrentIndexRateSource) ? leg.CurrentIndexRate : null;
            if (declaredFixing is not null || scalarFixing is not null || !selected.TryAdd(fixing.ResetDate, receipt))
                return (selected, new("ObservedFixingSourceConflict", "More than one source is selected for this reset, or the retained terms already supply it. Resolve the source choice explicitly."));
            inputs.Add(new("ObservedFixingReceipt", id.ToString("D"), fixing.SecurityVersion, receipt.ContentSha256,
                VersionId: id, AsOf: new DateTimeOffset(observation.ObservationDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero), RecordedAt: receipt.RecordedAt));
        }
        return (selected, null);
    }
}

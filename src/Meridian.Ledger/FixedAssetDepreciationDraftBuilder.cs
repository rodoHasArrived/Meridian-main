using System.Globalization;

namespace Meridian.Ledger;

/// <summary>
/// Batches per-asset depreciation projections for a period into a single governed
/// <see cref="AutomatedJournalDraft"/> (kind <see cref="AutomatedJournalEventKind.DepreciationPosted"/>)
/// so an accountant approves the whole period's depreciation run in one action — mirroring how
/// <see cref="DailyPortfolioPricingDraftBuilder"/> batches daily fair-value marks.
/// </summary>
public static class FixedAssetDepreciationDraftBuilder
{
    /// <summary>
    /// Builds one balanced depreciation draft covering every asset in <paramref name="assets"/> whose
    /// charge is positive. Returns null when no asset has a charge to post.
    /// </summary>
    /// <param name="periodEnd">The accounting period the depreciation run belongs to.</param>
    /// <param name="asOf">Timestamp used for the journal event and retained evidence.</param>
    /// <param name="assets">Per-asset depreciation inputs for the period.</param>
    /// <param name="bookLabel">Optional fund/book identifier used for scoping, idempotency, and metadata.</param>
    /// <param name="description">Optional description override for the batched draft.</param>
    public static AutomatedJournalDraft? BuildDraft(
        DateOnly periodEnd,
        DateTimeOffset asOf,
        IReadOnlyList<DepreciationInput> assets,
        string? bookLabel = null,
        string? description = null)
    {
        ArgumentNullException.ThrowIfNull(assets);

        var projected = assets
            .Where(static asset => asset.DepreciationAmount > 0m)
            .Select(FixedAssetDepreciationProjector.Project)
            .ToArray();
        if (projected.Length == 0)
            return null;

        var lines = projected
            .SelectMany(static projection => projection.Lines)
            .Select(static line => (line.account, line.debit, line.credit, (LedgerLineDimensionSet?)null))
            .ToArray();

        var totalDepreciation = projected.Sum(static projection => projection.TotalDebits);
        var scope = string.IsNullOrWhiteSpace(bookLabel) ? "fixed-assets" : bookLabel.Trim();
        var idempotencyKey = FormattableString.Invariant($"depreciation|{scope}|{periodEnd:yyyy-MM-dd}");
        var resolvedDescription = string.IsNullOrWhiteSpace(description)
            ? FormattableString.Invariant($"Fixed-asset depreciation for {scope} through {periodEnd:yyyy-MM-dd}")
            : description.Trim();

        var evidence = projected
            .Select(projection => new JournalEvidenceReference(
                EvidenceId: FormattableString.Invariant($"depreciation:{projection.AssetId:D}:{periodEnd:yyyy-MM-dd}"),
                Uri: FormattableString.Invariant($"fixed-asset://{projection.AssetId:D}/depreciation/{periodEnd:yyyy-MM-dd}"),
                Kind: "depreciation",
                SourceSystem: "meridian.fixed-assets",
                RetainedAtUtc: asOf,
                RetainedBy: scope,
                SubjectId: projection.AssetId.ToString("D"),
                Description: projection.Description).Normalize())
            .ToArray();

        var journalEvent = new AutomatedJournalEvent(
            AutomatedJournalEventKind.DepreciationPosted,
            scope,
            totalDepreciation,
            asOf,
            Description: resolvedDescription,
            EffectiveDate: periodEnd,
            IdempotencyKey: idempotencyKey,
            EvidenceReferences: evidence);

        var tags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["depreciation.book"] = scope,
            ["depreciation.periodEnd"] = periodEnd.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["depreciation.assetCount"] = projected.Length.ToString(CultureInfo.InvariantCulture),
            ["depreciation.totalAmount"] = totalDepreciation.ToString(CultureInfo.InvariantCulture),
        };

        var metadata = new JournalEntryMetadata(
            ActivityType: "depreciation",
            EffectiveDate: periodEnd,
            IdempotencyKey: idempotencyKey,
            Tags: tags,
            EvidenceReferences: evidence);

        return new AutomatedJournalDraft(journalEvent, resolvedDescription, lines, metadata);
    }
}

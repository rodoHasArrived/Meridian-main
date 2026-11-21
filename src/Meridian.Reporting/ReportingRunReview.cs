using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Contracts.Integrity;

namespace Meridian.Reporting;

public sealed record ReportingReviewChange(string Key, string? Previous, string? Current);
public sealed record ReportingReviewLine(
    string GridId, string RowKey, string Column, string State,
    string? Previous, string? Current, string? Delta, string? Percent,
    IReadOnlyList<string> EvidenceIds)
{
    public bool IsMeasure { get; init; }
    public string MaterialityOutcome { get; init; } = "NotAssessed";
}

/// <summary>Server-owned review basis. A missing materiality policy is never a passing assessment.</summary>
public sealed record ReportingRunReview(
    string ReviewId, string RunId, long Version, string SnapshotHash,
    string PeriodId, string? Currency, string? AccountingBasis,
    string? BaselineRunId, long? BaselineVersion,
    IReadOnlyList<ReportingReviewChange> Parameters,
    IReadOnlyList<ReportingReviewChange> SourceChanges,
    IReadOnlyList<ReportingReviewLine> Lines,
    string MaterialityOutcome, int? MaterialChangeCount,
    IReadOnlyList<string> UnavailableReasons)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ReportingReviewMaterialityPolicy? MaterialityPolicy { get; init; }
}

public sealed record RetainedReportingRunReview(
    string Format, ReportingRunReview Review,
    ReportingOutputManifest CurrentManifest, ReportingOutputManifest? BaselineManifest);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ReportingRunReview))]
[JsonSerializable(typeof(RetainedReportingRunReview))]
[JsonSerializable(typeof(IReadOnlyList<ReportingReviewMaterialityPolicy>), TypeInfoPropertyName = "MaterialityPolicies")]
public sealed partial class ReportingRunReviewJsonContext : JsonSerializerContext;

/// <summary>Deterministic review of retained inputs; never rereads current accounting sources.</summary>
public static class ReportingRunReviewEngine
{
    public const string Format = "report-run-review/v1";

    public static RetainedReportingRunReview Build(
        GovernedReportingRun current, ReportingOutputManifest currentManifest,
        GovernedReportingRun? baseline, ReportingOutputManifest? baselineManifest,
        ReportingReviewMaterialityPolicy? policy = null)
    {
        if (policy is not null && (policy.Scope != current.Scope || policy.TemplateId != current.TemplateId
            || policy.TemplateVersion != current.TemplateVersion || policy.Currency != currentManifest.ResolvedParameters?.PresentationCurrency))
            throw new ArgumentException("Materiality policy does not bind this exact run scope, template revision and currency.");
        var unavailable = new List<string>();
        var completeLineComparison = baseline is not null && baselineManifest is not null;
        var parameters = new List<ReportingReviewChange>();
        var sources = new List<ReportingReviewChange>();
        var lines = new List<ReportingReviewLine>();
        if (baseline is null || baselineManifest is null)
            unavailable.Add("No prior approved comparable run is retained in this series.");
        else
        {
            if (!Comparable(current, currentManifest, baseline, baselineManifest))
                throw new ArgumentException("The baseline is not a prior approved comparable revision.");
            parameters.AddRange(Diff(
                ParameterValues(baseline, baselineManifest), ParameterValues(current, currentManifest)));
            if (baselineManifest.CertifiedDatasetRows.IsDefault || currentManifest.CertifiedDatasetRows.IsDefault)
                unavailable.Add("Dataset revisions unavailable: both runs require retained certified source rows.");
            else
            {
                var before = SourceRows(baselineManifest);
                var after = SourceRows(currentManifest);
                if (before is null || after is null)
                    unavailable.Add("Dataset revisions unavailable: certified source rows have missing or duplicate entry identities.");
                else sources.AddRange(Diff(before, after));
            }
            var priorGrids = baselineManifest.RenderedReportWriterGrids.IsDefault ? [] : baselineManifest.RenderedReportWriterGrids;
            var nextGrids = currentManifest.RenderedReportWriterGrids.IsDefault ? [] : currentManifest.RenderedReportWriterGrids;
            if (priorGrids.IsEmpty || nextGrids.IsEmpty)
            {
                completeLineComparison = false;
                unavailable.Add("Line amounts unavailable: both runs require retained report-writer grids.");
            }
            var duplicateGrid = priorGrids.GroupBy(g => g.GridId).Any(g => g.Count() > 1)
                || nextGrids.GroupBy(g => g.GridId).Any(g => g.Count() > 1);
            if (duplicateGrid) { completeLineComparison = false; unavailable.Add("Line amounts unavailable: retained grid identities are duplicated."); }
            else
            {
                foreach (var next in nextGrids)
                {
                    var prior = priorGrids.SingleOrDefault(g => g.GridId == next.GridId);
                    if (prior is null) { completeLineComparison = false; unavailable.Add($"Grid '{next.GridId}' has no prior retained grid."); continue; }
                    Meridian.Contracts.Workstation.ReportWriterGridDiffDto diff;
                    try { diff = ReportSnapshotDiffEngine.Diff(prior, next); }
                    catch (OverflowException)
                    {
                        completeLineComparison = false;
                        unavailable.Add($"Grid '{next.GridId}' variance exceeds supported decimal range; its line comparison is unavailable.");
                        continue;
                    }
                    if (diff.Warnings.Count > 0)
                    {
                        completeLineComparison = false;
                        unavailable.AddRange(diff.Warnings.Select(w => $"Grid '{next.GridId}': {w}"));
                        // Ambiguous identities must not yield apparently reliable line differences.
                        if (prior.Rows.GroupBy(r => r.RowKey).Any(g => g.Count() > 1)
                            || next.Rows.GroupBy(r => r.RowKey).Any(g => g.Count() > 1)) continue;
                    }
                    foreach (var row in diff.Rows)
                    foreach (var cell in row.Cells)
                    {
                        if (cell.PriorValue == cell.CurrentValue) continue;
                        string? exactDelta = null;
                        string? percent = null;
                        var hasPrior = decimal.TryParse(cell.PriorValue, NumberStyles.Number, CultureInfo.InvariantCulture, out var p);
                        var hasCurrent = decimal.TryParse(cell.CurrentValue, NumberStyles.Number, CultureInfo.InvariantCulture, out var c);
                        if (hasPrior && hasCurrent) exactDelta = (c - p).ToString("0.############################", CultureInfo.InvariantCulture);
                        if (hasPrior && decimal.TryParse(exactDelta, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) && p != 0)
                        {
                            try { percent = (d / Math.Abs(p) * 100m).ToString("0.############################", CultureInfo.InvariantCulture); }
                            catch (OverflowException)
                            { unavailable.Add($"Grid '{next.GridId}', row '{row.RowKey}': percentage exceeds supported decimal range."); }
                        }
                        lines.Add(new(next.GridId, row.RowKey, cell.Column, row.State.ToString(),
                            cell.PriorValue, cell.CurrentValue, exactDelta, percent,
                            [$"run:{baseline.RunId}:snapshot:{baseline.Snapshot.SnapshotHash}", $"run:{current.RunId}:snapshot:{current.Snapshot.SnapshotHash}"])
                        {
                            IsMeasure = next.Columns.Concat(prior.Columns).Any(column => column.Key == cell.Column && column.Role is "metric" or "formula")
                        });
                    }
                }
                foreach (var prior in priorGrids.Where(g => !nextGrids.Any(n => n.GridId == g.GridId)))
                {
                    completeLineComparison = false;
                    unavailable.Add($"Grid '{prior.GridId}' is absent from the current retained output.");
                }
            }
        }
        var materiality = ReportingReviewMateriality.Assess(policy, currentManifest, baselineManifest, lines, completeLineComparison);
        unavailable.AddRange(materiality.UnavailableReasons);
        var review = new ReportingRunReview("", current.RunId, current.Version, current.Snapshot.SnapshotHash,
            current.Scope.PeriodId, currentManifest.ResolvedParameters?.PresentationCurrency,
            currentManifest.ResolvedParameters?.AccountingBasis.ToString(), baseline?.RunId, baseline?.Version, parameters, sources, materiality.Lines, materiality.Outcome, materiality.MaterialChangeCount, unavailable)
        { MaterialityPolicy = policy };
        var retained = new RetainedReportingRunReview(Format, review, NormalizeForRetention(currentManifest),
            baselineManifest is null ? null : NormalizeForRetention(baselineManifest));
        var id = Sha256Digest.Compute(JsonSerializer.SerializeToUtf8Bytes(retained,
            ReportingRunReviewJsonContext.Default.RetainedReportingRunReview));
        return retained with { Review = review with { ReviewId = id } };
    }

    private static ReportingOutputManifest NormalizeForRetention(ReportingOutputManifest manifest) => manifest with
    {
        Sections = manifest.Sections.IsDefault ? [] : manifest.Sections,
        Artifacts = manifest.Artifacts.IsDefault ? [] : manifest.Artifacts,
        ReportWriterGrids = manifest.ReportWriterGrids.IsDefault ? [] : manifest.ReportWriterGrids,
        RenderedReportWriterGrids = manifest.RenderedReportWriterGrids.IsDefault ? [] : manifest.RenderedReportWriterGrids,
        ReportWriterGridDiffs = manifest.ReportWriterGridDiffs.IsDefault ? [] : manifest.ReportWriterGridDiffs,
        CertifiedDatasetRows = manifest.CertifiedDatasetRows.IsDefault ? [] : manifest.CertifiedDatasetRows
    };

    public static bool Comparable(GovernedReportingRun current, ReportingOutputManifest currentManifest,
        GovernedReportingRun prior, ReportingOutputManifest priorManifest) =>
        prior.RunId != current.RunId && prior.SeriesId == current.SeriesId && prior.Revision < current.Revision
        && prior.GovernanceState is GovernedReportingState.Approved or GovernedReportingState.Released
        && current.Scope == prior.Scope && current.TemplateId == prior.TemplateId
        && currentManifest.ResolvedParameters is { } cp && priorManifest.ResolvedParameters is { } pp
        && currentManifest.AsOfDate == priorManifest.AsOfDate && cp.PeriodId == pp.PeriodId
        && cp.AccountingBasis == pp.AccountingBasis && cp.PresentationCurrency == pp.PresentationCurrency
        && cp.LedgerBook == pp.LedgerBook;

    private static SortedDictionary<string, string> ParameterValues(GovernedReportingRun run, ReportingOutputManifest manifest)
    {
        var result = new SortedDictionary<string, string>(StringComparer.Ordinal) { ["templateVersion"] = run.TemplateVersion };
        if (manifest.ResolvedParameters is { } parameters)
        {
            var json = JsonSerializer.SerializeToElement(parameters,
                ReportingIncomeComparisonJsonContext.Default.ReportingOutputManifest.Options);
            void Add(string key, JsonElement value)
            {
                if (value.ValueKind == JsonValueKind.Object)
                {
                    foreach (var property in value.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
                        Add(string.IsNullOrEmpty(key) ? property.Name : $"{key}.{property.Name}", property.Value);
                }
                else result[key] = value.ValueKind == JsonValueKind.String ? value.GetString() ?? "Not recorded"
                    : value.ValueKind == JsonValueKind.Null ? "Not recorded" : value.GetRawText();
            }
            Add("", json);
        }
        return result;
    }

    private static SortedDictionary<string, string>? SourceRows(ReportingOutputManifest manifest)
    {
        var result = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var identities = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in manifest.CertifiedDatasetRows)
        {
            if (!row.TryGetValue("entryId", out var id) || string.IsNullOrWhiteSpace(id)) return null;
            if (!identities.Add(id)) return null;
            foreach (var pair in row.OrderBy(p => p.Key, StringComparer.Ordinal)) result.Add($"{id} · {pair.Key}", pair.Value);
        }
        return result;
    }

    private static IEnumerable<ReportingReviewChange> Diff(IReadOnlyDictionary<string, string> before,
        IReadOnlyDictionary<string, string> after) =>
        before.Keys.Concat(after.Keys).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)
            .Where(key => before.GetValueOrDefault(key) != after.GetValueOrDefault(key))
            .Select(key => new ReportingReviewChange(key, before.GetValueOrDefault(key), after.GetValueOrDefault(key)));
}

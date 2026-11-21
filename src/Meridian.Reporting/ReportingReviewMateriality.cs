using System.Globalization;
using System.Numerics;
using System.Text.Json.Serialization;

namespace Meridian.Reporting;

/// <summary>Explicit monetary measures only; no inferred units or default tolerances.</summary>
public sealed record ReportingReviewMetricThreshold(
    string GridId, string Column,
    [property: JsonConverter(typeof(ReportingIncomeNullableDecimalJsonConverter))] decimal? AbsoluteVariance = null,
    [property: JsonConverter(typeof(ReportingIncomeNullableDecimalJsonConverter))] decimal? RelativeVariancePercent = null);

/// <summary>Admin-provisioned policy bound to an exact scope, template revision and currency.</summary>
public sealed record ReportingReviewMaterialityPolicy(
    string PolicyId, string Version, ReportingOperationalScope Scope,
    string TemplateId, string TemplateVersion, string Currency,
    IReadOnlyList<ReportingReviewMetricThreshold> Metrics);

public interface IReportingReviewMaterialityPolicyProvider
{
    ValueTask<ReportingReviewMaterialityPolicy?> ResolveAsync(
        ReportingOperationalScope scope, string templateId, string templateVersion, string currency,
        CancellationToken ct = default);
}

public sealed record ReportingReviewMaterialityAssessment(
    string Outcome, int? MaterialChangeCount, IReadOnlyList<ReportingReviewLine> Lines,
    IReadOnlyList<string> UnavailableReasons);

public static class ReportingReviewMateriality
{
    public static void ValidatePolicy(ReportingReviewMaterialityPolicy policy)
    {
        if (policy is null || string.IsNullOrWhiteSpace(policy.PolicyId) || string.IsNullOrWhiteSpace(policy.Version)
            || string.IsNullOrWhiteSpace(policy.TemplateId) || string.IsNullOrWhiteSpace(policy.TemplateVersion)
            || string.IsNullOrWhiteSpace(policy.Currency) || policy.Scope is null || policy.Metrics is not { Count: > 0 }
            || policy.Metrics.Any(m => m is null || string.IsNullOrWhiteSpace(m.GridId) || string.IsNullOrWhiteSpace(m.Column)
                || m.AbsoluteVariance < 0m || m.RelativeVariancePercent < 0m)
            || policy.Metrics.GroupBy(m => (m.GridId, m.Column)).Any(g => g.Count() != 1))
            throw new InvalidDataException("Reporting materiality policy identity, exact measure bindings and nonnegative thresholds are required.");
        ReportingGovernanceCanonicalValidation.ValidateScope(policy.Scope);
    }

    public static ReportingReviewMaterialityAssessment Assess(
        ReportingReviewMaterialityPolicy? policy, ReportingOutputManifest current,
        ReportingOutputManifest? baseline, IReadOnlyList<ReportingReviewLine> lines, bool completeLineComparison)
    {
        if (policy is null)
            return new("NotAssessed", null, lines,
                ["Materiality not assessed: no authoritative report/template policy is configured."]);
        ValidatePolicy(policy);
        var unassessed = !completeLineComparison || baseline is null;
        var reasons = new List<string>();
        if (unassessed) reasons.Add("Materiality not fully assessed: a complete comparable retained line population is required.");
        // Even unchanged missing cells must not turn an absent input into a passing assessment.
        foreach (var metric in policy.Metrics)
        {
            var beforeMatches = baseline?.RenderedReportWriterGrids.IsDefault == false
                ? baseline.RenderedReportWriterGrids.Where(g => g.GridId == metric.GridId).ToArray() : [];
            var afterMatches = !current.RenderedReportWriterGrids.IsDefault
                ? current.RenderedReportWriterGrids.Where(g => g.GridId == metric.GridId).ToArray() : [];
            var before = beforeMatches.Length == 1 ? beforeMatches[0] : null;
            var after = afterMatches.Length == 1 ? afterMatches[0] : null;
            var beforeRows = before?.Rows ?? [];
            var afterRows = after?.Rows ?? [];
            if (metric.AbsoluteVariance is null && metric.RelativeVariancePercent is null
                || before is null || after is null || beforeRows.Count == 0 || afterRows.Count == 0
                || !before.Columns.Any(c => c.Key == metric.Column && c.Role is "metric" or "formula")
                || !after.Columns.Any(c => c.Key == metric.Column && c.Role is "metric" or "formula")
                || beforeRows.GroupBy(r => r.RowKey).Any(g => g.Count() != 1)
                || afterRows.GroupBy(r => r.RowKey).Any(g => g.Count() != 1)
                || beforeRows.Any(r => !Try(r.Values.GetValueOrDefault(metric.Column), out var value)
                    || metric.RelativeVariancePercent is not null && value == 0m)
                || afterRows.Any(r => !Try(r.Values.GetValueOrDefault(metric.Column), out _)))
            {
                unassessed = true;
                reasons.Add($"Materiality not assessed for {metric.GridId}/{metric.Column}: configured thresholds and numeric retained inputs on both runs are required.");
            }
        }
        var assessed = new List<ReportingReviewLine>(lines.Count);
        foreach (var line in lines)
        {
            if (!line.IsMeasure) { assessed.Add(line); continue; }
            var threshold = policy.Metrics.SingleOrDefault(m => m.GridId == line.GridId && m.Column == line.Column);
            var hasDelta = Try(line.Delta, out var delta);
            var hasPrior = Try(line.Previous, out var prior);
            var hasCurrent = Try(line.Current, out _);
            var outcome = "WithinTolerance";
            if (threshold is null || !hasDelta || !hasPrior || !hasCurrent
                || threshold.AbsoluteVariance is null && threshold.RelativeVariancePercent is null)
                outcome = "NotAssessed";
            else
            {
                var breach = threshold.AbsoluteVariance is { } absolute && Math.Abs(delta) > absolute;
                var missing = false;
                if (threshold.RelativeVariancePercent is { } relative)
                {
                    if (prior == 0m) missing = true;
                    else breach |= ExceedsRelative(delta, prior, relative);
                }
                outcome = breach ? "MaterialException" : missing ? "NotAssessed" : "WithinTolerance";
            }
            if (outcome == "NotAssessed")
            {
                unassessed = true;
                reasons.Add($"Materiality not assessed for {line.GridId}/{line.RowKey}/{line.Column}: a bound threshold and numeric before/after inputs are required; relative variance requires a nonzero prior.");
            }
            assessed.Add(line with { MaterialityOutcome = outcome });
        }
        var count = assessed.Where(l => l.MaterialityOutcome == "MaterialException")
            .Select(l => (l.GridId, l.RowKey)).Distinct().Count();
        return new(count > 0 ? "MaterialException" : unassessed ? "NotAssessed" : "WithinTolerance",
            unassessed ? null : count, assessed, reasons.Distinct().ToArray());
    }

    private static bool Try(string? text, out decimal value) =>
        decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out value);

    // Cross multiplication of decimal coefficients avoids division rounding and intermediate overflow.
    // Threshold equality remains within tolerance even at decimal's full precision.
    private static bool ExceedsRelative(decimal delta, decimal prior, decimal threshold)
    {
        static (BigInteger Coefficient, int Scale) Parts(decimal value)
        {
            var bits = decimal.GetBits(value);
            return ((BigInteger)(uint)bits[0] | (BigInteger)(uint)bits[1] << 32 | (BigInteger)(uint)bits[2] << 64,
                (bits[3] >> 16) & 255);
        }
        var d = Parts(delta); var p = Parts(prior); var t = Parts(threshold);
        return d.Coefficient * 100 * BigInteger.Pow(10, p.Scale + t.Scale)
            > p.Coefficient * t.Coefficient * BigInteger.Pow(10, d.Scale);
    }
}

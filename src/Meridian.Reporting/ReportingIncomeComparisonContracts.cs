using System.Collections.Immutable;
using Meridian.Contracts.Workstation;

namespace Meridian.Reporting;

/// <summary>Both inputs are explicit immutable run identities; no implicit latest baseline.</summary>
public sealed record ReportingIncomeComparisonRequestDto(
    string BaselineRunId, string CurrentRunId, string GridId, string MetricColumn);

public sealed record ReportingIncomeComparisonMetricDto(string Column, string Label, string? SourceField);
public sealed record ReportingIncomeComparisonGridDto(
    string GridId, string Title, IReadOnlyList<ReportingIncomeComparisonMetricDto> Metrics);

public sealed record ReportingIncomeComparisonRunDto(
    string RunId, string TemplateId, string? TemplateVersion, string PublicationLabel,
    int? Revision, string? RestatementOfRunId, string Status, string PeriodId,
    string AsOfDate, string Population, string AccountingBasis, string Currency,
    IReadOnlyList<ReportingIncomeComparisonGridDto> Grids);

public sealed record ReportingIncomeComparisonDifferenceDto(
    string Dimension, string Baseline, string Current, bool Compatible, string Detail);

public sealed record ReportingIncomeContributionDto(
    string ContributionId, string Kind, string Label, decimal Amount,
    string RecordId, string SourceRunId, string Detail);

public sealed record ReportingIncomeComparisonDto(
    string ComparisonId, string ExplanationVersion, DateTimeOffset RetainedAtUtc,
    ReportingIncomeComparisonRunDto Baseline, ReportingIncomeComparisonRunDto Current,
    string GridId, string MetricColumn, string Status, bool Compatible,
    decimal? BaselineAmount, decimal? CurrentAmount, decimal? Movement,
    decimal ExplainedAmount, decimal? ResidualAmount,
    IReadOnlyList<ReportingIncomeComparisonDifferenceDto> Differences,
    IReadOnlyList<ReportingIncomeContributionDto> Contributions,
    IReadOnlyList<string> Warnings, ReportWriterGridDiffDto GridDiff);

public sealed record ReportingIncomeContributionSupportDto(
    string ComparisonId, string BaselineRunId, string CurrentRunId,
    ReportingIncomeContributionDto Contribution,
    IReadOnlyList<IReadOnlyDictionary<string, string>> BaselineRecords,
    IReadOnlyList<IReadOnlyDictionary<string, string>> CurrentRecords,
    IReadOnlyList<string> EvidenceReferences);

/// <summary>
/// A quantified, documented methodology bridge captured with the current manifest. Impact is
/// allowed only for the named baseline, measure and exact unchanged retained ledger lines.
/// </summary>
public sealed record ReportingIncomeMethodologyEvidence(
    string RecordId, string BaselineRunId, string GridId, string MetricColumn,
    string Description, decimal BaselineAmount, decimal CurrentAmount,
    ImmutableArray<string> EntryIds, ImmutableArray<string> EvidenceReferences);

/// <summary>Durable content-addressed envelope; input manifests include the original access scopes.</summary>
public sealed record RetainedReportingIncomeComparison(
    string Format, ReportingOutputManifest BaselineManifest, ReportingOutputManifest CurrentManifest,
    ReportingIncomeComparisonDto Comparison,
    IReadOnlyList<ReportingIncomeContributionSupportDto> Support);

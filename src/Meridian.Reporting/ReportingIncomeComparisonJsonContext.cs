using System.Text.Json.Serialization;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Workstation;

namespace Meridian.Reporting;

/// <summary>
/// Reporting-owned generated metadata for the complete retained comparison graph and its wire
/// responses. Match the historical Web defaults so old artifact payloads remain readable.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    NumberHandling = JsonNumberHandling.AllowReadingFromString)]
[JsonSerializable(typeof(RetainedReportingIncomeComparison))]
[JsonSerializable(typeof(ReportingOutputManifest))]
[JsonSerializable(typeof(ReportingIncomeComparisonDto))]
[JsonSerializable(typeof(ReportingIncomeContributionSupportDto))]
[JsonSerializable(typeof(ReportingIncomeComparisonRequestDto))]
[JsonSerializable(typeof(IReadOnlyList<ReportingIncomeComparisonRunDto>), TypeInfoPropertyName = "IncomeComparisonRuns")]
[JsonSerializable(typeof(ReportingIncomeMethodologyDefinition))]
[JsonSerializable(typeof(LedgerDimensionSetDto))]
[JsonSerializable(typeof(IReadOnlyDictionary<string, string>))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(SortedDictionary<string, string>))]
public sealed partial class ReportingIncomeComparisonJsonContext : JsonSerializerContext;

/// <summary>Typed retained definition, including the exact filter sequence, for methodology evidence.</summary>
public sealed record ReportingIncomeMethodologyDefinition(
    ReportWriterGridKindDto Kind,
    ReportWriterMetricLineageDto? Metric,
    ReportWriterFormulaLineageDto? Formula,
    IReadOnlyList<ReportWriterFilterLineageDto>? Filters);

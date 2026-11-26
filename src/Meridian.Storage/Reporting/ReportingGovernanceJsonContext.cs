using System.Text.Json.Serialization;
using Meridian.Reporting;

namespace Meridian.Storage.Reporting;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(GovernedReportingRun))]
[JsonSerializable(typeof(ReportingRestatementRequest))]
[JsonSerializable(typeof(ReportingGovernanceAuditEntry))]
internal sealed partial class ReportingGovernanceJsonContext : JsonSerializerContext;

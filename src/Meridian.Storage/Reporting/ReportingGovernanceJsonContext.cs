using System.Text.Json.Serialization;
using Meridian.Reporting;

namespace Meridian.Storage.Reporting;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(GovernedReportingRun))]
[JsonSerializable(typeof(ReportingRestatementRequest))]
[JsonSerializable(typeof(ReportingGovernanceAuditEntry))]
[JsonSerializable(typeof(GovernedReportingRunV1))]
[JsonSerializable(typeof(ReportingRestatementRequestV1))]
internal sealed partial class ReportingGovernanceJsonContext : JsonSerializerContext;

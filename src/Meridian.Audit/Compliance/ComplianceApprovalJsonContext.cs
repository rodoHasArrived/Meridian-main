using System.Text.Json.Serialization;

namespace Meridian.Audit.Compliance;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true)]
[JsonSerializable(typeof(ComplianceApprovalSnapshot))]
internal sealed partial class ComplianceApprovalJsonContext : JsonSerializerContext;

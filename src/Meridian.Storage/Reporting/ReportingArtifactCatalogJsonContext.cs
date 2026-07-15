using System.Text.Json.Serialization;
using Meridian.Reporting;

namespace Meridian.Storage.Reporting;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(ReportingRetainedArtifactPackage))]
[JsonSerializable(typeof(ReportingRetainedArtifactRecord))]
[JsonSerializable(typeof(ReportingArtifactAuditEvent))]
internal sealed partial class ReportingArtifactCatalogJsonContext : JsonSerializerContext;

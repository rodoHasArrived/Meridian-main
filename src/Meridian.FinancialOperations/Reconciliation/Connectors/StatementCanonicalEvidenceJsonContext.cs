using System.Text.Json;
using System.Text.Json.Serialization;

namespace Meridian.FinancialOperations.Reconciliation.Connectors;

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, WriteIndented = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(StatementCanonicalEvidenceArtifact))]
internal sealed partial class StatementCanonicalEvidenceJsonContext : JsonSerializerContext;

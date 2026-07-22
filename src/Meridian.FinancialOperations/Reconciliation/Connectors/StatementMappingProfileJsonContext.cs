using System.Text.Json;
using System.Text.Json.Serialization;

namespace Meridian.FinancialOperations.Reconciliation.Connectors;

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, WriteIndented = true)]
[JsonSerializable(typeof(StatementMappingProfileDocument))]
[JsonSerializable(typeof(StatementMappingProfileSnapshot))]
internal sealed partial class StatementMappingProfileJsonContext : JsonSerializerContext;

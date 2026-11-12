using System.Text.Json.Serialization;

namespace Meridian.Execution.Serialization;

public sealed record PaperFinancingRequest(DateOnly AccrualDate, decimal Amount,
    string SourceReference, string Rationale);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(PaperFinancingRequest))]
public partial class PaperFinancingApiJsonContext : JsonSerializerContext
{
}

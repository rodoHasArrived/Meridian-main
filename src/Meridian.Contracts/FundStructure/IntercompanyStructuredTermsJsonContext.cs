using System.Text.Json.Serialization;

namespace Meridian.Contracts.FundStructure;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(IntercompanyStructuredTermsDto))]
[JsonSerializable(typeof(IntercompanyStructuredTermsPreviewRequestDto))]
[JsonSerializable(typeof(IntercompanyStructuredTermsPreviewDto))]
public partial class IntercompanyStructuredTermsJsonContext : JsonSerializerContext;

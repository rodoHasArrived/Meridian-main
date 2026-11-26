using System.Text.Json.Serialization;
using Meridian.Contracts.Api.Quality;

namespace Meridian.Ui.Shared.Serialization;

/// <summary>
/// Source-generated JSON metadata for the shared data-quality dashboard and commands.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(QualityDashboardResponse))]
[JsonSerializable(typeof(QualityCompositeDashboardResponse))]
[JsonSerializable(typeof(QualityGapRemediationRequest))]
[JsonSerializable(typeof(QualityGapRemediationResponse))]
internal sealed partial class QualityApiJsonContext : JsonSerializerContext;

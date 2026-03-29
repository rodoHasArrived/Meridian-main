using System.Text.Json.Serialization;
using Meridian.Contracts.DirectLending;
using Meridian.Infrastructure.Contracts;

namespace Meridian.Ui.Shared.Serialization;

/// <summary>
/// Source-generated <see cref="JsonSerializerContext"/> for concrete Direct Lending request/response
/// types used in HTTP endpoint deserialization.
/// </summary>
/// <remarks>
/// Per ADR-014, all JSON serialization must use source-generated contexts to avoid reflection-based
/// <see cref="System.Text.Json.JsonSerializer"/> calls at hot paths.
///
/// <b>Limitation — open-generic types:</b>
/// <see cref="DirectLendingCommandEnvelope{TCommand}"/> is an open generic record. The .NET source
/// generator cannot emit type metadata for open-generic types because the closed form is not known
/// at compile time. The <c>TryBindCommand&lt;TCommand&gt;</c> helper in
/// <see cref="Endpoints.DirectLendingEndpoints"/> therefore retains the reflection-based
/// <c>JsonSerializer.Deserialize&lt;T&gt;(string, JsonSerializerOptions)</c> overload for those
/// code paths. This is an accepted, documented deviation from ADR-014.
///
/// All other deserialization calls in DirectLendingEndpoints use this context directly.
/// </remarks>
[ImplementsAdr("ADR-014", "Source-generated JSON context for Direct Lending endpoint types — eliminates reflection overhead for concrete request types")]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(RequestProjectionRunRequest))]
[JsonSerializable(typeof(ResolveReconciliationExceptionRequest))]
internal sealed partial class DirectLendingJsonContext : JsonSerializerContext;

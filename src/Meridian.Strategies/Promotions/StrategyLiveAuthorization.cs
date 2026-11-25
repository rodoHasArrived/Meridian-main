using System.Text.Json.Serialization;

namespace Meridian.Strategies.Promotions;

public sealed record StrategyLiveAuthorizationRequest(
    string ExpectedStrategyArtifactId,
    IReadOnlyList<string> AllowedSymbols,
    decimal MaxOrderNotional,
    string Currency,
    DateTimeOffset ExpiresAt);

public sealed record StrategyLiveAuthorization(
    Guid FundAccountId,
    string GatewayId,
    string BrokerScope,
    IReadOnlyList<string> AllowedSymbols,
    decimal MaxOrderNotional,
    string Currency,
    DateTimeOffset ExpiresAt);

public sealed record StrategyLiveAuthorizationRevocation(
    string AuthorizationId, string Actor, string Reason, DateTimeOffset RevokedAt);

public sealed record StrategyLiveAuthorizationStatus(string State, string? Reason);

[JsonSerializable(typeof(StrategyLiveAuthorizationRevocation))]
internal sealed partial class StrategyLiveAuthorizationJsonContext : JsonSerializerContext;

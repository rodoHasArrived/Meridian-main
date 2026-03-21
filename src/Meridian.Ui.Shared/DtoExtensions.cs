using Meridian.Application.Config;
using Meridian.Contracts.Configuration;

namespace Meridian.Ui.Shared;

/// <summary>
/// Extension methods for converting DTOs to domain types.
/// Located in Ui.Shared since it needs references to both Contracts and core domain types.
/// </summary>
public static class DtoExtensions
{
    /// <summary>
    /// Converts AlpacaOptionsDto to AlpacaOptions domain type.
    /// </summary>
    public static AlpacaOptions ToDomain(this AlpacaOptionsDto dto) => new(
        KeyId: dto.KeyId ?? "",
        SecretKey: dto.SecretKey ?? "",
        Feed: dto.Feed,
        UseSandbox: dto.UseSandbox,
        SubscribeQuotes: dto.SubscribeQuotes
    );

    /// <summary>
    /// Converts PolygonOptionsDto to PolygonOptions domain type.
    /// </summary>
    public static PolygonOptions ToDomain(this PolygonOptionsDto dto) => new(
        ApiKey: dto.ApiKey,
        UseDelayed: dto.UseDelayed,
        Feed: dto.Feed,
        SubscribeTrades: dto.SubscribeTrades,
        SubscribeQuotes: dto.SubscribeQuotes,
        SubscribeAggregates: dto.SubscribeAggregates
    );

    /// <summary>
    /// Converts IBOptionsDto to IBOptions domain type.
    /// </summary>
    public static IBOptions ToDomain(this IBOptionsDto dto) => new(
        Host: dto.Host,
        Port: dto.Port,
        ClientId: dto.ClientId,
        UsePaperTrading: dto.UsePaperTrading,
        SubscribeDepth: dto.SubscribeDepth,
        DepthLevels: dto.DepthLevels,
        TickByTick: dto.TickByTick
    );

    /// <summary>
    /// Converts DerivativesConfigDto to DerivativesConfig domain type.
    /// </summary>
    public static DerivativesConfig ToDomain(this DerivativesConfigDto dto) => new(
        Enabled: dto.Enabled,
        Underlyings: dto.Underlyings,
        MaxDaysToExpiration: dto.MaxDaysToExpiration,
        StrikeRange: dto.StrikeRange,
        CaptureGreeks: dto.CaptureGreeks,
        CaptureChainSnapshots: dto.CaptureChainSnapshots,
        ChainSnapshotIntervalSeconds: dto.ChainSnapshotIntervalSeconds,
        CaptureOpenInterest: dto.CaptureOpenInterest,
        ExpirationFilter: dto.ExpirationFilter,
        IndexOptions: dto.IndexOptions?.ToDomain()
    );

    /// <summary>
    /// Converts IndexOptionsConfigDto to IndexOptionsConfig domain type.
    /// </summary>
    public static IndexOptionsConfig ToDomain(this IndexOptionsConfigDto dto) => new(
        Enabled: dto.Enabled,
        Indices: dto.Indices,
        IncludeWeeklies: dto.IncludeWeeklies,
        IncludeAmSettled: dto.IncludeAmSettled,
        IncludePmSettled: dto.IncludePmSettled
    );
}

using Meridian.Contracts.Configuration;

namespace Meridian.Application.Config;

/// <summary>
/// Maps shared configuration DTOs to core configuration records.
/// </summary>
public static class ConfigDtoMapper
{
    public static AlpacaOptions? ToDomain(this AlpacaOptionsDto? dto)
        => dto is null
            ? null
            : new AlpacaOptions(
                KeyId: dto.KeyId ?? string.Empty,
                SecretKey: dto.SecretKey ?? string.Empty,
                Feed: dto.Feed,
                UseSandbox: dto.UseSandbox,
                SubscribeQuotes: dto.SubscribeQuotes);

    public static PolygonOptions? ToDomain(this PolygonOptionsDto? dto)
        => dto is null
            ? null
            : new PolygonOptions(
                ApiKey: dto.ApiKey,
                UseDelayed: dto.UseDelayed,
                Feed: dto.Feed,
                SubscribeTrades: dto.SubscribeTrades,
                SubscribeQuotes: dto.SubscribeQuotes,
                SubscribeAggregates: dto.SubscribeAggregates);

    public static IBOptions? ToDomain(this IBOptionsDto? dto)
        => dto is null
            ? null
            : new IBOptions(
                Host: dto.Host,
                Port: dto.Port,
                ClientId: dto.ClientId,
                UsePaperTrading: dto.UsePaperTrading,
                SubscribeDepth: dto.SubscribeDepth,
                DepthLevels: dto.DepthLevels,
                TickByTick: dto.TickByTick);

    public static IBClientPortalOptions? ToDomain(this IBClientPortalOptionsDto? dto)
        => dto is null
            ? null
            : new IBClientPortalOptions(
                Enabled: dto.Enabled,
                BaseUrl: dto.BaseUrl,
                AllowSelfSignedCertificates: dto.AllowSelfSignedCertificates);

    public static DerivativesConfig? ToDomain(this DerivativesConfigDto? dto)
        => dto is null
            ? null
            : new DerivativesConfig(
                Enabled: dto.Enabled,
                Underlyings: dto.Underlyings,
                MaxDaysToExpiration: dto.MaxDaysToExpiration,
                StrikeRange: dto.StrikeRange,
                CaptureGreeks: dto.CaptureGreeks,
                CaptureChainSnapshots: dto.CaptureChainSnapshots,
                ChainSnapshotIntervalSeconds: dto.ChainSnapshotIntervalSeconds,
                CaptureOpenInterest: dto.CaptureOpenInterest,
                ExpirationFilter: dto.ExpirationFilter,
                IndexOptions: dto.IndexOptions?.ToDomain());

    public static IndexOptionsConfig? ToDomain(this IndexOptionsConfigDto? dto)
        => dto is null
            ? null
            : new IndexOptionsConfig(
                Enabled: dto.Enabled,
                Indices: dto.Indices,
                IncludeWeeklies: dto.IncludeWeeklies,
                IncludeAmSettled: dto.IncludeAmSettled,
                IncludePmSettled: dto.IncludePmSettled);
}

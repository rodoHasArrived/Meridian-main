using System.Text.Json.Serialization;

namespace Meridian.Core.Config;

/// <summary>Conservative application caps for a declared vendor plan; never a credential or entitlement.</summary>
public sealed record BackfillVendorTier(string Id, string Label, int? RateLimitPerMinute = null, int? RateLimitPerHour = null);

public sealed record BackfillVendorDescriptor(string Id, string DisplayName, int DefaultPriority, bool RequiresApiKey,
    IReadOnlyList<BackfillVendorTier> Tiers);

/// <summary>Secret-free, bounded fields accepted by the provider editor.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record BackfillProviderEdit(bool Enabled, int Priority, string TierId = "free",
    int? RateLimitPerMinute = null, int? RateLimitPerHour = null);

/// <summary>One source of UI presets and constructor defaults. Paid plans retain conservative finite application caps.</summary>
public static class BackfillProviderCatalog
{
    public const int AlpacaPriority = 5, AlpacaPerMinute = 200;
    public const int PolygonPriority = 12, PolygonPerMinute = 5, PolygonStarterPerMinute = 100;
    public const int TiingoPriority = 15, TiingoPerHour = 50;
    public const int FinnhubPriority = 18, FinnhubPerMinute = 60;
    public const int StooqPriority = 20;
    public const int YahooPriority = 10, YahooPerHour = 2000;
    public const int AlphaVantagePriority = 25, AlphaVantagePerMinute = 5;
    public const int NasdaqPriority = 30;
    public const int TwelveDataPriority = 22, TwelveDataPerMinute = 8;
    public const int OpenFigiPriority = 100, OpenFigiPerMinute = 25, OpenFigiAuthenticatedPerMinute = 250;

    public static IReadOnlyList<BackfillVendorDescriptor> Providers { get; } = Array.AsReadOnly(new BackfillVendorDescriptor[]
    {
        new("alpaca", "Alpaca", AlpacaPriority, true, [new("free", "IEX", AlpacaPerMinute)]),
        new("polygon", "Polygon", PolygonPriority, true, [new("free", "Basic", PolygonPerMinute), new("starter", "Starter (100/min application cap)", PolygonStarterPerMinute)]),
        new("tiingo", "Tiingo", TiingoPriority, true, [new("free", "Free", RateLimitPerHour: TiingoPerHour)]),
        new("finnhub", "Finnhub", FinnhubPriority, true, [new("free", "Free", FinnhubPerMinute)]),
        new("stooq", "Stooq", StooqPriority, false, [new("free", "Public")]),
        new("yahoo", "Yahoo Finance", YahooPriority, false, [new("free", "Public (unofficial)", RateLimitPerHour: YahooPerHour)]),
        new("alphavantage", "Alpha Vantage", AlphaVantagePriority, true, [new("free", "Free (25/day retained)", AlphaVantagePerMinute)]),
        new("nasdaqdatalink", "Nasdaq Data Link", NasdaqPriority, false, [new("free", "Public datasets")]),
        new("twelvedata", "Twelve Data", TwelveDataPriority, true, [new("free", "Basic", TwelveDataPerMinute)]),
        new("openfigi", "OpenFIGI (symbol resolution)", OpenFigiPriority, false, [new("free", "Unauthenticated", OpenFigiPerMinute), new("authenticated", "Authenticated", OpenFigiAuthenticatedPerMinute)])
    });

    public static Meridian.Contracts.Configuration.BackfillProviderMetadataDto[] Describe()
        => Providers.Select(provider => new Meridian.Contracts.Configuration.BackfillProviderMetadataDto
        {
            ProviderId = provider.Id,
            DisplayName = provider.DisplayName,
            Description = provider.Id == "openfigi" ? "Identifier resolution; this provider does not serve historical bars." : "Configured historical provider. Paid presets are conservative application caps; select only your subscribed vendor plan.",
            RequiresApiKey = provider.RequiresApiKey,
            FreeTier = true,
            DefaultPriority = provider.DefaultPriority,
            DefaultRateLimitPerMinute = provider.Tiers[0].RateLimitPerMinute,
            DefaultRateLimitPerHour = provider.Tiers[0].RateLimitPerHour,
            DataTypes = provider.Id switch
            {
                "openfigi" => ["Identifiers"],
                "alpaca" => ["Bars", "Trades", "Quotes"],
                "polygon" => ["Bars", "Trades", "Quotes", "Aggregates"],
                "yahoo" => ["Daily bars", "Intraday bars", "Aggregates"],
                "alphavantage" => ["Daily bars", "Intraday bars"],
                "nasdaqdatalink" => ["Various"],
                _ => ["Daily bars"]
            },
            SupportedGranularities = provider.Id switch
            {
                "openfigi" => [],
                "yahoo" => ["Daily", "1Min", "5Min", "15Min", "30Min", "Hourly", "4Hour"],
                "polygon" or "alpaca" or "alphavantage" => ["Daily", "1Min", "5Min", "15Min", "30Min", "Hourly"],
                _ => ["Daily"]
            },
            FeatureFlags = provider.Id switch
            {
                "alpaca" => new Dictionary<string, bool> { ["supportsCrypto"] = true, ["supportsExtendedHours"] = true },
                "polygon" => new Dictionary<string, bool> { ["supportsAggregates"] = true, ["supportsSnapshots"] = true },
                "tiingo" => new Dictionary<string, bool> { ["supportsCrypto"] = true },
                "finnhub" => new Dictionary<string, bool> { ["supportsInternational"] = true },
                "yahoo" => new Dictionary<string, bool> { ["supportsIntraday"] = true, ["unofficial"] = true },
                _ => null
            },
            Tiers = provider.Tiers.Select(t => new Meridian.Contracts.Configuration.BackfillVendorTierDto(t.Id, t.Label, t.RateLimitPerMinute, t.RateLimitPerHour)).ToArray()
        }).ToArray();

    public static BackfillVendorDescriptor Get(string id) => Providers.FirstOrDefault(p => p.Id == id)
        ?? throw new ArgumentException("Unknown provider. IB pacing and synthetic provenance are not editable here.", nameof(id));

    public static BackfillVendorTier GetTier(string id, string tierId) => Get(id).Tiers.FirstOrDefault(t => t.Id == tierId)
        ?? throw new ArgumentException("Unknown vendor tier.", nameof(tierId));

    public static BackfillProviderEdit Read(BackfillProvidersConfig config, string id)
    {
        var d = Get(id);
        return id switch
        {
            "alpaca" => new(config.Alpaca?.Enabled ?? true, config.Alpaca?.Priority ?? d.DefaultPriority, config.Alpaca?.TierId ?? "free", config.Alpaca?.RateLimitPerMinute ?? d.Tiers[0].RateLimitPerMinute),
            "polygon" => new(config.Polygon?.Enabled ?? true, config.Polygon?.Priority ?? d.DefaultPriority, config.Polygon?.TierId ?? "free", config.Polygon?.RateLimitPerMinute ?? d.Tiers[0].RateLimitPerMinute),
            "tiingo" => new(config.Tiingo?.Enabled ?? true, config.Tiingo?.Priority ?? d.DefaultPriority, config.Tiingo?.TierId ?? "free", RateLimitPerHour: config.Tiingo?.RateLimitPerHour ?? d.Tiers[0].RateLimitPerHour),
            "finnhub" => new(config.Finnhub?.Enabled ?? true, config.Finnhub?.Priority ?? d.DefaultPriority, config.Finnhub?.TierId ?? "free", config.Finnhub?.RateLimitPerMinute ?? d.Tiers[0].RateLimitPerMinute),
            "stooq" => new(config.Stooq?.Enabled ?? true, config.Stooq?.Priority ?? d.DefaultPriority),
            "yahoo" => new(config.Yahoo?.Enabled ?? true, config.Yahoo?.Priority ?? d.DefaultPriority, config.Yahoo?.TierId ?? "free", RateLimitPerHour: config.Yahoo?.RateLimitPerHour ?? d.Tiers[0].RateLimitPerHour),
            "alphavantage" => new(config.AlphaVantage?.Enabled ?? false, config.AlphaVantage?.Priority ?? d.DefaultPriority, RateLimitPerMinute: config.AlphaVantage?.RateLimitPerMinute ?? d.Tiers[0].RateLimitPerMinute),
            "nasdaqdatalink" => new(config.Nasdaq?.Enabled ?? true, config.Nasdaq?.Priority ?? d.DefaultPriority),
            "twelvedata" => new(config.TwelveData?.Enabled ?? true, config.TwelveData?.Priority ?? d.DefaultPriority, config.TwelveData?.TierId ?? "free", config.TwelveData?.RateLimitPerMinute ?? d.Tiers[0].RateLimitPerMinute),
            "openfigi" => new(config.OpenFigi?.Enabled ?? true, d.DefaultPriority, config.OpenFigi?.TierId ?? "free", config.OpenFigi?.RateLimitPerMinute ?? GetTier("openfigi", config.OpenFigi?.TierId ?? "free").RateLimitPerMinute),
            _ => throw new ArgumentException("Unknown provider.", nameof(id))
        };
    }

    public static BackfillProvidersConfig Apply(BackfillProvidersConfig config, string id, BackfillProviderEdit edit)
    {
        var tier = GetTier(id, edit.TierId);
        if (edit.Priority is < 0 or > 10000)
            throw new ArgumentException("Priority must be between 0 and 10000.");
        ValidateLimit(edit.RateLimitPerMinute, tier.RateLimitPerMinute, "minute");
        ValidateLimit(edit.RateLimitPerHour, tier.RateLimitPerHour, "hour");
        var minute = edit.RateLimitPerMinute ?? tier.RateLimitPerMinute;
        var hour = edit.RateLimitPerHour ?? tier.RateLimitPerHour;
        return id switch
        {
            "alpaca" => config with { Alpaca = (config.Alpaca ?? new()) with { Enabled = edit.Enabled, Priority = edit.Priority, TierId = edit.TierId, RateLimitPerMinute = minute!.Value } },
            "polygon" => config with { Polygon = (config.Polygon ?? new()) with { Enabled = edit.Enabled, Priority = edit.Priority, TierId = edit.TierId, RateLimitPerMinute = minute!.Value } },
            "tiingo" => config with { Tiingo = (config.Tiingo ?? new()) with { Enabled = edit.Enabled, Priority = edit.Priority, TierId = edit.TierId, RateLimitPerHour = hour!.Value } },
            "finnhub" => config with { Finnhub = (config.Finnhub ?? new()) with { Enabled = edit.Enabled, Priority = edit.Priority, TierId = edit.TierId, RateLimitPerMinute = minute!.Value } },
            "stooq" => config with { Stooq = (config.Stooq ?? new()) with { Enabled = edit.Enabled, Priority = edit.Priority } },
            "yahoo" => config with { Yahoo = (config.Yahoo ?? new()) with { Enabled = edit.Enabled, Priority = edit.Priority, TierId = edit.TierId, RateLimitPerHour = hour!.Value } },
            "alphavantage" => config with { AlphaVantage = (config.AlphaVantage ?? new()) with { Enabled = edit.Enabled, Priority = edit.Priority, RateLimitPerMinute = minute!.Value } },
            "nasdaqdatalink" => config with { Nasdaq = (config.Nasdaq ?? new()) with { Enabled = edit.Enabled, Priority = edit.Priority } },
            "twelvedata" => config with { TwelveData = (config.TwelveData ?? new()) with { Enabled = edit.Enabled, Priority = edit.Priority, TierId = edit.TierId, RateLimitPerMinute = minute!.Value } },
            "openfigi" => config with { OpenFigi = (config.OpenFigi ?? new()) with { Enabled = edit.Enabled, TierId = edit.TierId, RateLimitPerMinute = minute } },
            _ => throw new ArgumentException("Unknown provider.")
        };
    }

    private static void ValidateLimit(int? requested, int? ceiling, string window)
    {
        if (requested is not null && (ceiling is null || requested < 1 || requested > ceiling))
            throw new ArgumentException($"Rate limit per {window} must be positive and no greater than the selected tier ceiling ({ceiling?.ToString() ?? "not configurable"}).");
    }
}

using Meridian.Core.Config;
using Meridian.Infrastructure.Resilience;

namespace Meridian.Infrastructure.Adapters.Alpaca;

/// <summary>Known Alpaca stream endpoints and their entitlement semantics.</summary>
internal static class AlpacaStreamProfiles
{
    public static IReadOnlyList<ProviderStreamDiagnostics> Create(
        AlpacaOptions options,
        WebSocketConnectionDiagnostics connection,
        MarketDataAssetClass connectedAssetClass = MarketDataAssetClass.Equities)
    {
        ArgumentNullException.ThrowIfNull(options);

        var equityFeed = options.EquitiesFeed.Trim().ToLowerInvariant();
        var equityIsIndicative = equityFeed is "iex" or "delayed_sip";
        var optionsFeed = string.IsNullOrWhiteSpace(options.OptionsFeed)
            ? "indicative"
            : options.OptionsFeed.Trim().ToLowerInvariant();

        return
        [
            new(MarketDataAssetClass.Equities, equityFeed, EquityEntitlement(equityFeed),
                StateFor(MarketDataAssetClass.Equities), IsConnectedFor(MarketDataAssetClass.Equities), equityIsIndicative,
                equityIsIndicative ? "Limited or delayed equity feed; not consolidated SIP data." : null),
            new(MarketDataAssetClass.Options, optionsFeed, OptionsEntitlement(optionsFeed),
                StateFor(MarketDataAssetClass.Options), IsConnectedFor(MarketDataAssetClass.Options), optionsFeed != "opra",
                optionsFeed == "opra" ? "Options stream is configured but not connected." : "Indicative options data only; OPRA entitlement is not configured."),
            new(MarketDataAssetClass.Crypto, options.CryptoFeed, "crypto-us", StateFor(MarketDataAssetClass.Crypto),
                IsConnectedFor(MarketDataAssetClass.Crypto), false, "Crypto stream is configured separately and not connected."),
            new(MarketDataAssetClass.News, options.NewsFeed, "news-basic", StateFor(MarketDataAssetClass.News),
                IsConnectedFor(MarketDataAssetClass.News), true, "News stream is configured separately and not connected.")
        ];

        ProviderConnectionLifecycleState StateFor(MarketDataAssetClass assetClass) => assetClass == connectedAssetClass
            ? connection.LifecycleState
            : ProviderConnectionLifecycleState.NotConfigured;

        bool IsConnectedFor(MarketDataAssetClass assetClass) => assetClass == connectedAssetClass && connection.IsConnected;
    }

    private static string EquityEntitlement(string feed) => feed switch
    {
        "sip" => "sip",
        "delayed_sip" => "sip-delayed",
        "iex" => "iex",
        _ => feed
    };

    private static string OptionsEntitlement(string feed) => feed == "opra" ? "opra" : "indicative";
}

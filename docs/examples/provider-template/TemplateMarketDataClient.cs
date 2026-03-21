// Replace every occurrence of "Template" with your provider name.
// Replace the namespace segment "Template" with your provider's namespace segment.
using Meridian.Contracts.Configuration;
using Meridian.Domain.Collectors;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.Contracts;
using Meridian.Infrastructure.DataSources;
using Meridian.Infrastructure.Shared;
using System.Net.WebSockets;
using System.Text.Json;

namespace Meridian.Infrastructure.Adapters.Template;

/// <summary>
/// Template streaming market data client — replace "Template" with your provider name.
///
/// Implementation checklist:
/// - [ ] Replace every "Template" occurrence with your provider name
/// - [ ] Fill in WebSocket URI in <see cref="BuildWebSocketUri"/>
/// - [ ] Implement <see cref="AuthenticateAsync"/> with provider-specific auth message
/// - [ ] Implement <see cref="HandleMessageAsync"/> to parse and route provider messages
/// - [ ] Implement <see cref="ResubscribeAsync"/> to re-subscribe after reconnection
/// - [ ] Implement <see cref="SubscribeMarketDepth"/> / <see cref="UnsubscribeMarketDepth"/>
///   (return -1 if depth is not supported)
/// - [ ] Populate <see cref="ProviderCapabilities"/> with the correct flags
/// - [ ] Fill in <see cref="ProviderCredentialFields"/> with required API keys
/// - [ ] Remove the TODO comments and this checklist when implementation is complete
/// </summary>
// TODO: Replace "template" with the provider ID, display name, type, and category.
[DataSource("template", "Template Provider", DataSourceType.Realtime, DataSourceCategory.Other,
    Priority = 50, Description = "TODO: Add description")]
[ImplementsAdr("ADR-001", "Template streaming data provider implementation")]
[ImplementsAdr("ADR-004", "All async methods support CancellationToken")]
[ImplementsAdr("ADR-005", "Attribute-based provider discovery")]
public sealed class TemplateMarketDataClient : WebSocketProviderBase
{
    private readonly TradeDataCollector _tradeCollector;
    private readonly QuoteCollector _quoteCollector;

    // TODO: Replace TemplateOptions with the provider's configuration type.
    // private readonly TemplateOptions _options;

    /// <summary>
    /// Creates a new Template market data client.
    /// </summary>
    /// <param name="tradeCollector">Collector for incoming trade events.</param>
    /// <param name="quoteCollector">Collector for incoming quote events.</param>
    // Add the provider-specific options parameter and validate credentials, e.g.:
    //   public TemplateMarketDataClient(
    //       TradeDataCollector tradeCollector,
    //       QuoteCollector quoteCollector,
    //       TemplateStreamingOptions options)
    public TemplateMarketDataClient(
        TradeDataCollector tradeCollector,
        QuoteCollector quoteCollector
        /* TemplateStreamingOptions options */)
        : base(
            providerName: "Template",
            // Allocate a range in ProviderSubscriptionRanges.cs (600,000+ are reserved for future providers).
            subscriptionStartId: ProviderSubscriptionRanges.ReservedStart)
    {
        _tradeCollector = tradeCollector ?? throw new ArgumentNullException(nameof(tradeCollector));
        _quoteCollector = quoteCollector ?? throw new ArgumentNullException(nameof(quoteCollector));
        // _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    // ------------------------------------------------------------------ //
    //  IMarketDataClient                                                   //
    // ------------------------------------------------------------------ //

    /// <inheritdoc/>
    // Set to true when credentials are configured; false otherwise (stub mode).
    public override bool IsEnabled => false;

    // ------------------------------------------------------------------ //
    //  IProviderMetadata                                                   //
    // ------------------------------------------------------------------ //

    /// <inheritdoc/>
    public override string ProviderId => "template";

    /// <inheritdoc/>
    public override string ProviderDisplayName => "Template Provider Streaming";

    /// <inheritdoc/>
    public override string ProviderDescription => "TODO: Add a description of the provider.";

    /// <inheritdoc/>
    // Set an appropriate priority. Lower = tried first in failover chains.
    public override int ProviderPriority => 50;

    /// <inheritdoc/>
    // Adjust capability flags to match what the provider actually supports.
    public override ProviderCapabilities ProviderCapabilities { get; } = ProviderCapabilities.Streaming(
        trades: true,
        quotes: false,
        depth: false);

    /// <inheritdoc/>
    public override ProviderCredentialField[] ProviderCredentialFields =>
    [
        // Replace with the actual credential fields and environment variable names.
        new ProviderCredentialField("ApiKey", "TEMPLATE__APIKEY", "API Key", Required: true)
    ];

    // ------------------------------------------------------------------ //
    //  Subscription management                                            //
    // ------------------------------------------------------------------ //

    /// <inheritdoc/>
    public override int SubscribeTrades(SymbolConfig cfg)
    {
        if (cfg is null) throw new ArgumentNullException(nameof(cfg));
        var id = Subscriptions.Subscribe(cfg.Symbol, "trades");
        if (id == -1) return -1;

        // Send the provider-specific subscribe message, e.g.:
        // _ = SendAsync(BuildSubscribeMessage(cfg.Symbol), CancellationToken.None);
        return id;
    }

    /// <inheritdoc/>
    public override void UnsubscribeTrades(int subscriptionId)
    {
        var sub = Subscriptions.Unsubscribe(subscriptionId);
        if (sub is not null)
        {
            // Send the provider-specific unsubscribe message, e.g.:
            // _ = SendAsync(BuildUnsubscribeMessage(sub.Symbol), CancellationToken.None);
        }
    }

    /// <inheritdoc/>
    // Implement if the provider supports market depth; otherwise return -1.
    public override int SubscribeMarketDepth(SymbolConfig cfg) => -1;

    /// <inheritdoc/>
    public override void UnsubscribeMarketDepth(int subscriptionId) { }

    // ------------------------------------------------------------------ //
    //  WebSocketProviderBase template methods                             //
    // ------------------------------------------------------------------ //

    /// <inheritdoc/>
    protected override Uri BuildWebSocketUri()
    {
        // Return the provider-specific WebSocket URI using constants from TemplateConstants.cs, e.g.:
        //   return new Uri(TemplateEndpoints.WssUri);
        //   return new Uri($"{TemplateEndpoints.WssUri}/{_options.Feed}");
        throw new NotImplementedException("TODO: Implement BuildWebSocketUri");
    }

    /// <inheritdoc/>
    protected override async Task AuthenticateAsync(CancellationToken ct)
    {
        // Send the provider-specific authentication message, e.g.:
        //   var auth = JsonSerializer.Serialize(new { action = "auth", key = _options.ApiKey });
        //   await SendAsync(auth, ct).ConfigureAwait(false);
        await Task.CompletedTask.ConfigureAwait(false);
        throw new NotImplementedException("TODO: Implement AuthenticateAsync");
    }

    /// <inheritdoc/>
    protected override Task HandleMessageAsync(string message)
    {
        // Parse the incoming JSON message and route to the appropriate collector, e.g.:
        //   using var doc = JsonDocument.Parse(message);
        //   var type = doc.RootElement.GetProperty("type").GetString();
        //   if (type == TemplateMessageTypes.Trade) HandleTrade(doc.RootElement);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    protected override async Task ResubscribeAsync(CancellationToken ct)
    {
        // Re-subscribe to all active symbols after reconnection, e.g.:
        //   foreach (var sym in Subscriptions.GetActiveSymbols())
        //       await SendAsync(BuildSubscribeMessage(sym), ct).ConfigureAwait(false);
        await Task.CompletedTask.ConfigureAwait(false);
    }
}

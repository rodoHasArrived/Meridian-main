using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Models;
using Meridian.Infrastructure.Contracts;

namespace Meridian.Infrastructure.Adapters.Core;

/// <summary>
/// Provider contract for retrieving options chain data.
/// Implementations supply option chain snapshots, contract discovery,
/// and available expirations for underlying symbols.
/// </summary>
[ImplementsAdr("ADR-001", "Options chain data provider contract")]
[ImplementsAdr("ADR-004", "All async methods support CancellationToken")]
public interface IOptionsChainProvider : IProviderMetadata
{
    /// <summary>
    /// Gets available expiration dates for options on the given underlying symbol.
    /// </summary>
    /// <param name="underlyingSymbol">The underlying symbol (e.g., "AAPL", "SPX").</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Ordered list of available expiration dates.</returns>
    Task<IReadOnlyList<DateOnly>> GetExpirationsAsync(
        string underlyingSymbol,
        CancellationToken ct = default);

    /// <summary>
    /// Gets available strike prices for a specific underlying and expiration.
    /// </summary>
    /// <param name="underlyingSymbol">The underlying symbol.</param>
    /// <param name="expiration">The expiration date.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Ordered list of available strike prices.</returns>
    Task<IReadOnlyList<decimal>> GetStrikesAsync(
        string underlyingSymbol,
        DateOnly expiration,
        CancellationToken ct = default);

    /// <summary>
    /// Gets a full option chain snapshot for an underlying symbol and expiration.
    /// </summary>
    /// <param name="underlyingSymbol">The underlying symbol.</param>
    /// <param name="expiration">The expiration date.</param>
    /// <param name="strikeRange">
    /// Optional number of strikes above and below ATM to include.
    /// If null, returns all available strikes.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Option chain snapshot with calls and puts.</returns>
    Task<OptionChainSnapshot?> GetChainSnapshotAsync(
        string underlyingSymbol,
        DateOnly expiration,
        int? strikeRange = null,
        CancellationToken ct = default);

    /// <summary>
    /// Gets a quote for a specific option contract.
    /// </summary>
    /// <param name="contract">The option contract specification.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Option quote, or null if unavailable.</returns>
    Task<OptionQuote?> GetOptionQuoteAsync(
        OptionContractSpec contract,
        CancellationToken ct = default);

    /// <summary>
    /// Gets option chain capabilities for this provider.
    /// </summary>
    OptionsChainCapabilities Capabilities { get; }

    #region IProviderMetadata Default Implementations

    /// <inheritdoc/>
    string IProviderMetadata.ProviderId => GetType().Name.Replace("OptionsChainProvider", "").ToLowerInvariant();

    /// <inheritdoc/>
    string IProviderMetadata.ProviderDisplayName => GetType().Name.Replace("OptionsChainProvider", " Options");

    /// <inheritdoc/>
    string IProviderMetadata.ProviderDescription => "Options chain data provider";

    /// <inheritdoc/>
    int IProviderMetadata.ProviderPriority => 100;

    /// <inheritdoc/>
    ProviderCapabilities IProviderMetadata.ProviderCapabilities => ProviderCapabilities.OptionsChain();

    #endregion
}

/// <summary>
/// Capability flags for an options chain provider.
/// </summary>
public sealed record OptionsChainCapabilities
{
    /// <summary>Whether the provider supports real-time greeks.</summary>
    public bool SupportsGreeks { get; init; }

    /// <summary>Whether the provider supports open interest data.</summary>
    public bool SupportsOpenInterest { get; init; }

    /// <summary>Whether the provider supports implied volatility.</summary>
    public bool SupportsImpliedVolatility { get; init; }

    /// <summary>Whether the provider supports index options (SPX, NDX, etc.).</summary>
    public bool SupportsIndexOptions { get; init; }

    /// <summary>Whether the provider supports historical option data.</summary>
    public bool SupportsHistorical { get; init; }

    /// <summary>Whether the provider supports streaming option quotes.</summary>
    public bool SupportsStreaming { get; init; }

    /// <summary>Supported underlying asset types.</summary>
    public IReadOnlyList<InstrumentType> SupportedInstrumentTypes { get; init; } =
        new[] { InstrumentType.EquityOption };

    /// <summary>Default capabilities: basic equity options.</summary>
    public static OptionsChainCapabilities Basic { get; } = new()
    {
        SupportsGreeks = false,
        SupportsOpenInterest = true,
        SupportsImpliedVolatility = true,
        SupportsIndexOptions = false,
        SupportsHistorical = false,
        SupportsStreaming = false
    };

    /// <summary>Full-featured provider supporting all option data types.</summary>
    public static OptionsChainCapabilities FullFeatured { get; } = new()
    {
        SupportsGreeks = true,
        SupportsOpenInterest = true,
        SupportsImpliedVolatility = true,
        SupportsIndexOptions = true,
        SupportsHistorical = true,
        SupportsStreaming = true,
        SupportedInstrumentTypes = new[] { InstrumentType.EquityOption, InstrumentType.IndexOption }
    };
}

using System.Globalization;

namespace Meridian.Infrastructure.Adapters.Core;

/// <summary>The provider-declared price basis; an unknown or adjusted basis cannot price an accounting close.</summary>
public enum AccountingClosePriceBasis
{
    Unknown,
    Raw,
    Adjusted
}

/// <summary>
/// One explicitly requested accounting close, separate from a provider's configured research bars.
/// Provider and feed identify the actual serving dataset; the digest identifies the original source
/// record. Returning a price does not grant valuation or posting approval.
/// </summary>
public sealed record AccountingClosePrice(
    string Symbol,
    DateOnly SessionDate,
    decimal Close,
    string Provider,
    string Feed,
    AccountingClosePriceBasis PriceBasis,
    string SourceContentHash)
{
    /// <summary>
    /// Immutable evidence identity carried unchanged through mark evidence and journal fingerprints.
    /// The digest is a source-record hash, not a claim that a fetch proves market-wide completeness.
    /// </summary>
    public string ToEvidenceReference() =>
        $"meridian://accounting-close/v1?provider={Uri.EscapeDataString(Provider)}" +
        $"&feed={Uri.EscapeDataString(Feed)}&symbol={Uri.EscapeDataString(Symbol)}" +
        $"&session={SessionDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}" +
        $"&basis={PriceBasis.ToString().ToLowerInvariant()}&close={Close.ToString("G29", CultureInfo.InvariantCulture)}" +
        $"&sha256={SourceContentHash}";
}

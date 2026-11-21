using System.Text.Json;
using Meridian.Contracts.Domain;

namespace Meridian.Contracts.SecurityMaster;

/// <summary>One listing-identity inconsistency on a stored security.</summary>
/// <param name="Code">Stable rule code: <see cref="SecurityListingIdentity.MalformedPrimaryListingMicCode"/> and its siblings.</param>
/// <param name="FieldPath">The stored field the finding is about.</param>
/// <param name="Message">The operator-facing sentence.</param>
public sealed record SecurityListingIdentityFinding(string Code, string FieldPath, string Message);

/// <summary>
/// The listing identity of a security and its consistency rules (extensibility review D8). Three
/// concepts are kept apart, and none is derived from or flattened into another:
/// <list type="bullet">
/// <item><description><b>Primary listing market</b>: <c>commonTerms.primaryListingMic</c>, an ISO 10383
/// MIC, or failing that the <c>commonTerms.exchange</c> label where the bundled registry resolves it.
/// A property of the security.</description></item>
/// <item><description><b>Listing identifier</b>: a <see cref="SecurityIdentifierKind.ListingId"/>
/// identifier, <c>TICKER:MIC</c>. It identifies one listing; a security listed on several markets
/// holds several, and at most one is the primary identifier at a time.</description></item>
/// <item><description><b>Venue MICs</b>: where something was executed or where an underlying trades
/// (the economic <c>venue.executionVenueMic</c> and <c>depositaryReceipt.underlyingExchangeMic</c>).
/// Attributes of an execution or of the underlying. They are never compared with the listing
/// market, and never identify the security.</description></item>
/// </list>
/// The rules are checks, not write refusals: reads stay tolerant of an exchange label where the MIC
/// is absent, as the stored <c>listing_mic</c> column and migration 048 already are.
/// </summary>
public static class SecurityListingIdentity
{
    /// <summary><c>primaryListingMic</c> is stated but is not a well-formed ISO 10383 MIC.</summary>
    public const string MalformedPrimaryListingMicCode = "LI001";

    /// <summary>The <c>exchange</c> label resolves to a market other than a well-formed <c>primaryListingMic</c>.</summary>
    public const string ExchangeLabelDisagreesCode = "LI002";

    /// <summary>The active primary listing identifier names a market other than the primary listing market.</summary>
    public const string PrimaryListingIdentifierDisagreesCode = "LI003";

    private const string PrimaryListingMicKey = "primaryListingMic";
    private const string ExchangeKey = "exchange";

    /// <summary>
    /// The primary listing market of a record: a well-formed <c>primaryListingMic</c> wins, otherwise
    /// the <c>exchange</c> label where <see cref="MicReference"/> resolves it, otherwise null. This is
    /// the rule the stored <c>listing_mic</c> column is written with.
    /// </summary>
    public static string? ResolvePrimaryListingMic(JsonElement commonTerms)
    {
        if (Mic.TryParse(ReadString(commonTerms, PrimaryListingMicKey), out var primary))
        {
            return primary.Value;
        }

        return MicReference.TryResolve(ReadString(commonTerms, ExchangeKey), out var resolved) ? resolved.Value : null;
    }

    /// <summary>
    /// The listing-identity findings of one stored record at <paramref name="asOf"/>. An identifier is
    /// checked only while it is in force and marked primary: a secondary listing on another market is
    /// not an inconsistency.
    /// </summary>
    public static IReadOnlyList<SecurityListingIdentityFinding> Evaluate(
        JsonElement commonTerms, IReadOnlyList<SecurityIdentifierDto> identifiers, DateTimeOffset asOf)
    {
        ArgumentNullException.ThrowIfNull(identifiers);
        var findings = new List<SecurityListingIdentityFinding>();
        var stated = ReadString(commonTerms, PrimaryListingMicKey);
        var statedIsWellFormed = Mic.TryParse(stated, out var statedMic);
        if (!string.IsNullOrWhiteSpace(stated) && !statedIsWellFormed)
        {
            findings.Add(new(MalformedPrimaryListingMicCode, "commonTerms.primaryListingMic",
                $"primaryListingMic '{stated.Trim()}' is not a four-character ISO 10383 market identifier code."));
        }

        var exchange = ReadString(commonTerms, ExchangeKey);
        if (statedIsWellFormed && MicReference.TryResolve(exchange, out var exchangeMic) && exchangeMic != statedMic)
        {
            findings.Add(new(ExchangeLabelDisagreesCode, "commonTerms.exchange",
                $"exchange '{exchange!.Trim()}' resolves to {exchangeMic.Value}, but primaryListingMic is {statedMic.Value}."));
        }

        if (ResolvePrimaryListingMic(commonTerms) is { } listingMic)
        {
            foreach (var identifier in identifiers)
            {
                if (identifier.Kind != SecurityIdentifierKind.ListingId
                    || !identifier.IsPrimary
                    || identifier.ValidFrom > asOf
                    || (identifier.ValidTo is { } validTo && validTo <= asOf)
                    || !ListingId.TryParse(identifier.Value, out var listing)
                    || string.Equals(listing.Mic.Value, listingMic, StringComparison.Ordinal))
                {
                    continue;
                }

                findings.Add(new(PrimaryListingIdentifierDisagreesCode, "identifiers.ListingId",
                    $"The primary listing identifier '{identifier.Value}' names {listing.Mic.Value}, but the record's primary listing market is {listingMic}."));
            }
        }

        return findings;
    }

    private static string? ReadString(JsonElement json, string propertyName)
        => json.ValueKind == JsonValueKind.Object
           && json.TryGetProperty(propertyName, out var value)
           && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}

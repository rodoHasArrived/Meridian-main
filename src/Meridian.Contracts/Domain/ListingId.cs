namespace Meridian.Contracts.Domain;

/// <summary>
/// A listing identity: a ticker on a specific market, written <c>TICKER:MIC</c> (for example
/// <c>"AAPL:XNAS"</c>, <c>"BRK.B:XNYS"</c>). A ticker alone is ambiguous across markets; the
/// pair is not.
/// </summary>
/// <param name="Ticker">The normalized ticker.</param>
/// <param name="Mic">The ISO 10383 market the ticker is listed on.</param>
public readonly record struct ListingId(SymbolId Ticker, Mic Mic)
{
    /// <summary>The separator between ticker and MIC in the canonical form.</summary>
    public const char Separator = ':';

    /// <summary>
    /// Parses the canonical <c>TICKER:MIC</c> form (case-insensitive). The MIC is taken after the
    /// last separator, so a ticker may itself contain punctuation; neither part may be blank or
    /// carry surrounding whitespace.
    /// </summary>
    public static bool TryParse(string? value, out ListingId listing)
    {
        listing = default;
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        var split = value.LastIndexOf(Separator);
        if (split <= 0 || split == value.Length - 1)
        {
            return false;
        }

        var ticker = value[..split];
        var mic = value[(split + 1)..];
        if (ticker.Trim().Length != ticker.Length || ticker.Any(char.IsWhiteSpace) || mic.Trim().Length != mic.Length
            || !Domain.Mic.TryParse(mic, out var parsedMic))
        {
            return false;
        }

        listing = new ListingId(new SymbolId(ticker), parsedMic);
        return true;
    }

    /// <summary>Parses the canonical form or throws.</summary>
    /// <exception cref="FormatException">Thrown when <paramref name="value"/> is not <c>TICKER:MIC</c>.</exception>
    public static ListingId Parse(string? value)
        => TryParse(value, out var listing)
            ? listing
            : throw new FormatException($"'{value}' is not a listing id of the form TICKER:MIC.");

    /// <inheritdoc/>
    public override string ToString() => $"{Ticker.Value}{Separator}{Mic.Value}";
}

using System.Collections.Frozen;

namespace Meridian.Contracts.Domain;

/// <summary>
/// An ISO 10383 Market Identifier Code: four upper-case letters or digits identifying an
/// operating or segment market (for example <c>"XNAS"</c>, <c>"XNYS"</c>, <c>"ARCX"</c>).
/// </summary>
/// <remarks>
/// Unlike <see cref="VenueCode"/>, which accepts any non-blank venue label, a <see cref="Mic"/> is
/// format-validated on construction. Validity is format-based so that markets missing from the
/// bundled <see cref="MicReference"/> list are not rejected; <see cref="IsRegistered"/> reports
/// whether the code is in that list.
/// </remarks>
public readonly struct Mic : IEquatable<Mic>, IComparable<Mic>
{
    private readonly string _value;

    private Mic(string normalized) => _value = normalized;

    /// <summary>The upper-case four-character code.</summary>
    public string Value => _value ?? string.Empty;

    /// <summary>Whether the code appears in the bundled <see cref="MicReference"/> list.</summary>
    public bool IsRegistered => MicReference.TryGetName(Value, out _);

    /// <summary>
    /// Parses <paramref name="value"/> after trimming and upper-casing it; returns false unless the
    /// result is exactly four letters or digits.
    /// </summary>
    public static bool TryParse(string? value, out Mic mic)
    {
        var normalized = value?.Trim().ToUpperInvariant() ?? string.Empty;
        if (normalized.Length == 4 && normalized.All(static c => c is >= 'A' and <= 'Z' or >= '0' and <= '9'))
        {
            mic = new Mic(normalized);
            return true;
        }

        mic = default;
        return false;
    }

    /// <summary>Parses <paramref name="value"/> or throws when it is not a well-formed MIC.</summary>
    /// <exception cref="FormatException">Thrown when the value is not four letters or digits.</exception>
    public static Mic Parse(string? value)
        => TryParse(value, out var mic)
            ? mic
            : throw new FormatException($"'{value}' is not an ISO 10383 MIC (four letters or digits).");

    /// <summary>Implicitly converts a <see cref="Mic"/> to its code.</summary>
    public static implicit operator string(Mic mic) => mic.Value;

    /// <inheritdoc/>
    public bool Equals(Mic other) => string.Equals(Value, other.Value, StringComparison.Ordinal);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is Mic other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);

    /// <inheritdoc/>
    public int CompareTo(Mic other) => string.CompareOrdinal(Value, other.Value);

    /// <inheritdoc/>
    public override string ToString() => Value;

    /// <inheritdoc cref="Equals(Mic)"/>
    public static bool operator ==(Mic left, Mic right) => left.Equals(right);

    /// <inheritdoc cref="Equals(Mic)"/>
    public static bool operator !=(Mic left, Mic right) => !left.Equals(right);
}

/// <summary>
/// A small bundled subset of the ISO 10383 registry: the markets Meridian's providers and seed
/// data reference, with the common exchange names that map onto each one.
/// </summary>
public static class MicReference
{
    private static readonly FrozenDictionary<string, string> Names = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["XNAS"] = "NASDAQ Stock Market",
        ["XNYS"] = "New York Stock Exchange",
        ["ARCX"] = "NYSE Arca",
        ["XASE"] = "NYSE American",
        ["XCHI"] = "NYSE Chicago",
        ["XBOS"] = "NASDAQ BX",
        ["XPHL"] = "NASDAQ PHLX",
        ["BATS"] = "Cboe BZX Exchange",
        ["BATY"] = "Cboe BYX Exchange",
        ["EDGA"] = "Cboe EDGA Exchange",
        ["EDGX"] = "Cboe EDGX Exchange",
        ["XCBO"] = "Cboe Options Exchange",
        ["IEXG"] = "Investors Exchange",
        ["MEMX"] = "Members Exchange",
        ["MIHI"] = "MIAX Pearl Equities",
        ["LTSE"] = "Long-Term Stock Exchange",
        ["FINN"] = "FINRA ADF",
        ["OTCM"] = "OTC Markets",
        ["XCME"] = "Chicago Mercantile Exchange",
        ["XCBT"] = "Chicago Board of Trade",
        ["XNYM"] = "New York Mercantile Exchange",
        ["IFUS"] = "ICE Futures U.S.",
        ["XLON"] = "London Stock Exchange",
        ["XTSE"] = "Toronto Stock Exchange",
        ["XETR"] = "Xetra",
        ["XPAR"] = "Euronext Paris",
        ["XAMS"] = "Euronext Amsterdam",
        ["XSWX"] = "SIX Swiss Exchange",
        ["XTKS"] = "Tokyo Stock Exchange",
        ["XHKG"] = "Hong Kong Exchanges",
        ["XASX"] = "ASX",
    }.ToFrozenDictionary(StringComparer.Ordinal);

    // Exchange names and legacy codes seen in stored terms and vendor feeds. Only unambiguous
    // names are listed; a routing label such as "SMART" deliberately has no MIC.
    private static readonly FrozenDictionary<string, string> ExchangeNameAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["NASDAQ"] = "XNAS",
        ["NASDAQ GS"] = "XNAS",
        ["NASDAQ GM"] = "XNAS",
        ["NASDAQ CM"] = "XNAS",
        ["NYSE"] = "XNYS",
        ["NYSE ARCA"] = "ARCX",
        ["ARCA"] = "ARCX",
        ["NYSE AMERICAN"] = "XASE",
        ["NYSE MKT"] = "XASE",
        ["AMEX"] = "XASE",
        ["CBOE"] = "XCBO",
        ["IEX"] = "IEXG",
        ["OTC"] = "OTCM",
        ["CME"] = "XCME",
        ["CBOT"] = "XCBT",
        ["NYMEX"] = "XNYM",
        ["LSE"] = "XLON",
        ["TSX"] = "XTSE",
        ["SIX"] = "XSWX",
        ["TSE"] = "XTKS",
        ["HKEX"] = "XHKG",
        ["ASX"] = "XASX",
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>The bundled MIC codes.</summary>
    public static IReadOnlyCollection<string> Codes => Names.Keys;

    /// <summary>The exchange names and legacy codes <see cref="TryResolve"/> maps, keyed case-insensitively.</summary>
    public static IReadOnlyDictionary<string, string> ExchangeNames => ExchangeNameAliases;

    /// <summary>Looks up the market name for a bundled MIC.</summary>
    public static bool TryGetName(string? mic, out string name)
    {
        if (mic is not null && Names.TryGetValue(mic.Trim().ToUpperInvariant(), out var found))
        {
            name = found;
            return true;
        }

        name = string.Empty;
        return false;
    }

    /// <summary>
    /// Resolves an exchange label to a MIC: a bundled MIC is returned as-is, and a known exchange
    /// name or legacy code (for example <c>"NYSE"</c>, <c>"Nasdaq"</c>) is mapped. Anything else,
    /// including an unbundled four-character code, returns false so callers keep the raw label.
    /// </summary>
    public static bool TryResolve(string? exchange, out Mic mic)
    {
        mic = default;
        var label = exchange?.Trim();
        if (string.IsNullOrEmpty(label))
        {
            return false;
        }

        if (Names.ContainsKey(label.ToUpperInvariant()))
        {
            return Mic.TryParse(label, out mic);
        }

        return ExchangeNameAliases.TryGetValue(label, out var code) && Mic.TryParse(code, out mic);
    }
}

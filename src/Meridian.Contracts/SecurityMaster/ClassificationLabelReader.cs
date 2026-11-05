namespace Meridian.Contracts.SecurityMaster;

/// <summary>
/// Read-side decoder for stored Security Master classification labels (asset class, family and
/// sub-type). Writes now persist the F# domain's explicit names (<c>assetClassName</c>,
/// <c>familyName</c>, <c>subTypeName</c>), but events and projections written earlier retain the
/// F# option/union <c>ToString()</c> rendering: <c>Some(PreferredEquity)</c>, <c>None</c>,
/// <c>OtherFamily "x"</c>, <c>OtherSubType "x"</c> and <c>Some(OtherFamily "x")</c>.
/// <para>
/// Retained events and projections are append-only evidence and are never rewritten; consumers
/// that display, match or group a stored label call <see cref="Normalize"/> instead.
/// </para>
/// <para>
/// The decode is structural and single-pass, never a fixed-point loop: at most one <c>Some(…)</c>
/// wrapper is removed, and only around a legacy shape (a known case name, a union payload or a
/// quoted literal). A union payload's string is the free-text label itself and is returned
/// verbatim, so a legacy <c>OtherSubType "Some(Nested)"</c> reads as <c>Some(Nested)</c> and
/// <c>OtherSubType "None"</c> as <c>None</c>. Anything that is not a legacy shape (every canonical
/// label, including free text such as <c>Some(Nested)</c>) is returned trimmed and unchanged, which
/// makes the function idempotent on its outputs that are canonical.
/// </para>
/// <para>
/// A bare <c>OtherFamily</c>/<c>OtherSubType</c> (no payload) is returned unchanged: it is the
/// <see cref="InstrumentTypeDescriptorCatalog"/> placeholder, not a legacy domain rendering.
/// </para>
/// </summary>
public static class ClassificationLabelReader
{
    private static readonly string[] PayloadCasePrefixes = ["OtherFamily", "OtherSubType"];

    /// <summary>
    /// The nullary case names of the F# <c>AssetClass</c>, <c>AssetFamily</c> and
    /// <c>SecuritySubType</c> unions, which is what the retired writer rendered inside
    /// <c>Some(…)</c>. A test pins this set to the F# unions by reflection.
    /// </summary>
    public static IReadOnlySet<string> KnownCaseNames { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        // AssetClass
        "Equity", "FixedIncome", "Fund", "CashEquivalent", "Financing", "Derivative", "PrivateCredit", "Other",
        // AssetFamily
        "Sovereign", "CorporateDebt", "BankProduct", "CommonEquity", "PreferredEquity", "PartnershipEquity",
        "MoneyMarket", "RepurchaseAgreement", "StructuredCash", "SecuritizedCredit", "ListedDerivative", "PrivateLoan",
        // SecuritySubType
        "TreasuryBill", "TreasuryNote", "TreasuryBond", "Tips", "TreasuryFrn", "CommercialPaper", "CertificateOfDeposit",
        "TimeDeposit", "DemandDeposit", "MoneyMarketFund", "Repo", "CashSweep", "CorporateBond", "MunicipalBond",
        "CommonShare", "PreferredShare", "Adr", "MlpUnit", "LimitedPartnershipInterest", "ReitShare", "OptionContract",
        "FutureContract", "SwapContract", "DirectLoan", "StructuredCredit", "PrivateCompanyEquity", "RealEstateHolding",
        "CommitmentGuarantee",
    };

    /// <summary>
    /// Returns the canonical label for a stored classification label, decoding legacy F#
    /// option/union renderings. <c>null</c>, whitespace and a legacy option <c>None</c> read as
    /// <c>null</c>; anything else that is not a legacy rendering is returned trimmed.
    /// </summary>
    public static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var current = value.Trim();
        if (current.Equals("None", StringComparison.Ordinal))
        {
            // The legacy rendering of an absent optional family.
            return null;
        }

        if (current.Length > "Some()".Length
            && current.StartsWith("Some(", StringComparison.Ordinal)
            && current.EndsWith(')'))
        {
            var inner = current["Some(".Length..^1].Trim();
            if (inner.Equals("None", StringComparison.Ordinal))
            {
                return null;
            }

            if (TryDecodeUnionPayload(inner, out var payload) || TryUnquote(inner, out payload))
            {
                return payload;
            }

            if (KnownCaseNames.Contains(inner))
            {
                return inner;
            }

            // Not a legacy shape inside the wrapper: canonical free text such as "Some(Nested)".
            return current;
        }

        if (TryDecodeUnionPayload(current, out var unionPayload) || TryUnquote(current, out unionPayload))
        {
            return unionPayload;
        }

        return current;
    }

    /// <summary>
    /// True when <paramref name="value"/> is free text the decoder would change, because it looks like
    /// a retired rendering. New writes refuse such text so stored labels stay unambiguous.
    /// </summary>
    public static bool IsAmbiguousFreeText(string? value)
        => !string.IsNullOrWhiteSpace(value)
           && !string.Equals(Normalize(value), value.Trim(), StringComparison.Ordinal);

    /// <summary>
    /// Normalizes a label for a non-optional field (asset class, sub-type), keeping the stored
    /// value when the decoder would produce no label rather than turning a required field null.
    /// </summary>
    public static string NormalizeRequired(string value)
        => Normalize(value) ?? value;

    /// <summary>
    /// Returns a read-side copy of <paramref name="record"/> whose asset-class, family and sub-type
    /// labels are canonical. The record's classification JSON and every other field are kept as
    /// stored; the original instance is returned when no label changes.
    /// </summary>
    public static SecurityEconomicDefinitionRecord NormalizeLabels(SecurityEconomicDefinitionRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        var assetClass = NormalizeRequired(record.AssetClass);
        var assetFamily = Normalize(record.AssetFamily);
        var subType = NormalizeRequired(record.SubType);

        return string.Equals(assetClass, record.AssetClass, StringComparison.Ordinal)
               && string.Equals(assetFamily, record.AssetFamily, StringComparison.Ordinal)
               && string.Equals(subType, record.SubType, StringComparison.Ordinal)
            ? record
            : record with { AssetClass = assetClass, AssetFamily = assetFamily, SubType = subType };
    }

    /// <summary>
    /// Decodes <c>OtherFamily "x"</c>, <c>OtherSubType "x"</c> or the parenthesised
    /// <c>OtherFamily("x")</c> into the literal <c>x</c>, returned verbatim.
    /// </summary>
    private static bool TryDecodeUnionPayload(string value, out string payload)
    {
        foreach (var prefix in PayloadCasePrefixes)
        {
            if (value.Length <= prefix.Length || !value.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            var rest = value[prefix.Length..];
            if (rest.Length > 2 && rest[0] == '(' && rest[^1] == ')')
            {
                rest = rest[1..^1];
            }
            else if (char.IsWhiteSpace(rest[0]))
            {
                rest = rest.TrimStart();
            }
            else
            {
                continue;
            }

            if (TryUnquote(rest.Trim(), out payload))
            {
                return true;
            }
        }

        payload = string.Empty;
        return false;
    }

    private static bool TryUnquote(string value, out string unquoted)
    {
        if (value.Length < 2 || value[0] != '"' || value[^1] != '"')
        {
            unquoted = string.Empty;
            return false;
        }

        var body = value[1..^1];
        unquoted = body.Contains('\\', StringComparison.Ordinal)
            ? body.Replace("\\\"", "\"", StringComparison.Ordinal).Replace("\\\\", "\\", StringComparison.Ordinal)
            : body;
        return true;
    }
}

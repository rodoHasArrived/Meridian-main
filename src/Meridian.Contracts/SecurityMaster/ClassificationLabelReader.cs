namespace Meridian.Contracts.SecurityMaster;

/// <summary>
/// Read-side decoder for stored Security Master classification labels (asset class, family and
/// sub-type). Writes now persist the F# domain's explicit names (<c>assetClassName</c>,
/// <c>familyName</c>, <c>subTypeName</c>), but events and projections written earlier retain the
/// F# option/union <c>ToString()</c> rendering: <c>Some(PreferredEquity)</c>,
/// <c>OtherFamily "x"</c>, <c>OtherSubType "x"</c> and nested <c>Some(OtherFamily "x")</c>.
/// <para>
/// Retained events and projections are append-only evidence and are never rewritten; consumers
/// that display, match or group a stored label call <see cref="Normalize"/> instead. The decoder
/// only unwraps those legacy renderings and otherwise returns the trimmed input unchanged, so every
/// current canonical label is a fixed point and the function is idempotent.
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
    /// Returns the canonical label for a stored classification label, decoding legacy F#
    /// option/union renderings. <c>null</c>, whitespace and a legacy <c>None</c> read as
    /// <c>null</c>; anything else that is not a legacy rendering is returned trimmed.
    /// </summary>
    public static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var current = value.Trim();

        // Each pass removes at least one wrapper, so the loop terminates; decoding to a fixed point
        // is what makes Normalize(Normalize(x)) == Normalize(x).
        while (true)
        {
            if (current.Equals("None", StringComparison.Ordinal))
            {
                return null;
            }

            if (TryUnwrap(current, out var inner))
            {
                if (string.IsNullOrWhiteSpace(inner))
                {
                    return null;
                }

                current = inner.Trim();
                continue;
            }

            return current;
        }
    }

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

    private static bool TryUnwrap(string value, out string inner)
    {
        // F# option: Some(X), Some("X"), Some(OtherFamily "x").
        if (value.Length > "Some()".Length
            && value.StartsWith("Some(", StringComparison.Ordinal)
            && value.EndsWith(')'))
        {
            inner = value["Some(".Length..^1];
            return true;
        }

        // F# string literal rendering: "X" with \" and \\ escapes.
        if (TryUnquote(value, out inner))
        {
            return true;
        }

        // F# union case with a string payload: OtherFamily "x", OtherSubType "x" (or the
        // parenthesized OtherFamily("x") form).
        foreach (var prefix in PayloadCasePrefixes)
        {
            if (value.Length <= prefix.Length || !value.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            var payload = value[prefix.Length..];
            if (payload.Length > 2 && payload[0] == '(' && payload[^1] == ')')
            {
                payload = payload[1..^1];
            }
            else if (char.IsWhiteSpace(payload[0]))
            {
                payload = payload.TrimStart();
            }
            else
            {
                continue;
            }

            if (TryUnquote(payload.Trim(), out inner))
            {
                return true;
            }
        }

        inner = string.Empty;
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

namespace Meridian.Reporting;

/// <summary>
/// Shared rule for reading bare (unbraced) formula references that contain hyphens. Hyphens are
/// legal inside field names, so <c>net-pnl</c> can be one field; but operators also write
/// <c>pnl-fees</c> meaning subtraction. The longest known name wins: the whole identifier when it
/// is a known field, otherwise the longest hyphen-delimited prefix that is known, with the rest left
/// for the caller to read as a subtraction. An identifier with no known prefix is returned whole so
/// the "not found" message still names what the operator typed. Braced references
/// (<c>{net-pnl}</c>) are never split.
/// </summary>
public static class ReportWriterFormulaReferences
{
    /// <summary>
    /// Returns how many leading characters of <paramref name="identifier"/> form the reference.
    /// </summary>
    public static int ResolveReferenceLength(string identifier, Func<string, bool> isKnown)
    {
        ArgumentNullException.ThrowIfNull(identifier);
        ArgumentNullException.ThrowIfNull(isKnown);

        if (!identifier.Contains('-') || isKnown(identifier))
        {
            return identifier.Length;
        }

        for (var split = identifier.LastIndexOf('-'); split > 0; split = identifier.LastIndexOf('-', split - 1))
        {
            if (isKnown(identifier[..split]))
            {
                return split;
            }
        }

        return identifier.Length;
    }
}

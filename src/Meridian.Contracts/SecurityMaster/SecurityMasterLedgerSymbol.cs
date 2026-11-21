namespace Meridian.Contracts.SecurityMaster;

/// <summary>Resolves one dated ticker for instrument-bearing native ledger lines.</summary>
public static class SecurityMasterLedgerSymbol
{
    public static string? ResolveUniqueActiveTicker(IReadOnlyList<SecurityIdentifierDto> identifiers, DateTimeOffset asOfUtc)
    {
        ArgumentNullException.ThrowIfNull(identifiers);
        var symbols = identifiers.Where(identifier => identifier.Kind == SecurityIdentifierKind.Ticker
                && identifier.ValidFrom <= asOfUtc && (!identifier.ValidTo.HasValue || identifier.ValidTo.Value > asOfUtc)
                && !string.IsNullOrWhiteSpace(identifier.Value))
            .Select(identifier => identifier.Value.Trim().ToUpperInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(2).ToArray();
        // These delimiters cannot be represented by the existing native lineage grammar.
        return symbols.Length == 1 && symbols[0].IndexOfAny([':', '|', ';', ',', '\r', '\n']) < 0 ? symbols[0] : null;
    }
}

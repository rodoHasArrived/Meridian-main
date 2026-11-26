namespace Meridian.Ledger;

/// <summary>
/// The instrument key for a per-security ledger account (Securities, Short Securities Payable,
/// Dividend Receivable, Accrued Interest Receivable).
/// </summary>
/// <remarks>
/// Owner decision 2026-11-25 (Security Master architecture review F2): per-security accounts are
/// keyed by Security Master <see cref="SecurityId"/>, with the ticker kept only as a display
/// attribute, so a ticker rename keeps one account history. <see cref="Unresolved"/> is the legacy
/// ticker key; posting sites use it only when the Security Master has no record for the ticker
/// (typed NotFound). Such accounts are listed by <see cref="LedgerSecurityKeyMigration"/> so they
/// can be reclassed once the security is registered. An ambiguous ticker is never posted.
/// </remarks>
public readonly record struct LedgerSecurityKey
{
    private LedgerSecurityKey(Guid? securityId, string? symbol)
    {
        SecurityId = securityId;
        Symbol = symbol;
    }

    /// <summary>The Security Master identity, or <see langword="null"/> for a legacy ticker key.</summary>
    public Guid? SecurityId { get; }

    /// <summary>The normalized ticker: display-only for resolved keys, the key itself for legacy keys.</summary>
    public string? Symbol { get; }

    /// <summary><see langword="true"/> when the key is a Security Master identity.</summary>
    public bool IsResolved => SecurityId.HasValue;

    /// <summary>Keys an account by <paramref name="securityId"/>, retaining <paramref name="displaySymbol"/> for display.</summary>
    public static LedgerSecurityKey Resolved(Guid securityId, string? displaySymbol = null)
    {
        if (securityId == Guid.Empty)
            throw new ArgumentException("A Security Master identity is required.", nameof(securityId));

        return new(securityId, NormalizeOptionalSymbol(displaySymbol));
    }

    /// <summary>
    /// Legacy ticker key for a holding the Security Master cannot resolve (typed NotFound).
    /// Do not use for an ambiguous ticker: that must fail closed.
    /// </summary>
    public static LedgerSecurityKey Unresolved(string symbol) => new(null, NormalizeSymbol(symbol));

    /// <summary>Resolved when <paramref name="securityId"/> is present, otherwise the legacy ticker key.</summary>
    public static LedgerSecurityKey From(Guid? securityId, string symbol)
        => securityId is { } id && id != Guid.Empty ? Resolved(id, symbol) : Unresolved(symbol);

    /// <summary>Reads the key from a per-security account.</summary>
    public static LedgerSecurityKey FromAccount(LedgerAccount account)
    {
        ArgumentNullException.ThrowIfNull(account);
        if (account.SecurityId is { } id)
            return Resolved(id, account.Symbol);
        if (account.Symbol is null)
            throw new ArgumentException($"Account '{account}' is not a per-security account.", nameof(account));
        return Unresolved(account.Symbol);
    }

    /// <inheritdoc/>
    public bool Equals(LedgerSecurityKey other)
        => SecurityId == other.SecurityId && (SecurityId.HasValue || Symbol == other.Symbol);

    /// <inheritdoc/>
    public override int GetHashCode()
        => SecurityId.HasValue ? SecurityId.Value.GetHashCode() : Symbol?.GetHashCode() ?? 0;

    /// <inheritdoc/>
    public override string ToString()
        => SecurityId is { } id ? $"{Symbol ?? "?"} <{id:D}>" : Symbol ?? string.Empty;

    internal static string NormalizeSymbol(string symbol)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        return symbol.Trim().ToUpperInvariant();
    }

    private static string? NormalizeOptionalSymbol(string? symbol)
        => string.IsNullOrWhiteSpace(symbol) ? null : symbol.Trim().ToUpperInvariant();
}

namespace Meridian.Contracts.Workstation;

/// <summary>
/// Canonical parser for retained Evidence Vault references shared by strategy execution and
/// promotion governance. The parser deliberately accepts only production vault identifiers and
/// does not rely on <see cref="Uri.AbsolutePath"/>, whose path normalization can hide encoded
/// traversal segments.
/// </summary>
public static class EvidenceVaultReference
{
    public const string Scheme = "evidence";
    public const string Authority = "evidence-vault";
    public const string CanonicalPrefix = "evidence://evidence-vault/";
    public const int VaultIdHexLength = 24;
    public const int VaultIdLength = 3 + VaultIdHexLength;

    /// <summary>
    /// Parses an exact <c>evidence://evidence-vault/{vaultId}</c> reference. The returned vault id
    /// is normalized to the lowercase form used by the file-backed Evidence Vault index.
    /// </summary>
    public static bool TryParseCanonical(string? reference, out string vaultId) =>
        TryParseCanonical(reference, out vaultId, out _);

    /// <summary>
    /// Parses a canonical reference and reports whether the input targeted the Evidence Vault
    /// authority even when another canonical constraint was invalid.
    /// </summary>
    public static bool TryParseCanonical(
        string? reference,
        out string vaultId,
        out bool targetsEvidenceVault)
    {
        vaultId = string.Empty;
        targetsEvidenceVault = false;
        var raw = reference?.Trim() ?? string.Empty;

        if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri) ||
            !Uri.IsWellFormedUriString(raw, UriKind.Absolute))
        {
            targetsEvidenceVault = LooksLikeEvidenceVaultTarget(raw);
            return false;
        }

        targetsEvidenceVault =
            string.Equals(uri.Scheme, Scheme, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(uri.Host, Authority, StringComparison.OrdinalIgnoreCase);
        if (!targetsEvidenceVault)
        {
            return false;
        }

        if (!string.IsNullOrEmpty(uri.UserInfo) ||
            uri.Port != -1 ||
            raw.Contains('?') ||
            raw.Contains('#'))
        {
            return false;
        }

        var schemeSeparator = raw.IndexOf("://", StringComparison.Ordinal);
        var authorityStart = schemeSeparator + 3;
        var pathStart = raw.IndexOf('/', authorityStart);
        if (schemeSeparator <= 0 ||
            pathStart < 0 ||
            !string.Equals(
                raw[authorityStart..pathStart],
                Authority,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var rawVaultId = raw[(pathStart + 1)..];
        if (rawVaultId.Length == 0 || rawVaultId.Contains('/') || rawVaultId.Contains('\\'))
        {
            return false;
        }

        return TryNormalizeVaultId(rawVaultId, out vaultId);
    }

    /// <summary>Validates and normalizes a production <c>ev-</c> plus 24-hex vault identifier.</summary>
    public static bool TryNormalizeVaultId(string? value, out string vaultId)
    {
        vaultId = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var candidate = value.Trim();
        if (candidate.Length != VaultIdLength ||
            !candidate.StartsWith("ev-", StringComparison.OrdinalIgnoreCase) ||
            !candidate[3..].All(static ch =>
                ch is (>= '0' and <= '9') or
                    (>= 'a' and <= 'f') or
                    (>= 'A' and <= 'F')))
        {
            return false;
        }

        vaultId = $"ev-{candidate[3..].ToLowerInvariant()}";
        return true;
    }

    private static bool LooksLikeEvidenceVaultTarget(string raw)
    {
        const string authorityPrefix = "evidence://evidence-vault";
        if (!raw.StartsWith(authorityPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (raw.Length == authorityPrefix.Length)
        {
            return true;
        }

        return raw[authorityPrefix.Length] is '/' or '\\' or ':' or '@' or '?' or '#' or '%';
    }
}

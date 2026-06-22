using System.Text.RegularExpressions;

namespace Meridian.Infrastructure.Etl.Sftp;

/// <summary>Connection settings required to establish an SFTP session safely.</summary>
public sealed partial record SftpConnectionOptions(
    string Host,
    int Port,
    string Username,
    string Password,
    string HostKeySha256Fingerprint)
{
    public static SftpConnectionOptions Create(
        string host,
        int port,
        string username,
        string password,
        string? hostKeySha256Fingerprint)
    {
        if (string.IsNullOrWhiteSpace(host))
            throw new InvalidOperationException("SFTP host is required.");

        if (port <= 0 || port > 65535)
            throw new InvalidOperationException("SFTP port must be between 1 and 65535.");

        if (string.IsNullOrWhiteSpace(username))
            throw new InvalidOperationException("SFTP username is required.");

        if (string.IsNullOrEmpty(password))
            throw new InvalidOperationException("SFTP secretRef must contain the password in v1.");

        var normalizedFingerprint = NormalizeSha256Fingerprint(hostKeySha256Fingerprint);
        if (normalizedFingerprint is null)
        {
            throw new InvalidOperationException(
                "SFTP hostKeySha256Fingerprint is required so the server host key can be pinned before transfer.");
        }

        return new SftpConnectionOptions(host.Trim(), port, username, password, normalizedFingerprint);
    }

    public static string? NormalizeSha256Fingerprint(string? fingerprint)
    {
        if (string.IsNullOrWhiteSpace(fingerprint))
            return null;

        var value = fingerprint.Trim();
        if (value.StartsWith("SHA256:", StringComparison.OrdinalIgnoreCase))
            value = value[7..];

        value = value.Replace(":", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal);
        return Sha256FingerprintRegex().IsMatch(value) ? value.ToUpperInvariant() : null;
    }

    [GeneratedRegex("^[A-Fa-f0-9]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex Sha256FingerprintRegex();
}

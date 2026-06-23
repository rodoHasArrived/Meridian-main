using Meridian.Contracts.Etl;

namespace Meridian.Infrastructure.Etl.Sftp;

public sealed record SftpCredentialMaterial(string Username, string Password);

public interface ISftpCredentialResolver
{
    ValueTask<SftpCredentialMaterial> ResolveAsync(EtlSourceDefinition source, CancellationToken ct = default);
}

public sealed class EnvironmentSftpCredentialResolver : ISftpCredentialResolver
{
    public ValueTask<SftpCredentialMaterial> ResolveAsync(EtlSourceDefinition source, CancellationToken ct = default)
    {
        _ = ct;
        if (string.IsNullOrWhiteSpace(source.Username))
            throw new InvalidOperationException("SFTP username is required.");

        if (string.IsNullOrWhiteSpace(source.SecretRef))
            throw new InvalidOperationException("SFTP secretRef is required.");

        var secretRef = source.SecretRef.Trim();
        var password = secretRef.StartsWith("env:", StringComparison.OrdinalIgnoreCase)
            ? Environment.GetEnvironmentVariable(secretRef[4..])
            : secretRef;

        if (string.IsNullOrEmpty(password))
            throw new InvalidOperationException($"SFTP secretRef '{source.SecretRef}' did not resolve to a password.");

        return ValueTask.FromResult(new SftpCredentialMaterial(source.Username.Trim(), password));
    }
}

public sealed record SftpCapabilityStatus(
    bool RealSftpEnabled,
    bool SourceKindIsSftp,
    bool HasSftpUri,
    bool HasUsername,
    bool HasSecretRef,
    bool HasHostKeyFingerprint,
    bool Ready,
    IReadOnlyList<string> Issues);

public interface ISftpCapabilityService
{
    SftpCapabilityStatus Evaluate(EtlSourceDefinition source);
}

public sealed class SftpCapabilityService : ISftpCapabilityService
{
    public SftpCapabilityStatus Evaluate(EtlSourceDefinition source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var issues = new List<string>();
        var hasSftpUri = Uri.TryCreate(source.Location, UriKind.Absolute, out var uri) &&
            string.Equals(uri.Scheme, "sftp", StringComparison.OrdinalIgnoreCase);
        if (source.Kind != EtlSourceKind.Sftp)
            issues.Add("Source kind is not SFTP.");
        if (!hasSftpUri)
            issues.Add("SFTP source location must be a full sftp:// URI.");
        if (string.IsNullOrWhiteSpace(source.Username))
            issues.Add("SFTP username is missing.");
        if (string.IsNullOrWhiteSpace(source.SecretRef))
            issues.Add("SFTP secretRef is missing.");
        if (string.IsNullOrWhiteSpace(source.HostKeySha256Fingerprint))
            issues.Add("SFTP hostKeySha256Fingerprint is missing.");
#if SFTP
        var enabled = true;
#else
        var enabled = false;
        issues.Add("Real SFTP support is disabled in this build. Build with /p:EnableSftp=true.");
#endif
        return new SftpCapabilityStatus(
            enabled,
            source.Kind == EtlSourceKind.Sftp,
            hasSftpUri,
            !string.IsNullOrWhiteSpace(source.Username),
            !string.IsNullOrWhiteSpace(source.SecretRef),
            !string.IsNullOrWhiteSpace(source.HostKeySha256Fingerprint),
            issues.Count == 0,
            issues);
    }
}

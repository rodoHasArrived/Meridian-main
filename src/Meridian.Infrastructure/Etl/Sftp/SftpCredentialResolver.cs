using Meridian.Contracts.Etl;

namespace Meridian.Infrastructure.Etl.Sftp;

public sealed record SftpCredentialMaterial(string Username, string Password);

public interface ISftpCredentialResolver
{
    ValueTask<SftpCredentialMaterial> ResolveAsync(EtlSourceDefinition source, CancellationToken ct = default);

    /// <summary>
    /// Resolves destination credentials through the same secret model as a source.
    /// </summary>
    /// <remarks>
    /// Publishing previously passed <c>destination.SecretRef</c> straight through as the
    /// password, so an <c>env:</c> reference that resolved correctly on the read side was sent
    /// verbatim on the write side — the literal text "env:SFTP_PASSWORD" became the password.
    /// Source and destination must resolve secrets identically.
    /// </remarks>
    ValueTask<SftpCredentialMaterial> ResolveAsync(EtlDestinationDefinition destination, CancellationToken ct = default);
}

public sealed class EnvironmentSftpCredentialResolver : ISftpCredentialResolver
{
    public ValueTask<SftpCredentialMaterial> ResolveAsync(EtlSourceDefinition source, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        _ = ct;
        return ValueTask.FromResult(Resolve(source.Username, source.SecretRef, "source"));
    }

    public ValueTask<SftpCredentialMaterial> ResolveAsync(EtlDestinationDefinition destination, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        _ = ct;
        return ValueTask.FromResult(Resolve(destination.Username, destination.SecretRef, "destination"));
    }

    private static SftpCredentialMaterial Resolve(string? username, string? secretRef, string role)
    {
        if (string.IsNullOrWhiteSpace(username))
            throw new InvalidOperationException($"SFTP {role} username is required.");

        if (string.IsNullOrWhiteSpace(secretRef))
            throw new InvalidOperationException($"SFTP {role} secretRef is required.");

        var trimmed = secretRef.Trim();
        var password = trimmed.StartsWith("env:", StringComparison.OrdinalIgnoreCase)
            ? Environment.GetEnvironmentVariable(trimmed[4..])
            : trimmed;

        if (string.IsNullOrEmpty(password))
            throw new InvalidOperationException($"SFTP {role} secretRef '{secretRef}' did not resolve to a password.");

        return new SftpCredentialMaterial(username.Trim(), password);
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
    /// <summary>Gets whether this build contains a real SFTP client rather than the disabled stub.</summary>
    bool RealSftpEnabled { get; }

    SftpCapabilityStatus Evaluate(EtlSourceDefinition source);

    /// <summary>
    /// Evaluates an SFTP publishing destination against the same readiness rules as a source.
    /// </summary>
    SftpCapabilityStatus Evaluate(EtlDestinationDefinition destination);
}

public sealed class SftpCapabilityService : ISftpCapabilityService
{
#if SFTP
    public bool RealSftpEnabled => true;
#else
    public bool RealSftpEnabled => false;
#endif

    public SftpCapabilityStatus Evaluate(EtlSourceDefinition source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return Evaluate(
            kindIsSftp: source.Kind == EtlSourceKind.Sftp,
            kindIssue: "Source kind is not SFTP.",
            role: "source",
            location: source.Location,
            username: source.Username,
            secretRef: source.SecretRef,
            hostKeyFingerprint: source.HostKeySha256Fingerprint);
    }

    public SftpCapabilityStatus Evaluate(EtlDestinationDefinition destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        return Evaluate(
            kindIsSftp: destination.Kind == EtlDestinationKind.Sftp,
            kindIssue: "Destination kind is not SFTP.",
            role: "destination",
            location: destination.Location,
            username: destination.Username,
            secretRef: destination.SecretRef,
            hostKeyFingerprint: destination.HostKeySha256Fingerprint);
    }

    private SftpCapabilityStatus Evaluate(
        bool kindIsSftp,
        string kindIssue,
        string role,
        string? location,
        string? username,
        string? secretRef,
        string? hostKeyFingerprint)
    {
        var issues = new List<string>();
        var hasSftpUri = Uri.TryCreate(location, UriKind.Absolute, out var uri) &&
            string.Equals(uri.Scheme, "sftp", StringComparison.OrdinalIgnoreCase);
        if (!kindIsSftp)
            issues.Add(kindIssue);
        if (!hasSftpUri)
            issues.Add($"SFTP {role} location must be a full sftp:// URI.");
        if (string.IsNullOrWhiteSpace(username))
            issues.Add("SFTP username is missing.");
        if (string.IsNullOrWhiteSpace(secretRef))
            issues.Add("SFTP secretRef is missing.");
        if (string.IsNullOrWhiteSpace(hostKeyFingerprint))
            issues.Add("SFTP hostKeySha256Fingerprint is missing.");
        if (!RealSftpEnabled)
            issues.Add("Real SFTP support is disabled in this build. Build with /p:EnableSftp=true.");

        return new SftpCapabilityStatus(
            RealSftpEnabled,
            kindIsSftp,
            hasSftpUri,
            !string.IsNullOrWhiteSpace(username),
            !string.IsNullOrWhiteSpace(secretRef),
            !string.IsNullOrWhiteSpace(hostKeyFingerprint),
            issues.Count == 0,
            issues);
    }
}

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

        // Trim only to recognise and parse an `env:` reference; a literal secret is returned
        // exactly as configured. `env: MERIDIAN_SFTP_PASSWORD` reads naturally in YAML, and
        // Evaluate accepts it, but passing the leading space to GetEnvironmentVariable looked
        // up a different name and failed after the destination had already been reported
        // ready — an accepted-then-broken export. Trimming a *literal* password is the same
        // class of defect pointed the other way: it silently substitutes a different
        // credential, and leaves a password whose significant characters include leading or
        // trailing whitespace impossible to express. Publishing passed destination.SecretRef
        // through verbatim before destinations moved onto this path, so trimming here would
        // have broken exports that authenticate today.
        var trimmed = secretRef.Trim();
        var password = trimmed.StartsWith("env:", StringComparison.OrdinalIgnoreCase)
            ? Environment.GetEnvironmentVariable(trimmed[4..].Trim())
            : secretRef;

        if (string.IsNullOrEmpty(password))
            throw new InvalidOperationException($"SFTP {role} secretRef '{secretRef}' did not resolve to a password.");

        // Verbatim for the same reason as the password above: the publisher passed
        // destination.Username straight through, so trimming it here would silently
        // authenticate a destination that works today as a different account.
        return new SftpCredentialMaterial(username, password);
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
        // Delegate to the parser the transfer path actually uses rather than re-checking the
        // scheme here. A scheme-only test accepted sftp://host/, sftp://user@host/drop, and URIs
        // carrying a query or fragment, all of which SftpRemoteLocation.ParseRequired rejects the
        // moment publishing starts — so readiness approved a destination that could never
        // connect, which is the accepted-then-broken export this preflight exists to prevent.
        // Sharing one implementation also stops the two rule sets from drifting apart.
        var hasSftpUri = true;
        string? locationIssue = null;
        try
        {
            SftpRemoteLocation.ParseRequired(location, role);
        }
        catch (InvalidOperationException ex)
        {
            hasSftpUri = false;
            locationIssue = ex.Message;
        }

        if (!kindIsSftp)
            issues.Add(kindIssue);
        if (locationIssue is not null)
            issues.Add(locationIssue);
        if (string.IsNullOrWhiteSpace(username))
            issues.Add("SFTP username is missing.");
        // Presence is not enough for an `env:` reference: EnvironmentSftpCredentialResolver
        // throws when the named variable is absent or empty, so a destination naming a unset
        // variable would report Ready and then fail before connecting.
        var hasSecretRef = !string.IsNullOrWhiteSpace(secretRef);
        if (!hasSecretRef)
        {
            issues.Add("SFTP secretRef is missing.");
        }
        else
        {
            var trimmed = secretRef.Trim();
            if (trimmed.StartsWith("env:", StringComparison.OrdinalIgnoreCase))
            {
                var variable = trimmed[4..].Trim();
                if (variable.Length == 0)
                {
                    issues.Add("SFTP secretRef 'env:' reference names no environment variable.");
                    hasSecretRef = false;
                }
                else if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(variable)))
                {
                    issues.Add($"SFTP secretRef environment variable '{variable}' is unset or empty.");
                    hasSecretRef = false;
                }
            }
        }
        // Presence is not enough: SftpConnectionOptions.Create rejects a fingerprint that
        // NormalizeSha256Fingerprint cannot parse, so a non-blank but malformed value would
        // report Ready and then fail before the connection was attempted — exactly the
        // accepted-then-broken export this preflight exists to prevent.
        var hasFingerprint = !string.IsNullOrWhiteSpace(hostKeyFingerprint);
        if (!hasFingerprint)
        {
            issues.Add("SFTP hostKeySha256Fingerprint is missing.");
        }
        else if (SftpConnectionOptions.NormalizeSha256Fingerprint(hostKeyFingerprint) is null)
        {
            issues.Add(
                "SFTP hostKeySha256Fingerprint is not a valid SHA-256 host key fingerprint "
                + "(expected 64 hex characters or an OpenSSH 'SHA256:<base64>' value).");
            hasFingerprint = false;
        }

        if (!RealSftpEnabled)
            issues.Add("Real SFTP support is disabled in this build. Build with /p:EnableSftp=true.");

        return new SftpCapabilityStatus(
            RealSftpEnabled,
            kindIsSftp,
            hasSftpUri,
            !string.IsNullOrWhiteSpace(username),
            hasSecretRef,
            hasFingerprint,
            issues.Count == 0,
            issues);
    }
}

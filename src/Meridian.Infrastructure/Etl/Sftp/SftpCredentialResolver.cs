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
        // trimLiteral: the source resolver has always trimmed a literal username and secret.
        return ValueTask.FromResult(Resolve(source.Username, source.SecretRef, "source", trimLiteral: true));
    }

    public ValueTask<SftpCredentialMaterial> ResolveAsync(EtlDestinationDefinition destination, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        _ = ct;
        // trimLiteral: SftpFilePublisher passed destination.Username and destination.SecretRef
        // to the client verbatim, so destinations must keep seeing them verbatim.
        return ValueTask.FromResult(Resolve(destination.Username, destination.SecretRef, "destination", trimLiteral: false));
    }

    /// <summary>
    /// Resolves credential material for one role.
    /// </summary>
    /// <param name="trimLiteral">
    /// Whether a *literal* username/secret is trimmed. This differs by role on purpose, and the
    /// asymmetry is compatibility rather than design: the two roles normalised differently before
    /// they shared this resolver — sources trimmed, destinations passed through verbatim — so any
    /// single behaviour silently re-authenticates one role's working configurations as different
    /// credentials. That is unacceptable for a change whose stated purpose is to certify the SFTP
    /// capability, so each role keeps what it had.
    ///
    /// What is genuinely unified is the part that was broken: `env:` references now resolve
    /// identically for both, which is the defect this work exists to fix — publishing used to send
    /// the literal text "env:SFTP_PASSWORD" as the password. Collapsing the two literal behaviours
    /// into one is a deliberate migration with an operator-visible failure mode, not a cleanup to
    /// fold into this change.
    /// </param>
    private static SftpCredentialMaterial Resolve(string? username, string? secretRef, string role, bool trimLiteral)
    {
        if (string.IsNullOrWhiteSpace(username))
            throw new InvalidOperationException($"SFTP {role} username is required.");

        if (string.IsNullOrWhiteSpace(secretRef))
            throw new InvalidOperationException($"SFTP {role} secretRef is required.");

        // The `env:` prefix is always detected and parsed on trimmed text, for both roles.
        // `env: MERIDIAN_SFTP_PASSWORD` reads naturally in YAML, and Evaluate accepts it, but
        // passing the leading space to GetEnvironmentVariable looked up a different name and
        // failed after the destination had already been reported ready — an accepted-then-broken
        // export. The environment variable's *value* is never trimmed: the operator who set it
        // chose its bytes.
        var trimmed = secretRef.Trim();
        var password = trimmed.StartsWith("env:", StringComparison.OrdinalIgnoreCase)
            ? Environment.GetEnvironmentVariable(trimmed[4..].Trim())
            : (trimLiteral ? trimmed : secretRef);

        if (string.IsNullOrEmpty(password))
            throw new InvalidOperationException($"SFTP {role} secretRef '{secretRef}' did not resolve to a password.");

        return new SftpCredentialMaterial(trimLiteral ? username.Trim() : username, password);
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

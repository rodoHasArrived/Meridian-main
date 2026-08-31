using Meridian.Contracts.Integrations;

namespace Meridian.Application.Integrations;

/// <summary>
/// A single field-level problem found while validating a provider integration setup draft.
/// <paramref name="Field"/> uses camelCase request paths (e.g. <c>connection.credentialSecretRef</c>)
/// so API consumers can map issues back onto form fields.
/// </summary>
public sealed record ProviderIntegrationSetupValidationIssue(
    string Field,
    string Code,
    string Message);

/// <summary>
/// Thrown when a provider integration setup draft fails validation. Carries every issue found
/// so callers can surface all problems in one round trip instead of fixing them one at a time.
/// Derives from <see cref="InvalidOperationException"/> to preserve the setup endpoint's
/// established 400 mapping.
/// </summary>
public sealed class ProviderIntegrationSetupValidationException : InvalidOperationException
{
    public ProviderIntegrationSetupValidationException(
        IReadOnlyList<ProviderIntegrationSetupValidationIssue> issues)
        : base(BuildMessage(issues))
    {
        Issues = issues;
    }

    public IReadOnlyList<ProviderIntegrationSetupValidationIssue> Issues { get; }

    private static string BuildMessage(IReadOnlyList<ProviderIntegrationSetupValidationIssue> issues)
        => issues.Count == 0
            ? "Provider integration setup draft failed validation."
            : "Provider integration setup draft failed validation: "
                + string.Join(" ", issues.Select(issue => issue.Message));
}

/// <summary>
/// Validates provider integration setup drafts and returns every issue found. Rules mirror the
/// client-side draft validation in the workstation settings screen
/// (src/Meridian.Ui/dashboard/src/lib/provider-integration-setup-validation.ts); keep the two in
/// sync when adding rules.
/// </summary>
public static class ProviderIntegrationSetupValidator
{
    public static IReadOnlyList<ProviderIntegrationSetupValidationIssue> Validate(
        ProviderIntegrationSetupSaveRequestDto request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var issues = new List<ProviderIntegrationSetupValidationIssue>();

        RequireText(
            issues,
            request.SavedBy,
            "savedBy",
            "provider-setup.saved-by-required",
            "Saved-by operator identity is required.");

        if (request.Manifest is null)
        {
            issues.Add(new ProviderIntegrationSetupValidationIssue(
                "manifest",
                "provider-setup.manifest-required",
                "Manifest draft is required."));
        }
        else
        {
            RequireText(
                issues,
                request.Manifest.ManifestId,
                "manifest.manifestId",
                "provider-setup.manifest-id-required",
                "Manifest id is required.");
            RequireText(
                issues,
                request.Manifest.ProviderId,
                "manifest.providerId",
                "provider-setup.manifest-provider-id-required",
                "Manifest provider id is required.");
        }

        if (request.Connection is null)
        {
            issues.Add(new ProviderIntegrationSetupValidationIssue(
                "connection",
                "provider-setup.connection-required",
                "Connection draft is required."));
        }
        else
        {
            RequireText(
                issues,
                request.Connection.ConnectionId,
                "connection.connectionId",
                "provider-setup.connection-id-required",
                "Connection id is required.");
            RequireText(
                issues,
                request.Connection.ProviderId,
                "connection.providerId",
                "provider-setup.connection-provider-id-required",
                "Connection provider id is required.");
            RequireText(
                issues,
                request.Connection.ManifestId,
                "connection.manifestId",
                "provider-setup.connection-manifest-id-required",
                "Connection manifest id is required.");
            RequireText(
                issues,
                request.Connection.CredentialSecretRef,
                "connection.credentialSecretRef",
                "provider-setup.credential-secret-ref-required",
                "Connection credential secret reference is required.");
        }

        if (request.Manifest is not null && request.Connection is not null)
        {
            AddScopeIssues(issues, request.Manifest, request.Connection);
        }

        return issues;
    }

    private static void AddScopeIssues(
        List<ProviderIntegrationSetupValidationIssue> issues,
        ProviderIntegrationManifestDto manifest,
        ProviderConnectionDto connection)
    {
        if (!string.IsNullOrWhiteSpace(connection.ManifestId)
            && !string.IsNullOrWhiteSpace(manifest.ManifestId)
            && !StringComparer.Ordinal.Equals(connection.ManifestId, manifest.ManifestId))
        {
            issues.Add(new ProviderIntegrationSetupValidationIssue(
                "connection.manifestId",
                "provider-setup.connection-manifest-mismatch",
                "Provider connection manifest id must match the manifest being saved."));
        }

        if (!string.IsNullOrWhiteSpace(connection.ProviderId)
            && !string.IsNullOrWhiteSpace(manifest.ProviderId)
            && !StringComparer.Ordinal.Equals(connection.ProviderId, manifest.ProviderId))
        {
            issues.Add(new ProviderIntegrationSetupValidationIssue(
                "connection.providerId",
                "provider-setup.connection-provider-mismatch",
                "Provider connection provider id must match the manifest provider id."));
        }

        if (!StringComparer.OrdinalIgnoreCase.Equals(connection.Environment, manifest.Environment))
        {
            issues.Add(new ProviderIntegrationSetupValidationIssue(
                "connection.environment",
                "provider-setup.connection-environment-mismatch",
                "Provider connection environment must match the manifest environment."));
        }

        // Drafts arrive as hand-edited JSON, so declared non-nullable collections can still be null.
        if (manifest.Capabilities is null)
        {
            issues.Add(new ProviderIntegrationSetupValidationIssue(
                "manifest.capabilities",
                "provider-setup.manifest-capabilities-required",
                "Manifest capabilities list is required (an empty list is allowed)."));
        }

        if (connection.EnabledCapabilities is null)
        {
            issues.Add(new ProviderIntegrationSetupValidationIssue(
                "connection.enabledCapabilities",
                "provider-setup.connection-capabilities-required",
                "Connection enabled capabilities list is required (an empty list is allowed)."));
        }

        var declaredCapabilities = (manifest.Capabilities ?? [])
            .Select(capability => capability.Capability)
            .ToHashSet();
        var undeclaredCapabilities = new HashSet<ProviderCapabilityKindDto>();
        foreach (var capability in connection.EnabledCapabilities ?? [])
        {
            if (!declaredCapabilities.Contains(capability) && undeclaredCapabilities.Add(capability))
            {
                issues.Add(new ProviderIntegrationSetupValidationIssue(
                    "connection.enabledCapabilities",
                    "provider-setup.connection-capability-not-declared",
                    $"Provider connection enables {capability}, but the manifest does not declare it."));
            }
        }
    }

    private static void RequireText(
        List<ProviderIntegrationSetupValidationIssue> issues,
        string? value,
        string field,
        string code,
        string message)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            issues.Add(new ProviderIntegrationSetupValidationIssue(field, code, message));
        }
    }
}

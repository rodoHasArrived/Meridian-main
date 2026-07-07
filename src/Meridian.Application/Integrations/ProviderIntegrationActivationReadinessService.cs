using Meridian.Contracts.Integrations;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Application.Integrations;

public sealed class ProviderIntegrationActivationReadinessService
{
    private static readonly ProviderCapabilityKindDto[] CertifiedAdapterCapabilities =
    [
        ProviderCapabilityKindDto.OrderPreview,
        ProviderCapabilityKindDto.OrderPlacement,
        ProviderCapabilityKindDto.OrderCancellation
    ];

    private readonly IProviderIntegrationManifestStore store;
    private readonly ILogger<ProviderIntegrationActivationReadinessService> logger;

    public ProviderIntegrationActivationReadinessService(
        IProviderIntegrationManifestStore store,
        ILogger<ProviderIntegrationActivationReadinessService>? logger = null)
    {
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        this.logger = logger ?? NullLogger<ProviderIntegrationActivationReadinessService>.Instance;
    }

    public async Task<ProviderIntegrationActivationReadinessDto> EvaluateAsync(
        string manifestId,
        string? connectionId = null,
        CancellationToken ct = default)
        => await EvaluateAsync(null, manifestId, connectionId, ct).ConfigureAwait(false);

    public async Task<ProviderIntegrationActivationReadinessDto> EvaluateAsync(
        string? tenantId,
        string manifestId,
        string? connectionId = null,
        CancellationToken ct = default)
    {
        logger.LogDebug(
            "Provider integration operation {Operation} starting for manifest {ManifestId}, connection {ConnectionId}.",
            nameof(EvaluateAsync),
            manifestId,
            connectionId);
        try
        {
            return await EvaluateCoreAsync(tenantId, manifestId, connectionId, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Provider integration operation {Operation} failed for manifest {ManifestId}, connection {ConnectionId}.",
                nameof(EvaluateAsync),
                manifestId,
                connectionId);
            throw;
        }
    }

    private async Task<ProviderIntegrationActivationReadinessDto> EvaluateCoreAsync(
        string? tenantId,
        string manifestId,
        string? connectionId = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestId);
        if (connectionId is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);
        }

        var scopedStore = ResolveStore(tenantId);
        var manifest = await scopedStore.GetManifestAsync(manifestId, ct).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Provider integration manifest '{manifestId}' was not found.");
        var connection = string.IsNullOrWhiteSpace(connectionId)
            ? null
            : await scopedStore.GetConnectionAsync(connectionId, ct).ConfigureAwait(false)
                ?? throw new KeyNotFoundException($"Provider integration connection '{connectionId}' was not found.");

        return Evaluate(manifest, connection);
    }

    private IProviderIntegrationManifestStore ResolveStore(string? tenantId)
        => string.IsNullOrWhiteSpace(tenantId)
            ? store
            : store is IProviderIntegrationTenantManifestStoreFactory factory
                ? factory.ForTenant(tenantId)
                : store;

    public static ProviderIntegrationActivationReadinessDto Evaluate(
        ProviderIntegrationManifestDto manifest,
        ProviderConnectionDto? connection = null)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        var issues = new List<ProviderIntegrationActivationIssueDto>();
        var requiredEvidence = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var enabledCapabilities = manifest.Capabilities
            .Where(capability => capability.Enabled)
            .ToArray();
        var isCertifiedAdapter = manifest.IntegrationType == IntegrationTypeDto.CertifiedTradingAdapter;

        AddEvidenceRequirements(manifest, requiredEvidence);
        AddConnectionScopeIssues(manifest, connection, issues);
        AddActivationStateIssues(manifest, connection, issues);

        foreach (var capability in enabledCapabilities)
        {
            AddCertifiedAdapterIssues(manifest, capability, isCertifiedAdapter, issues, requiredEvidence);
            AddMappingIssues(manifest, capability, issues);
        }

        var isReady = !issues.Any(issue => issue.Severity == ProviderIntegrationIssueSeverityDto.Critical);
        return new ProviderIntegrationActivationReadinessDto(
            isReady,
            issues,
            requiredEvidence.Order(StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private static void AddEvidenceRequirements(
        ProviderIntegrationManifestDto manifest,
        HashSet<string> requiredEvidence)
    {
        if (manifest.Activation.RequiresAuthenticationTest)
        {
            requiredEvidence.Add("authentication-test");
        }

        if (manifest.Activation.RequiresEndpointTest)
        {
            requiredEvidence.Add("endpoint-test");
        }

        if (manifest.Activation.RequiresDryRun)
        {
            requiredEvidence.Add("dry-run-result");
        }

        if (manifest.Activation.RequiresApproval)
        {
            requiredEvidence.Add("activation-approval");
        }
    }

    private static void AddConnectionScopeIssues(
        ProviderIntegrationManifestDto manifest,
        ProviderConnectionDto? connection,
        List<ProviderIntegrationActivationIssueDto> issues)
    {
        if (connection is null)
        {
            return;
        }

        if (!StringComparer.Ordinal.Equals(connection.ManifestId, manifest.ManifestId))
        {
            issues.Add(new ProviderIntegrationActivationIssueDto(
                "provider-manifest.connection-manifest-mismatch",
                ProviderIntegrationIssueSeverityDto.Critical,
                "The provider connection is not linked to the manifest under review.",
                null,
                "Select a connection created from the same manifest version."));
        }

        var declaredCapabilities = manifest.Capabilities
            .Select(capability => capability.Capability)
            .ToHashSet();
        foreach (var capability in connection.EnabledCapabilities)
        {
            if (!declaredCapabilities.Contains(capability))
            {
                issues.Add(new ProviderIntegrationActivationIssueDto(
                    "provider-manifest.connection-capability-not-declared",
                    ProviderIntegrationIssueSeverityDto.Critical,
                    $"The provider connection enables {capability}, but the manifest does not declare it.",
                    capability,
                    "Update the manifest capability list or disable the connection capability before activation."));
            }
        }
    }

    private static void AddActivationStateIssues(
        ProviderIntegrationManifestDto manifest,
        ProviderConnectionDto? connection,
        List<ProviderIntegrationActivationIssueDto> issues)
    {
        if (manifest.Activation.RequiresEndpointTest &&
            manifest.State == ProviderIntegrationActivationStateDto.Draft)
        {
            issues.Add(new ProviderIntegrationActivationIssueDto(
                "provider-manifest.endpoint-test-required",
                ProviderIntegrationIssueSeverityDto.Critical,
                "Endpoint connectivity has not been tested for this provider manifest.",
                null,
                "Run the endpoint test before submitting the manifest for approval."));
        }

        if (manifest.Activation.RequiresDryRun &&
            manifest.State < ProviderIntegrationActivationStateDto.DryRunPassed)
        {
            issues.Add(new ProviderIntegrationActivationIssueDto(
                "provider-manifest.dry-run-required",
                ProviderIntegrationIssueSeverityDto.Critical,
                "A successful dry run is required before this provider manifest can be activated.",
                null,
                "Run a dry-run sync and retain the result evidence before activation."));
        }

        if (manifest.Activation.RequiresApproval &&
            manifest.State is ProviderIntegrationActivationStateDto.PendingApproval or ProviderIntegrationActivationStateDto.Active &&
            !HasApprovalEvidence(manifest, connection))
        {
            issues.Add(new ProviderIntegrationActivationIssueDto(
                "provider-manifest.approval-evidence-required",
                ProviderIntegrationIssueSeverityDto.Critical,
                "Activation approval evidence is required before this provider connection can become active.",
                null,
                "Capture the approval actor, approval time, and retained evidence id before activation."));
        }
    }

    private static void AddCertifiedAdapterIssues(
        ProviderIntegrationManifestDto manifest,
        ProviderCapabilityDto capability,
        bool isCertifiedAdapter,
        List<ProviderIntegrationActivationIssueDto> issues,
        HashSet<string> requiredEvidence)
    {
        var requiresCertifiedAdapter = capability.RequiresCertifiedAdapter ||
            CertifiedAdapterCapabilities.Contains(capability.Capability);
        if (!requiresCertifiedAdapter)
        {
            return;
        }

        requiredEvidence.Add($"certified-adapter:{capability.Capability}");
        if (!isCertifiedAdapter)
        {
            issues.Add(new ProviderIntegrationActivationIssueDto(
                "provider-manifest.certified-adapter-required",
                ProviderIntegrationIssueSeverityDto.Critical,
                $"{capability.Capability} requires a certified provider adapter; generic no-code manifests are read-only.",
                capability.Capability,
                "Use a certified trading adapter with sandbox proof, entitlement checks, idempotency, kill-switch support, and audit logging."));
        }

        if (!manifest.Activation.ProductionWriteCapabilitiesAllowed)
        {
            issues.Add(new ProviderIntegrationActivationIssueDto(
                "provider-manifest.production-write-policy-blocked",
                ProviderIntegrationIssueSeverityDto.Critical,
                $"{capability.Capability} is blocked because production write capabilities are disabled for this manifest.",
                capability.Capability,
                "Enable production write capability only through a certified adapter activation policy."));
        }
    }

    private static void AddMappingIssues(
        ProviderIntegrationManifestDto manifest,
        ProviderCapabilityDto capability,
        List<ProviderIntegrationActivationIssueDto> issues)
    {
        foreach (var requiredField in capability.RequiredCanonicalFields.Where(field => !string.IsNullOrWhiteSpace(field)))
        {
            var mapping = manifest.FieldMappings.FirstOrDefault(candidate =>
                candidate.Capability == capability.Capability &&
                StringComparer.OrdinalIgnoreCase.Equals(candidate.TargetField, requiredField));

            if (mapping is null)
            {
                issues.Add(new ProviderIntegrationActivationIssueDto(
                    "provider-manifest.required-mapping-missing",
                    ProviderIntegrationIssueSeverityDto.Critical,
                    $"Required canonical field '{requiredField}' is not mapped for {capability.Capability}.",
                    capability.Capability,
                    "Map the required canonical field before activation."));
                continue;
            }

            if (mapping.Confidence is ProviderMappingConfidenceDto.Low or ProviderMappingConfidenceDto.Medium)
            {
                issues.Add(new ProviderIntegrationActivationIssueDto(
                    "provider-manifest.mapping-review-required",
                    ProviderIntegrationIssueSeverityDto.Warning,
                    $"Required canonical field '{requiredField}' needs operator mapping review.",
                    capability.Capability,
                    "Review and approve the mapping confidence before activation."));
            }
        }
    }

    private static bool HasApprovalEvidence(
        ProviderIntegrationManifestDto manifest,
        ProviderConnectionDto? connection)
        => !string.IsNullOrWhiteSpace(manifest.ApprovedBy) &&
           manifest.ApprovedAt.HasValue &&
           (connection is null || !string.IsNullOrWhiteSpace(connection.ApprovalEvidenceId));
}

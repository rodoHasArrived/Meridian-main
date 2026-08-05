namespace Meridian.Contracts.Workstation;

/// <summary>
/// One deployment-owned prerequisite for authoritative reporting.
/// </summary>
public sealed record ReportingDeploymentComponentDto(
    string ComponentId = default!,
    string DisplayName = default!,
    bool IsReady = default,
    string Summary = default!);

/// <summary>
/// Sanitized capability and durability posture for the canonical reporting authority.
/// This contract never exposes connection strings, destinations, credentials, or schema details.
/// </summary>
public sealed record ReportingDeploymentCapabilityDto(
    bool IsReady = default,
    bool DurableGovernance = default,
    bool DurableArtifacts = default,
    bool DurableReconciliationEvidence = default,
    bool DurableRuns = default,
    bool DurableScheduling = default,
    bool DurableDelivery = default,
    bool RecipientDestinationsConfigured = default,
    bool ClientDocumentsConfigured = default,
    bool MigrationsManaged = default,
    IReadOnlyList<ReportingDeploymentComponentDto> Components = default!,
    IReadOnlyList<string> BlockingReasons = default!);

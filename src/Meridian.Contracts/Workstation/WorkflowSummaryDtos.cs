namespace Meridian.Contracts.Workstation;

/// <summary>
/// Shared home-summary projection for operator workflow guidance across the workstation shell.
/// </summary>
public sealed record OperatorWorkflowHomeSummary(
    DateTimeOffset GeneratedAt,
    bool HasOperatingContext,
    string OperatingContextLabel,
    string FundDisplayName,
    IReadOnlyList<WorkspaceWorkflowSummary> Workspaces);

/// <summary>
/// Workflow posture for one top-level workstation workspace.
/// </summary>
public sealed record WorkspaceWorkflowSummary(
    string WorkspaceId,
    string WorkspaceTitle,
    string StatusLabel,
    string StatusDetail,
    string StatusTone,
    WorkflowNextAction NextAction,
    WorkflowBlockerSummary PrimaryBlocker,
    IReadOnlyList<WorkflowEvidenceBadge> Evidence);

/// <summary>
/// Primary operator action recommended for a workspace.
/// </summary>
public sealed record WorkflowNextAction(
    string Label,
    string Detail,
    string TargetPageTag,
    string Tone);

/// <summary>
/// Primary blocker that explains why the operator cannot progress smoothly.
/// </summary>
public sealed record WorkflowBlockerSummary(
    string Code,
    string Label,
    string Detail,
    string Tone,
    bool IsBlocking);

/// <summary>
/// Lightweight evidence chip supporting a workflow recommendation.
/// </summary>
public sealed record WorkflowEvidenceBadge(
    string Label,
    string Value,
    string Tone);

using Meridian.Ui.Services;

namespace Meridian.Wpf.Models;

public interface IWorkspaceStateToken
{
    int Version { get; }

    string WorkspaceId { get; }

    string PageTag { get; }

    DateTimeOffset CapturedAt { get; }

    string? OperatingContextKey { get; }

    string? SelectedFundProfileId { get; }

    string? SelectedFundId { get; }

    string? SelectedAccountId { get; }

    string? ActiveWorkItemId { get; }

    string? SelectedReconciliationBreakId { get; }

    string? SelectedReconciliationCaseId { get; }

    string? SelectedRunId { get; }

    string? SelectedSessionId { get; }

    WorkstationLayoutState? PaneLayout { get; }

    IReadOnlyDictionary<string, string> StateValues { get; }
}

public sealed class WorkspaceStateToken : IWorkspaceStateToken
{
    public int Version { get; set; } = WorkspaceStateTokenVersions.Current;

    public string WorkspaceId { get; set; } = string.Empty;

    public string PageTag { get; set; } = string.Empty;

    public DateTimeOffset CapturedAt { get; set; } = DateTimeOffset.UtcNow;

    public string? OperatingContextKey { get; set; }

    public string? SelectedFundProfileId { get; set; }

    public string? SelectedFundId { get; set; }

    public string? SelectedAccountId { get; set; }

    public string? ActiveWorkItemId { get; set; }

    public string? SelectedReconciliationBreakId { get; set; }

    public string? SelectedReconciliationCaseId { get; set; }

    public string? SelectedRunId { get; set; }

    public string? SelectedSessionId { get; set; }

    public WorkstationLayoutState? PaneLayout { get; set; }

    public Dictionary<string, string> StateValues { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    IReadOnlyDictionary<string, string> IWorkspaceStateToken.StateValues => StateValues;
}

public static class WorkspaceStateTokenVersions
{
    public const int Current = 1;
}

public sealed class WorkspaceStateRestoreValidation
{
    public IReadOnlySet<string> ExistingSessionIds { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlySet<string> ExistingSymbols { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlySet<string> ExistingFundProfileIds { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlySet<string> ExistingFundIds { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlySet<string> ExistingAccountIds { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlySet<string> OpenWorkItemIds { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlySet<string> OpenReconciliationBreakIds { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlySet<string> OpenReconciliationCaseIds { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlySet<string> ExistingRunIds { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public static WorkspaceStateRestoreValidation AllowAll { get; } = new();
}

public sealed class WorkspaceStateRestoreResult
{
    public WorkspaceStateRestoreResult(WorkspaceStateToken token, bool isRecoveryState, string? recoveryReason)
    {
        Token = token;
        IsRecoveryState = isRecoveryState;
        RecoveryReason = recoveryReason;
    }

    public WorkspaceStateToken Token { get; }

    public bool IsRecoveryState { get; }

    public string? RecoveryReason { get; }
}

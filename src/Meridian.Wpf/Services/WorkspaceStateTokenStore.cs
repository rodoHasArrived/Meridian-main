using System.Text.Json;
using Meridian.Storage.Archival;
using Meridian.Ui.Services;
using Meridian.Wpf.Models;

namespace Meridian.Wpf.Services;

public sealed class WorkspaceStateTokenStore
{
    private const string FileName = "workspace-state.json";
    private static readonly AsyncLocal<string?> FilePathOverride = new();

    public static string GetDefaultFilePath()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Meridian");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, FileName);
    }

    internal static void SetFilePathOverrideForTests(string? filePath)
    {
        FilePathOverride.Value = filePath;
    }

    public async Task SaveAsync(IWorkspaceStateToken token, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(token);

        var mutable = await LoadEnvelopeAsync(ct).ConfigureAwait(false);
        mutable.Tokens.RemoveAll(existing =>
            string.Equals(existing.WorkspaceId, token.WorkspaceId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(existing.PageTag, token.PageTag, StringComparison.OrdinalIgnoreCase));
        mutable.Tokens.Add(CloneToken(token));
        mutable.SavedAt = DateTimeOffset.UtcNow;

        var json = JsonSerializer.Serialize(mutable, Meridian.Ui.Services.DesktopJsonOptions.PrettyPrint);
        await AtomicFileWriter.WriteAsync(GetFilePath(), json, ct).ConfigureAwait(false);
    }

    public async Task<WorkspaceStateToken?> LoadAsync(string workspaceId, string pageTag, CancellationToken ct = default)
    {
        var envelope = await LoadEnvelopeAsync(ct).ConfigureAwait(false);
        return envelope.Tokens
            .Where(token => string.Equals(token.WorkspaceId, workspaceId, StringComparison.OrdinalIgnoreCase))
            .Where(token => string.Equals(token.PageTag, pageTag, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(token => token.CapturedAt)
            .FirstOrDefault();
    }

    public async Task<WorkspaceStateRestoreResult?> RestoreAsync(
        string workspaceId,
        string pageTag,
        WorkspaceStateRestoreValidation? validation = null,
        CancellationToken ct = default)
    {
        var token = await LoadAsync(workspaceId, pageTag, ct).ConfigureAwait(false);
        if (token is null)
        {
            return null;
        }

        var recoveryReason = Validate(token, validation ?? WorkspaceStateRestoreValidation.AllowAll);
        if (recoveryReason is null)
        {
            return new WorkspaceStateRestoreResult(CloneToken(token), false, null);
        }

        return new WorkspaceStateRestoreResult(CreateRecoveryToken(token, recoveryReason), true, recoveryReason);
    }

    public static WorkspaceStateToken CreateToken(
        string workspaceId,
        SessionState session,
        DateTimeOffset? capturedAt = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceId);
        ArgumentNullException.ThrowIfNull(session);

        var values = new Dictionary<string, string>(session.ActiveFilters, StringComparer.OrdinalIgnoreCase);
        foreach (var pair in session.WorkspaceContext)
        {
            values[pair.Key] = pair.Value;
        }

        return new WorkspaceStateToken
        {
            Version = WorkspaceStateTokenVersions.Current,
            WorkspaceId = workspaceId.Trim(),
            PageTag = string.IsNullOrWhiteSpace(session.ActivePageTag) ? "Dashboard" : session.ActivePageTag.Trim(),
            CapturedAt = capturedAt ?? DateTimeOffset.UtcNow,
            OperatingContextKey = FirstValue(values, "OperatingContextKey", "ContextKey") ?? session.WorkstationLayout?.OperatingContextKey,
            SelectedFundProfileId = FirstValue(values, "SelectedFundProfileId", "FundProfileId"),
            SelectedFundId = FirstValue(values, "SelectedFundId", "FundId"),
            SelectedAccountId = FirstValue(values, "SelectedAccountId", "AccountId"),
            ActiveWorkItemId = FirstValue(values, "ActiveWorkItemId", "WorkItemId"),
            SelectedReconciliationBreakId = FirstValue(values, "SelectedReconciliationBreakId", "ReconciliationBreakId"),
            SelectedReconciliationCaseId = FirstValue(values, "SelectedReconciliationCaseId", "ReconciliationCaseId", "CaseId"),
            SelectedRunId = FirstValue(values, "SelectedRunId", "RunId", "ActiveRunId"),
            SelectedSessionId = FirstValue(values, "SelectedSessionId", "SessionId"),
            PaneLayout = CloneLayout(session.WorkstationLayout),
            StateValues = values
        };
    }

    private static WorkspaceStateToken CloneToken(IWorkspaceStateToken token)
        => new()
        {
            Version = token.Version,
            WorkspaceId = token.WorkspaceId,
            PageTag = token.PageTag,
            CapturedAt = token.CapturedAt,
            OperatingContextKey = token.OperatingContextKey,
            SelectedFundProfileId = token.SelectedFundProfileId,
            SelectedFundId = token.SelectedFundId,
            SelectedAccountId = token.SelectedAccountId,
            ActiveWorkItemId = token.ActiveWorkItemId,
            SelectedReconciliationBreakId = token.SelectedReconciliationBreakId,
            SelectedReconciliationCaseId = token.SelectedReconciliationCaseId,
            SelectedRunId = token.SelectedRunId,
            SelectedSessionId = token.SelectedSessionId,
            PaneLayout = CloneLayout(token.PaneLayout),
            StateValues = new Dictionary<string, string>(token.StateValues, StringComparer.OrdinalIgnoreCase)
        };

    private static WorkspaceStateToken CreateRecoveryToken(WorkspaceStateToken token, string recoveryReason)
        => new()
        {
            Version = WorkspaceStateTokenVersions.Current,
            WorkspaceId = token.WorkspaceId,
            PageTag = token.PageTag,
            CapturedAt = DateTimeOffset.UtcNow,
            OperatingContextKey = token.OperatingContextKey,
            SelectedFundProfileId = null,
            SelectedFundId = null,
            SelectedAccountId = null,
            ActiveWorkItemId = null,
            SelectedReconciliationBreakId = null,
            SelectedReconciliationCaseId = null,
            SelectedRunId = null,
            SelectedSessionId = null,
            PaneLayout = null,
            StateValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["RecoveryReason"] = recoveryReason
            }
        };

    private static string? Validate(WorkspaceStateToken token, WorkspaceStateRestoreValidation validation)
    {
        if (token.Version != WorkspaceStateTokenVersions.Current)
        {
            return "version-mismatch";
        }

        if (IsMissing(token.SelectedSessionId, validation.ExistingSessionIds))
        {
            return "missing-session";
        }

        if (TryGetStateValue(token, "SelectedSymbol", out var symbol) &&
            IsMissing(symbol, validation.ExistingSymbols))
        {
            return "deleted-symbol";
        }

        if (IsMissing(token.SelectedFundProfileId, validation.ExistingFundProfileIds) ||
            IsMissing(token.SelectedFundId, validation.ExistingFundIds))
        {
            return "missing-fund-context";
        }

        if (IsMissing(token.SelectedAccountId, validation.ExistingAccountIds))
        {
            return "missing-account";
        }

        if (IsMissing(token.ActiveWorkItemId, validation.OpenWorkItemIds))
        {
            return "closed-work-item";
        }

        if (IsMissing(token.SelectedReconciliationBreakId, validation.OpenReconciliationBreakIds))
        {
            return "closed-reconciliation-break";
        }

        if (IsMissing(token.SelectedReconciliationCaseId, validation.OpenReconciliationCaseIds))
        {
            return "closed-reconciliation-case";
        }

        if (IsMissing(token.SelectedRunId, validation.ExistingRunIds))
        {
            return "missing-run";
        }

        return null;
    }

    private static bool IsMissing(string? value, IReadOnlySet<string> knownValues)
        => !string.IsNullOrWhiteSpace(value) &&
           knownValues.Count > 0 &&
           !knownValues.Contains(value);

    private static bool TryGetStateValue(WorkspaceStateToken token, string key, out string value)
        => token.StateValues.TryGetValue(key, out value!) && !string.IsNullOrWhiteSpace(value);

    private static string? FirstValue(IReadOnlyDictionary<string, string> values, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
    }

    private static WorkstationLayoutState? CloneLayout(WorkstationLayoutState? layout)
    {
        if (layout is null)
        {
            return null;
        }

        var json = JsonSerializer.Serialize(layout, Meridian.Ui.Services.DesktopJsonOptions.Compact);
        return JsonSerializer.Deserialize<WorkstationLayoutState>(json, Meridian.Ui.Services.DesktopJsonOptions.Compact);
    }

    private static string GetFilePath()
    {
        var filePathOverride = FilePathOverride.Value;
        if (!string.IsNullOrWhiteSpace(filePathOverride))
        {
            var directory = Path.GetDirectoryName(filePathOverride);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            return filePathOverride;
        }

        return GetDefaultFilePath();
    }

    private static async Task<WorkspaceStatePersistenceEnvelope> LoadEnvelopeAsync(CancellationToken ct)
    {
        var path = GetFilePath();
        if (!File.Exists(path))
        {
            return new WorkspaceStatePersistenceEnvelope();
        }

        try
        {
            var json = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
            return JsonSerializer.Deserialize<WorkspaceStatePersistenceEnvelope>(json, Meridian.Ui.Services.DesktopJsonOptions.Compact)
                ?? new WorkspaceStatePersistenceEnvelope();
        }
        catch (JsonException)
        {
            return new WorkspaceStatePersistenceEnvelope();
        }
        catch (IOException)
        {
            return new WorkspaceStatePersistenceEnvelope();
        }
    }

    private sealed class WorkspaceStatePersistenceEnvelope
    {
        public int Version { get; set; } = WorkspaceStateTokenVersions.Current;

        public DateTimeOffset SavedAt { get; set; } = DateTimeOffset.UtcNow;

        public List<WorkspaceStateToken> Tokens { get; set; } = new();
    }
}

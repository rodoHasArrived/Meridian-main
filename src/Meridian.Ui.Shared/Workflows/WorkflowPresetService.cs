using Meridian.Contracts.Workstation;

namespace Meridian.Ui.Shared.Workflows;

/// <summary>
/// Validates and manages operator-saved workflow presets on top of the workflow catalog.
/// </summary>
public sealed class WorkflowPresetService
{
    private readonly IWorkflowActionCatalog _catalog;
    private readonly IWorkflowPresetStore _store;

    public WorkflowPresetService(IWorkflowActionCatalog catalog, IWorkflowPresetStore store)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public async Task<WorkflowPresetLibraryDto> GetLibraryAsync(CancellationToken ct = default)
    {
        var presets = await _store.LoadAsync(ct).ConfigureAwait(false);
        return new WorkflowPresetLibraryDto(
            GeneratedAt: DateTimeOffset.UtcNow,
            Presets: SortPresets(presets));
    }

    public async Task<WorkflowPresetMutationResult> SaveAsync(
        WorkflowPresetSaveRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var validation = Validate(request);
        if (validation is not null)
        {
            return WorkflowPresetMutationResult.Fail(validation);
        }

        WorkflowPresetDto? saved = null;
        await _store.UpdateAsync(current =>
        {
            var now = DateTimeOffset.UtcNow;
            var presetId = NormalizePresetId(request.PresetId, request.Name);
            var list = current.ToList();
            var index = list.FindIndex(preset => string.Equals(preset.PresetId, presetId, StringComparison.OrdinalIgnoreCase));
            var existing = index >= 0 ? list[index] : null;
            saved = BuildPreset(request, presetId, existing, now);

            if (index >= 0)
            {
                list[index] = saved;
            }
            else
            {
                list.Add(saved);
            }

            return SortPresets(list);
        }, ct).ConfigureAwait(false);

        return WorkflowPresetMutationResult.Ok(saved!);
    }

    public async Task<WorkflowPresetMutationResult> SetPinnedAsync(
        string presetId,
        bool isPinned,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(presetId))
        {
            return WorkflowPresetMutationResult.Fail("Preset id is required.");
        }

        WorkflowPresetDto? updated = null;
        await _store.UpdateAsync(current =>
        {
            var list = current.ToList();
            var index = list.FindIndex(preset => string.Equals(preset.PresetId, presetId, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
            {
                return SortPresets(list);
            }

            updated = list[index] with
            {
                IsPinned = isPinned,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            list[index] = updated;
            return SortPresets(list);
        }, ct).ConfigureAwait(false);

        return updated is null
            ? WorkflowPresetMutationResult.Missing(presetId)
            : WorkflowPresetMutationResult.Ok(updated);
    }

    public async Task<WorkflowPresetMutationResult> MarkUsedAsync(string presetId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(presetId))
        {
            return WorkflowPresetMutationResult.Fail("Preset id is required.");
        }

        WorkflowPresetDto? updated = null;
        await _store.UpdateAsync(current =>
        {
            var list = current.ToList();
            var index = list.FindIndex(preset => string.Equals(preset.PresetId, presetId, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
            {
                return SortPresets(list);
            }

            var now = DateTimeOffset.UtcNow;
            updated = list[index] with
            {
                LastUsedAt = now,
                UpdatedAt = now
            };
            list[index] = updated;
            return SortPresets(list);
        }, ct).ConfigureAwait(false);

        return updated is null
            ? WorkflowPresetMutationResult.Missing(presetId)
            : WorkflowPresetMutationResult.Ok(updated);
    }

    public async Task<bool> DeleteAsync(string presetId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(presetId))
        {
            return false;
        }

        var deleted = false;
        await _store.UpdateAsync(current =>
        {
            var next = current
                .Where(preset => !string.Equals(preset.PresetId, presetId, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            deleted = next.Length != current.Count;
            return next;
        }, ct).ConfigureAwait(false);

        return deleted;
    }

    private string? Validate(WorkflowPresetSaveRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return "Preset name is required.";
        }

        if (string.IsNullOrWhiteSpace(request.WorkflowId))
        {
            return "Workflow id is required.";
        }

        var workflow = FindWorkflow(request.WorkflowId);
        if (workflow is null)
        {
            return $"Workflow '{request.WorkflowId}' is not registered.";
        }

        var action = ResolveAction(workflow, request.ActionId);
        if (!string.IsNullOrWhiteSpace(request.ActionId) && action is null)
        {
            return $"Action '{request.ActionId}' is not registered for workflow '{workflow.WorkflowId}'.";
        }

        return null;
    }

    private WorkflowPresetDto BuildPreset(
        WorkflowPresetSaveRequest request,
        string presetId,
        WorkflowPresetDto? existing,
        DateTimeOffset now)
    {
        var workflow = FindWorkflow(request.WorkflowId)!;
        var action = ResolveAction(workflow, request.ActionId);

        return new WorkflowPresetDto(
            PresetId: presetId,
            Name: request.Name.Trim(),
            Description: NormalizeOptional(request.Description),
            WorkflowId: workflow.WorkflowId,
            WorkflowTitle: workflow.Title,
            ActionId: action?.ActionId,
            ActionLabel: action?.Label ?? "Open workflow",
            WorkspaceId: workflow.WorkspaceId,
            WorkspaceTitle: workflow.WorkspaceTitle,
            TargetPageTag: action?.TargetPageTag ?? workflow.EntryPageTag,
            Tags: NormalizeTags(request.Tags),
            IsPinned: request.IsPinned,
            CreatedAt: existing?.CreatedAt ?? now,
            UpdatedAt: now,
            LastUsedAt: existing?.LastUsedAt);
    }

    private WorkflowDefinitionDto? FindWorkflow(string workflowId)
        => _catalog.GetWorkflowDefinitions()
            .FirstOrDefault(workflow => string.Equals(workflow.WorkflowId, workflowId, StringComparison.OrdinalIgnoreCase));

    private static WorkflowActionDto? ResolveAction(WorkflowDefinitionDto workflow, string? actionId)
    {
        if (string.IsNullOrWhiteSpace(actionId))
        {
            return null;
        }

        return workflow.Actions.FirstOrDefault(action =>
            string.Equals(action.ActionId, actionId, StringComparison.OrdinalIgnoreCase) ||
            action.Aliases.Any(alias => string.Equals(alias, actionId, StringComparison.OrdinalIgnoreCase)));
    }

    private static IReadOnlyList<WorkflowPresetDto> SortPresets(IEnumerable<WorkflowPresetDto> presets)
        => presets
            .OrderByDescending(static preset => preset.IsPinned)
            .ThenByDescending(static preset => preset.LastUsedAt ?? DateTimeOffset.MinValue)
            .ThenByDescending(static preset => preset.UpdatedAt)
            .ThenBy(static preset => preset.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static string NormalizePresetId(string? presetId, string name)
    {
        var candidate = string.IsNullOrWhiteSpace(presetId) ? name : presetId;
        var chars = candidate.Trim().ToLowerInvariant()
            .Select(static ch => char.IsLetterOrDigit(ch) ? ch : '-')
            .ToArray();
        var normalized = string.Join('-', new string(chars)
            .Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return string.IsNullOrWhiteSpace(normalized)
            ? $"workflow-preset-{Guid.NewGuid():N}"
            : normalized;
    }

    private static string? NormalizeOptional(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private static IReadOnlyList<string> NormalizeTags(IReadOnlyList<string>? tags)
        => (tags ?? [])
            .Select(static tag => tag.Trim())
            .Where(static tag => !string.IsNullOrWhiteSpace(tag))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static tag => tag, StringComparer.OrdinalIgnoreCase)
            .Take(16)
            .ToArray();
}

public sealed record WorkflowPresetMutationResult(
    bool Success,
    WorkflowPresetDto? Preset,
    string? Error,
    bool NotFound)
{
    public static WorkflowPresetMutationResult Ok(WorkflowPresetDto preset)
        => new(Success: true, Preset: preset, Error: null, NotFound: false);

    public static WorkflowPresetMutationResult Fail(string error)
        => new(Success: false, Preset: null, Error: error, NotFound: false);

    public static WorkflowPresetMutationResult Missing(string presetId)
        => new(Success: false, Preset: null, Error: $"Workflow preset '{presetId}' was not found.", NotFound: true);
}

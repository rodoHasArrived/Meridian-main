using Meridian.Contracts.Workstation;

namespace Meridian.Ui.Shared.Workflows;

/// <summary>
/// Durable store for operator-saved workflow presets.
/// </summary>
public interface IWorkflowPresetStore
{
    Task<IReadOnlyList<WorkflowPresetDto>> LoadAsync(CancellationToken ct = default);

    Task<IReadOnlyList<WorkflowPresetDto>> UpdateAsync(
        Func<IReadOnlyList<WorkflowPresetDto>, IReadOnlyList<WorkflowPresetDto>> update,
        CancellationToken ct = default);
}

using Meridian.Wpf.Models;

namespace Meridian.Wpf.Services;

public interface IWorkspaceShellSlotContributionService
{
    ValueTask<IReadOnlyList<IWorkspaceShellSlotContribution>> GetContributionsAsync(
        WorkspaceShellSlotContributionRequest request,
        CancellationToken ct = default);

    ValueTask<WorkspaceShellContext?> GetContextStripAsync(
        WorkspaceShellSlotContributionRequest request,
        CancellationToken ct = default);

    ValueTask<WorkspaceCommandGroup> GetActionBarAsync(
        WorkspaceShellSlotContributionRequest request,
        CancellationToken ct = default);
}

public sealed class WorkspaceShellSlotContributionService : IWorkspaceShellSlotContributionService
{
    private readonly IReadOnlyList<IWorkspaceShellSlotContributor> _contributors;

    public WorkspaceShellSlotContributionService(IEnumerable<IWorkspaceShellSlotContributor>? contributors = null)
    {
        _contributors = contributors?.ToArray() ?? Array.Empty<IWorkspaceShellSlotContributor>();
    }

    public async ValueTask<IReadOnlyList<IWorkspaceShellSlotContribution>> GetContributionsAsync(
        WorkspaceShellSlotContributionRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var contributions = new List<IWorkspaceShellSlotContribution>();
        foreach (var contributor in _contributors)
        {
            ct.ThrowIfCancellationRequested();
            var provided = await contributor.GetContributionsAsync(request, ct).ConfigureAwait(false);
            contributions.AddRange(provided.Where(contribution =>
                string.Equals(contribution.WorkspaceId, request.WorkspaceId, StringComparison.OrdinalIgnoreCase)));
        }

        return contributions
            .OrderBy(static contribution => contribution.Slot)
            .ThenBy(static contribution => contribution.Order)
            .ToArray();
    }

    public async ValueTask<WorkspaceShellContext?> GetContextStripAsync(
        WorkspaceShellSlotContributionRequest request,
        CancellationToken ct = default)
    {
        var contribution = (await GetContributionsAsync(request, ct).ConfigureAwait(false))
            .OfType<WorkspaceShellContextStripContribution>()
            .FirstOrDefault();

        return contribution?.Context;
    }

    public async ValueTask<WorkspaceCommandGroup> GetActionBarAsync(
        WorkspaceShellSlotContributionRequest request,
        CancellationToken ct = default)
    {
        var groups = (await GetContributionsAsync(request, ct).ConfigureAwait(false))
            .OfType<WorkspaceShellActionBarContribution>()
            .OrderBy(static contribution => contribution.Order)
            .Select(static contribution => contribution.CommandGroup)
            .ToArray();

        if (groups.Length == 0)
        {
            return new WorkspaceCommandGroup();
        }

        return new WorkspaceCommandGroup
        {
            PrimaryCommands = groups.SelectMany(static group => group.PrimaryCommands).ToArray(),
            SecondaryCommands = groups.SelectMany(static group => group.SecondaryCommands).ToArray()
        };
    }
}

using Meridian.Wpf.Models;

namespace Meridian.Wpf.Tests.Models;

public sealed class WorkspaceShellChromeContributionTests
{
    [Fact]
    public void ContextStripContribution_ShouldTargetContextStripSlot()
    {
        var contribution = new WorkspaceShellContextStripContribution(
            "trading",
            new WorkspaceShellContext { WorkspaceTitle = "Trading" },
            Order: 20);

        contribution.WorkspaceId.Should().Be("trading");
        contribution.Slot.Should().Be(WorkspaceShellSlot.ContextStrip);
        contribution.Order.Should().Be(20);
        contribution.Context.WorkspaceTitle.Should().Be("Trading");
    }

    [Fact]
    public void ActionBarContribution_ShouldTargetActionBarSlot()
    {
        var commandGroup = new WorkspaceCommandGroup
        {
            PrimaryCommands =
            [
                new WorkspaceCommandItem
                {
                    Id = "refresh",
                    Label = "Refresh",
                    Tone = WorkspaceTone.Primary
                }
            ]
        };

        var contribution = new WorkspaceShellActionBarContribution("accounting", commandGroup);

        contribution.WorkspaceId.Should().Be("accounting");
        contribution.Slot.Should().Be(WorkspaceShellSlot.ActionBar);
        contribution.CommandGroup.PrimaryCommands.Should().ContainSingle(command => command.Id == "refresh");
    }

    [Fact]
    public async Task ContributorContract_ShouldReturnOrderedSlotContributions()
    {
        IWorkspaceShellSlotContributor contributor = new StaticContributor(
            new WorkspaceShellActionBarContribution("trading", new WorkspaceCommandGroup(), Order: 30),
            new WorkspaceShellContextStripContribution("trading", new WorkspaceShellContext(), Order: 10));

        var contributions = await contributor.GetContributionsAsync(
            new WorkspaceShellSlotContributionRequest("trading", "TradingShell"));

        contributions
            .OrderBy(contribution => contribution.Order)
            .Select(contribution => contribution.Slot)
            .Should()
            .Equal(WorkspaceShellSlot.ContextStrip, WorkspaceShellSlot.ActionBar);
    }

    private sealed class StaticContributor(params IWorkspaceShellSlotContribution[] contributions)
        : IWorkspaceShellSlotContributor
    {
        public ValueTask<IReadOnlyList<IWorkspaceShellSlotContribution>> GetContributionsAsync(
            WorkspaceShellSlotContributionRequest request,
            CancellationToken ct = default)
        {
            var matching = contributions
                .Where(contribution => string.Equals(
                    contribution.WorkspaceId,
                    request.WorkspaceId,
                    StringComparison.OrdinalIgnoreCase))
                .OrderBy(contribution => contribution.Order)
                .ToArray();

            return ValueTask.FromResult<IReadOnlyList<IWorkspaceShellSlotContribution>>(matching);
        }
    }
}

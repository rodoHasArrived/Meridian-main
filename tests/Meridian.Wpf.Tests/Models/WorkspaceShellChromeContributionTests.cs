using Meridian.Wpf.Models;
using Meridian.Wpf.Services;
using Microsoft.Extensions.DependencyInjection;

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

    [Fact]
    public async Task ContributionService_ShouldComposeContextStripAndActionBarSlots()
    {
        var tradingContext = new WorkspaceShellContext { WorkspaceTitle = "Trading" };
        var actionBar = new WorkspaceCommandGroup
        {
            PrimaryCommands =
            [
                new WorkspaceCommandItem { Id = "refresh", Label = "Refresh" }
            ],
            SecondaryCommands =
            [
                new WorkspaceCommandItem { Id = "dock", Label = "Dock" }
            ]
        };
        IWorkspaceShellSlotContributionService service = new WorkspaceShellSlotContributionService(
        [
            new StaticContributor(
                new WorkspaceShellActionBarContribution("trading", actionBar, Order: 20),
                new WorkspaceShellContextStripContribution("trading", tradingContext, Order: 10)),
            new StaticContributor(
                new WorkspaceShellContextStripContribution("accounting", new WorkspaceShellContext { WorkspaceTitle = "Accounting" }))
        ]);

        var request = new WorkspaceShellSlotContributionRequest("trading", "TradingShell");

        var context = await service.GetContextStripAsync(request);
        var commands = await service.GetActionBarAsync(request);

        context.Should().BeSameAs(tradingContext);
        commands.PrimaryCommands.Should().ContainSingle(command => command.Id == "refresh");
        commands.SecondaryCommands.Should().ContainSingle(command => command.Id == "dock");
    }

    [Fact]
    public async Task ServiceCollectionExtension_ShouldLetFeatureModulesRegisterSlotContributors()
    {
        var services = new ServiceCollection();
        services.AddWorkspaceShellSlotContributor<TradingChromeContributor>();
        services.AddSingleton<IWorkspaceShellSlotContributionService, WorkspaceShellSlotContributionService>();

        using var provider = services.BuildServiceProvider();
        var service = provider.GetRequiredService<IWorkspaceShellSlotContributionService>();

        var context = await service.GetContextStripAsync(new WorkspaceShellSlotContributionRequest("trading", "TradingShell"));
        var commands = await service.GetActionBarAsync(new WorkspaceShellSlotContributionRequest("trading", "TradingShell"));

        context.Should().NotBeNull();
        context!.WorkspaceTitle.Should().Be("Trading");
        commands.PrimaryCommands.Should().ContainSingle(command => command.Id == "refresh-trading");
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

    private sealed class TradingChromeContributor : IWorkspaceShellSlotContributor
    {
        public ValueTask<IReadOnlyList<IWorkspaceShellSlotContribution>> GetContributionsAsync(
            WorkspaceShellSlotContributionRequest request,
            CancellationToken ct = default)
        {
            if (!string.Equals(request.WorkspaceId, "trading", StringComparison.OrdinalIgnoreCase))
            {
                return ValueTask.FromResult<IReadOnlyList<IWorkspaceShellSlotContribution>>(Array.Empty<IWorkspaceShellSlotContribution>());
            }

            return ValueTask.FromResult<IReadOnlyList<IWorkspaceShellSlotContribution>>(
            [
                new WorkspaceShellContextStripContribution(
                    "trading",
                    new WorkspaceShellContext { WorkspaceTitle = "Trading" }),
                new WorkspaceShellActionBarContribution(
                    "trading",
                    new WorkspaceCommandGroup
                    {
                        PrimaryCommands =
                        [
                            new WorkspaceCommandItem { Id = "refresh-trading", Label = "Refresh" }
                        ]
                    })
            ]);
        }
    }
}

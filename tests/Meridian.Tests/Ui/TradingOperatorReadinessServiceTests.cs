using FluentAssertions;
using Meridian.Contracts.Workstation;
using Meridian.Ui.Shared.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Tests.Ui;

public sealed class TradingOperatorReadinessServiceTests
{
    [Fact]
    public async Task GetAsync_WithoutRegisteredDependencies_ShouldReturnStableOperatorWorkItemIds()
    {
        using var provider = new ServiceCollection().BuildServiceProvider();
        var service = new TradingOperatorReadinessService(
            provider,
            NullLogger<TradingOperatorReadinessService>.Instance);

        var first = await service.GetAsync();
        var second = await service.GetAsync();

        var firstIds = first.WorkItems.Select(static item => item.WorkItemId).ToArray();
        var secondIds = second.WorkItems.Select(static item => item.WorkItemId).ToArray();

        firstIds.Should().Equal(
            "paper-session-missing",
            "execution-audit-empty",
            "promotion-decision-missing",
            "dk1-trust-packet-unavailable");
        secondIds.Should().Equal(firstIds);
        firstIds.Should().NotContain(static id => id.StartsWith("operator-", StringComparison.OrdinalIgnoreCase));

        first.WorkItems.Should().ContainSingle(item =>
            item.WorkItemId == "paper-session-missing" &&
            item.Kind == OperatorWorkItemKindDto.PaperReplay &&
            item.Tone == OperatorWorkItemToneDto.Critical);
        first.OverallStatus.Should().Be(TradingAcceptanceGateStatusDto.Blocked);
        first.ReadyForPaperOperation.Should().BeFalse();
    }

    [Fact]
    public async Task GetAsync_WithCanceledToken_ShouldPreserveCancellationFlow()
    {
        using var provider = new ServiceCollection().BuildServiceProvider();
        var service = new TradingOperatorReadinessService(
            provider,
            NullLogger<TradingOperatorReadinessService>.Instance);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => service.GetAsync(ct: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}

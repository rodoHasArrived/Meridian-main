using FluentAssertions;
using Meridian.Wpf.Services;
using Xunit;

namespace Meridian.Wpf.Tests.Services;

/// <summary>
/// Covers the WPF Trading command bar's desk safety controls against the exit criterion: every
/// safety control either invokes the real shared execution service or is disabled with an explicit
/// not-wired state.
/// <para>
/// Before this, all five resolved to a pane-layout change plus confirmation copy — "Cancel-all
/// review opened", "Pause queued", "Risk acknowledgement captured locally for this workstation
/// session". An operator could press Cancel All during an incident and read text implying the book
/// was being cancelled while nothing had been asked of any broker. These tests pin the two
/// permitted outcomes and, specifically, that no control sits in the third state the criterion
/// exists to forbid: enabled, and doing nothing.
/// </para>
/// </summary>
public sealed class TradingSafetyCommandTests
{
    private static readonly string[] SafetyCommandIds =
        ["Pause", "Stop", "Flatten", "CancelAll", "AcknowledgeRisk"];

    /// <summary>
    /// The criterion as a single assertion: every safety control is either wired or disabled, and
    /// none is both enabled and unwired.
    /// </summary>
    [Fact]
    public void EverySafetyControl_IsEitherWiredOrExplicitlyDisabled()
    {
        var commands = TradingWorkspaceShellPresentationService.BuildCommandGroup();
        var safety = commands.PrimaryCommands
            .Concat(commands.SecondaryCommands)
            .Where(command => SafetyCommandIds.Contains(command.Id))
            .ToList();

        safety.Should().HaveCount(SafetyCommandIds.Length, "the bar must still publish every safety control");

        foreach (var command in safety)
        {
            var wired = TradingWorkspaceShellPresentationService.IsSafetyCommand(command.Id);
            if (wired)
            {
                command.IsEnabled.Should().BeTrue($"{command.Id} reaches the shared execution service");
            }
            else
            {
                command.IsEnabled.Should().BeFalse($"{command.Id} reaches no execution service, so it must not be pressable");
                command.DisabledReason.Should().NotBeNullOrWhiteSpace(
                    $"{command.Id} must say why it cannot run, not just refuse silently");
            }
        }
    }

    /// <summary>
    /// The layout mapper no longer narrates desk actions. Its confirmation text is what made the
    /// dead buttons convincing, and the wired commands now report the service's own outcome.
    /// </summary>
    [Theory]
    [InlineData("Pause")]
    [InlineData("Stop")]
    [InlineData("Flatten")]
    [InlineData("CancelAll")]
    [InlineData("AcknowledgeRisk")]
    public void SafetyCommands_CarryNoConfirmationCopyOfTheirOwn(string commandId)
    {
        var request = TradingWorkspaceShellPresentationService.CreateActionRequest(commandId);

        request.ConfirmationMessage.Should().BeNull(
            "pane layout must not claim a desk action took place");
    }

    [Fact]
    public async Task WiredCommands_ReachTheSharedExecutionService()
    {
        var client = new RecordingSafetyControlClient();
        var service = CreateService(client);

        (await service.ExecuteSafetyCommandAsync("CancelAll")).Succeeded.Should().BeTrue();
        (await service.ExecuteSafetyCommandAsync("Stop")).Succeeded.Should().BeTrue();

        client.CancelAllCalls.Should().Be(1);
        client.HaltCalls.Should().Be(1);
    }

    /// <summary>
    /// The sweep's own verdict reaches the operator unchanged. A WPF surface that rewrote a
    /// partial cancellation as "cancel-all issued" would reintroduce the claim the server-side
    /// sweep exists to refuse.
    /// </summary>
    [Fact]
    public async Task PartialCancellation_IsReportedAsFailure_WithTheServiceMessageIntact()
    {
        var client = new RecordingSafetyControlClient
        {
            CancelAllResult = new SafetyCommandOutcome(
                false,
                "Kill-switch cancel-all cancelled 1 of 2 open order(s); 1 still working and requiring manual cancellation: ord-2 (MSFT).")
        };

        var outcome = await CreateService(client).ExecuteSafetyCommandAsync("CancelAll");

        outcome.Succeeded.Should().BeFalse();
        outcome.Message.Should().Contain("ord-2", "the operator needs the order that survived");
    }

    /// <summary>
    /// A host composed without the client must not report a halt. Silence here is the failure
    /// mode: the operator walks away believing the desk is stopped.
    /// </summary>
    [Fact]
    public async Task WithoutAComposedClient_TheCommandReportsThatNothingWasSent()
    {
        var outcome = await CreateService(safetyControlClient: null).ExecuteSafetyCommandAsync("Stop");

        outcome.Succeeded.Should().BeFalse();
        outcome.Message.Should().Contain("not sent");
    }

    [Theory]
    [InlineData("Pause")]
    [InlineData("Flatten")]
    [InlineData("AcknowledgeRisk")]
    [InlineData("PositionBlotter")]
    public async Task UnwiredAndNavigationCommands_AreNotTreatedAsSafetyCommands(string commandId)
    {
        TradingWorkspaceShellPresentationService.IsSafetyCommand(commandId).Should().BeFalse();

        var client = new RecordingSafetyControlClient();
        var outcome = await CreateService(client).ExecuteSafetyCommandAsync(commandId);

        outcome.Succeeded.Should().BeFalse();
        client.CancelAllCalls.Should().Be(0);
        client.HaltCalls.Should().Be(0);
    }

    private static TradingWorkspaceShellPresentationService CreateService(
        IExecutionSafetyControlClient? safetyControlClient) =>
        new(
            runService: null!,
            fundContextService: null!,
            operatingContextService: null,
            cashFinancingReadService: null!,
            shellContextService: null!,
            workflowSummaryService: null,
            operatorReadinessService: null,
            safetyControlClient: safetyControlClient);

    private sealed class RecordingSafetyControlClient : IExecutionSafetyControlClient
    {
        public int CancelAllCalls { get; private set; }

        public int HaltCalls { get; private set; }

        public SafetyCommandOutcome CancelAllResult { get; init; } =
            new(true, "Kill-switch cancel-all cancelled 2 of 2 open order(s).");

        public Task<SafetyCommandOutcome> CancelAllOrdersAsync(CancellationToken ct = default)
        {
            CancelAllCalls++;
            return Task.FromResult(CancelAllResult);
        }

        public Task<SafetyCommandOutcome> HaltTradingAsync(string reason, CancellationToken ct = default)
        {
            HaltCalls++;
            return Task.FromResult(new SafetyCommandOutcome(true, "Trading halted."));
        }
    }
}

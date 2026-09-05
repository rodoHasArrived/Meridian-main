using FluentAssertions;
using Meridian.Wpf.ViewModels;
using Xunit;

namespace Meridian.Wpf.Tests.ViewModels;

/// <summary>
/// The Event Replay page's Start, Pause, and Stop used to be enabled and to flip a local status
/// string over sample sessions while writing "Replay stopped." -- a safety-shaped control that
/// claimed an action nothing performed. Until the page drives the shared replay API they must be
/// disabled with the reason attached, and a command that reaches the view model anyway must
/// neither touch session state nor write confirmation copy.
/// </summary>
public sealed class EventReplayViewModelTests
{
    [Fact]
    public void ReplayControls_AreDisabledWithAnExplicitNotWiredReason()
    {
        var viewModel = new EventReplayViewModel();
        viewModel.Initialize();

        viewModel.IsReplayControlWired.Should().BeFalse();
        viewModel.CanStart.Should().BeFalse();
        viewModel.CanPause.Should().BeFalse();
        viewModel.CanStop.Should().BeFalse();
        viewModel.ControlDisabledReason.Should().Be(EventReplayViewModel.NotWiredReason);
        viewModel.StatusMessage.Should().Be(EventReplayViewModel.NotWiredReason);
    }

    [Theory]
    [InlineData("start")]
    [InlineData("pause")]
    [InlineData("stop")]
    public void ReplayCommand_WhenNotWired_DoesNotMutateTheSessionOrClaimAnAction(string command)
    {
        var viewModel = new EventReplayViewModel();
        viewModel.Initialize();
        var session = viewModel.SelectedReplay!;
        var statusBefore = session.Status;
        var lastRunBefore = session.LastRun;

        switch (command)
        {
            case "start":
                viewModel.StartReplay();
                break;
            case "pause":
                viewModel.PauseReplay();
                break;
            default:
                viewModel.StopReplay();
                break;
        }

        session.Status.Should().Be(statusBefore, "an unwired control must not change session state");
        session.LastRun.Should().Be(lastRunBefore);
        viewModel.StatusMessage.Should().Be(EventReplayViewModel.NotWiredReason,
            "the page must never read as though a replay was started, paused, or stopped");
    }
}

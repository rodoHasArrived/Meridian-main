using FluentAssertions;
using Meridian.Application.Commands;
using Xunit;

namespace Meridian.Tests.Application.Commands;

/// <summary>
/// Tests for the CommandDispatcher.
/// Validates command routing and fallback behavior.
/// </summary>
public class CommandDispatcherTests
{
    [Fact]
    public async Task TryDispatchAsync_WithMatchingCommand_ReturnsHandled()
    {
        var command = new TestCommand("--test", exitCode: 0);
        var dispatcher = new CommandDispatcher(command);

        var (handled, result) = await dispatcher.TryDispatchAsync(new[] { "--test" });

        handled.Should().BeTrue();
        result.ExitCode.Should().Be(0);
    }

    [Fact]
    public async Task TryDispatchAsync_WithNoMatchingCommand_ReturnsNotHandled()
    {
        var command = new TestCommand("--test", exitCode: 0);
        var dispatcher = new CommandDispatcher(command);

        var (handled, result) = await dispatcher.TryDispatchAsync(new[] { "--other" });

        handled.Should().BeFalse();
        result.ExitCode.Should().Be(0);
    }

    [Fact]
    public async Task TryDispatchAsync_WithFailingCommand_ReturnsErrorCode()
    {
        var command = new TestCommand("--fail", exitCode: 1);
        var dispatcher = new CommandDispatcher(command);

        var (handled, result) = await dispatcher.TryDispatchAsync(new[] { "--fail" });

        handled.Should().BeTrue();
        result.ExitCode.Should().Be(1);
    }

    [Fact]
    public async Task TryDispatchAsync_WithMultipleCommands_DispatchesToFirst()
    {
        var cmd1 = new TestCommand("--first", exitCode: 10);
        var cmd2 = new TestCommand("--second", exitCode: 20);
        var dispatcher = new CommandDispatcher(cmd1, cmd2);

        var (handled, result) = await dispatcher.TryDispatchAsync(new[] { "--second" });

        handled.Should().BeTrue();
        result.ExitCode.Should().Be(20);
    }

    [Fact]
    public async Task TryDispatchAsync_EmptyArgs_ReturnsNotHandled()
    {
        var command = new TestCommand("--test", exitCode: 0);
        var dispatcher = new CommandDispatcher(command);

        var (handled, _) = await dispatcher.TryDispatchAsync(Array.Empty<string>());

        handled.Should().BeFalse();
    }

    [Fact]
    public async Task TryDispatchAsync_WhenMultipleCommandsCanHandle_UsesRegistrationOrder()
    {
        var first = new RecordingCommand(canHandle: true, exitCode: 11);
        var second = new RecordingCommand(canHandle: true, exitCode: 22);
        var dispatcher = new CommandDispatcher(first, second);

        var (handled, result) = await dispatcher.TryDispatchAsync(new[] { "--shared" });

        handled.Should().BeTrue();
        result.ExitCode.Should().Be(11);
        first.ExecutionCount.Should().Be(1);
        second.ExecutionCount.Should().Be(0);
    }

    [Fact]
    public async Task TryDispatchAsync_ForwardsCancellationTokenToExecutedCommand()
    {
        using var cts = new CancellationTokenSource();
        var command = new RecordingCommand(canHandle: true, exitCode: 0);
        var dispatcher = new CommandDispatcher(command);
        cts.Cancel();

        await dispatcher.TryDispatchAsync(new[] { "--shared" }, cts.Token);

        command.ReceivedCancellationToken.Should().Be(cts.Token);
    }

    private sealed class TestCommand : ICliCommand
    {
        private readonly string _flag;
        private readonly int _exitCode;

        public TestCommand(string flag, int exitCode)
        {
            _flag = flag;
            _exitCode = exitCode;
        }

        public bool CanHandle(string[] args) => args.Contains(_flag);

        public Task<CliResult> ExecuteAsync(string[] args, CancellationToken ct = default)
            => Task.FromResult(_exitCode == 0 ? CliResult.Ok() : CliResult.Fail(_exitCode));
    }

    private sealed class RecordingCommand : ICliCommand
    {
        private readonly bool _canHandle;
        private readonly int _exitCode;

        public RecordingCommand(bool canHandle, int exitCode)
        {
            _canHandle = canHandle;
            _exitCode = exitCode;
        }

        public int ExecutionCount { get; private set; }
        public CancellationToken ReceivedCancellationToken { get; private set; }

        public bool CanHandle(string[] args) => _canHandle;

        public Task<CliResult> ExecuteAsync(string[] args, CancellationToken ct = default)
        {
            ExecutionCount++;
            ReceivedCancellationToken = ct;
            return Task.FromResult(_exitCode == 0 ? CliResult.Ok() : CliResult.Fail(_exitCode));
        }
    }

}

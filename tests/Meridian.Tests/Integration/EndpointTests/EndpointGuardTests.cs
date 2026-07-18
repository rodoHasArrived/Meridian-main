using FluentAssertions;
using Meridian.Ui.Shared.Endpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Tests.Integration.EndpointTests;

public sealed class EndpointGuardTests
{
    [Fact]
    public async Task GuardAsync_ReturnsHandlerResultOnSuccess()
    {
        var expected = Results.Ok("payload");

        var result = await EndpointHelpers.GuardAsync(
            () => Task.FromResult(expected), "Operation failed.");

        result.Should().BeSameAs(expected);
    }

    [Fact]
    public async Task GuardAsync_ConvertsFailuresToProblem()
    {
        var result = await EndpointHelpers.GuardAsync(
            () => throw new InvalidOperationException("boom"),
            "Operation failed.");

        var problem = result.Should().BeOfType<ProblemHttpResult>().Subject;
        problem.ProblemDetails.Detail.Should().Be("Operation failed.");
    }

    [Fact]
    public async Task GuardAsync_AppendsExceptionMessageWhenConfigured()
    {
        var result = await EndpointHelpers.GuardAsync(
            () => throw new InvalidOperationException("boom"),
            "Operation failed",
            includeExceptionMessage: true);

        result.Should().BeOfType<ProblemHttpResult>()
            .Which.ProblemDetails.Detail.Should().Be("Operation failed: boom");
    }

    [Fact]
    public async Task GuardAsync_PropagatesCancellation()
    {
        var act = () => EndpointHelpers.GuardAsync(
            () => throw new OperationCanceledException(),
            "Operation failed.");

        await act.Should().ThrowAsync<OperationCanceledException>(
            "request aborts must not be reported as endpoint failures");
    }

    [Fact]
    public async Task GuardAsync_UsesExceptionMappingBeforeGenericProblem()
    {
        var result = await EndpointHelpers.GuardAsync(
            () => throw new KeyNotFoundException("missing"),
            "Operation failed.",
            mapException: ex => ex is KeyNotFoundException ? Results.NotFound("missing") : null);

        result.Should().BeOfType<NotFound<string>>();
    }

    [Fact]
    public async Task GuardAsync_FallsThroughWhenMappingReturnsNull()
    {
        var result = await EndpointHelpers.GuardAsync(
            () => throw new InvalidOperationException("boom"),
            "Operation failed.",
            mapException: static _ => null);

        result.Should().BeOfType<ProblemHttpResult>();
    }

    [Fact]
    public async Task GuardAsync_LogsFailuresWhenLoggerSupplied()
    {
        var logger = new CapturingLogger();

        await EndpointHelpers.GuardAsync(
            () => throw new InvalidOperationException("boom"),
            "Operation failed.",
            logger);

        logger.Errors.Should().ContainSingle()
            .Which.Should().Contain("Operation failed.");
    }

    private sealed class CapturingLogger : ILogger
    {
        public List<string> Errors { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
            => NullLogger.Instance.BeginScope(state);

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Error)
                Errors.Add(formatter(state, exception));
        }
    }
}

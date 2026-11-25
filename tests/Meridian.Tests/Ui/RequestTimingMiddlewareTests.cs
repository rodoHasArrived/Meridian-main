using System.Collections.Concurrent;
using FluentAssertions;
using Meridian.Ui.Shared.Endpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Meridian.Tests.Ui;

/// <summary>
/// Request timing: every request lands in <c>mdc_http_request_duration_seconds</c> under its route
/// template (never the raw path), and a request at or over the threshold logs one Warning.
/// </summary>
[Trait("Category", "Unit")]
public sealed class RequestTimingMiddlewareTests
{
    [Fact]
    public async Task ARequest_IsRecordedUnderItsRouteTemplate_NotItsRawPath()
    {
        // A unique template keeps parallel tests' observations out of this assertion.
        var template = $"/api/test-{Guid.NewGuid():N}/{{id}}";
        var logger = new CapturingLogger();
        var middleware = new RequestTimingMiddleware(
            context => { context.Response.StatusCode = StatusCodes.Status200OK; return Task.CompletedTask; },
            TimeSpan.FromSeconds(30),
            logger);

        await middleware.InvokeAsync(CreateContext(template, "/api/test/secret-account-42?token=abc"));

        var exposition = await ExportAsync();
        exposition.Should().Contain($"mdc_http_request_duration_seconds_count{{route=\"{template}\",method=\"GET\",status=\"200\"}} 1");
        exposition.Should().NotContain("secret-account-42", "raw paths and query strings never become label values");
        logger.Entries.Should().BeEmpty("a fast request is not a slow request");
    }

    [Fact]
    public async Task ARequestOverTheThreshold_LogsOneWarningWithRouteStatusAndTrace()
    {
        var template = $"/api/slow-{Guid.NewGuid():N}";
        var logger = new CapturingLogger();
        var middleware = new RequestTimingMiddleware(
            async context => { await Task.Delay(60); context.Response.StatusCode = StatusCodes.Status202Accepted; },
            TimeSpan.FromMilliseconds(20),
            logger);
        var context = CreateContext(template, template);
        context.TraceIdentifier = "trace-123";

        await middleware.InvokeAsync(context);

        var warning = logger.Entries.Should().ContainSingle().Subject;
        warning.Level.Should().Be(LogLevel.Warning);
        warning.Message.Should().Contain($"GET {template} returned 202").And.Contain("trace trace-123");
    }

    [Fact]
    public async Task AThrowingRequest_IsRecordedAs500_AndTheExceptionPropagates()
    {
        var template = $"/api/throws-{Guid.NewGuid():N}";
        var middleware = new RequestTimingMiddleware(
            _ => throw new InvalidOperationException("boom"),
            TimeSpan.Zero,
            new CapturingLogger());

        var act = () => middleware.InvokeAsync(CreateContext(template, template));

        await act.Should().ThrowAsync<InvalidOperationException>();
        (await ExportAsync()).Should().Contain(
            $"mdc_http_request_duration_seconds_count{{route=\"{template}\",method=\"GET\",status=\"500\"}} 1");
    }

    [Fact]
    public async Task AnUnmatchedRequest_IsLabelledUnmatched()
    {
        var logger = new CapturingLogger();
        var middleware = new RequestTimingMiddleware(
            context => { context.Response.StatusCode = StatusCodes.Status404NotFound; return Task.CompletedTask; },
            TimeSpan.Zero,
            logger);
        var context = new DefaultHttpContext();
        context.Request.Method = "GET";
        context.Request.Path = "/no/such/" + Guid.NewGuid().ToString("N");

        await middleware.InvokeAsync(context);

        (await ExportAsync()).Should().Contain(
            $"mdc_http_request_duration_seconds_count{{route=\"{RequestTimingMiddleware.UnmatchedRoute}\",method=\"GET\",status=\"404\"}}");
        logger.Entries.Should().BeEmpty("a zero threshold turns the slow-request log off");
    }

    private static DefaultHttpContext CreateContext(string template, string path)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = "GET";
        var queryAt = path.IndexOf('?');
        context.Request.Path = queryAt < 0 ? path : path[..queryAt];
        if (queryAt >= 0)
            context.Request.QueryString = new QueryString(path[queryAt..]);
        context.SetEndpoint(new RouteEndpoint(
            _ => Task.CompletedTask, RoutePatternFactory.Parse(template), 0, EndpointMetadataCollection.Empty, template));
        return context;
    }

    private static async Task<string> ExportAsync()
    {
        using var buffer = new MemoryStream();
        await Prometheus.Metrics.DefaultRegistry.CollectAndExportAsTextAsync(buffer);
        return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
    }

    private sealed record CapturedEntry(LogLevel Level, string Message);

    private sealed class CapturingLogger : ILogger<RequestTimingMiddleware>
    {
        private readonly ConcurrentQueue<CapturedEntry> _entries = new();

        public IReadOnlyCollection<CapturedEntry> Entries => _entries;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => _entries.Enqueue(new CapturedEntry(logLevel, formatter(state, exception)));
    }
}

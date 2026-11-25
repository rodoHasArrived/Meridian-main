using System.Collections.Concurrent;
using System.Diagnostics;
using FluentAssertions;
using Meridian.Core.Logging;
using Serilog.Context;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting.Display;
using Xunit;

namespace Meridian.Tests.Application.Logging;

/// <summary>
/// The file log's enrichment: <c>ThreadId</c> names the thread that wrote each line, and the file
/// template renders scope properties (such as the pipeline's correlation ID) and the trace ID.
/// </summary>
[Trait("Category", "Unit")]
public sealed class LoggingSetupEnrichmentTests
{
    [Fact]
    public async Task ThreadIdEnricher_StampsTheEmittingThread_NotTheConfiguringThread()
    {
        var sink = new CollectingSink();
        using var logger = new Serilog.LoggerConfiguration()
            .Enrich.With<ThreadIdEnricher>()
            .WriteTo.Sink(sink)
            .CreateLogger();

        // Both threads stay alive until each has logged, so their managed thread IDs cannot be reused.
        using var bothLogged = new CountdownEvent(2);
        var first = RunOnDedicatedThread(() => logger.Information("first"), bothLogged);
        var second = RunOnDedicatedThread(() => logger.Information("second"), bothLogged);
        await Task.WhenAll(first, second);

        var threadIds = sink.Events
            .OrderBy(static e => e.MessageTemplate.Text, StringComparer.Ordinal)
            .Select(static e => e.Properties[ThreadIdEnricher.PropertyName].Should().BeOfType<ScalarValue>().Subject.Value)
            .ToArray();
        threadIds.Should().HaveCount(2).And.OnlyHaveUniqueItems();
        threadIds.Should().Equal(first.Result, second.Result);
    }

    [Fact]
    public void FileOutputTemplate_RendersScopePropertiesAndTheTraceId()
    {
        var sink = new CollectingSink();
        using var logger = new Serilog.LoggerConfiguration()
            .Enrich.FromLogContext()
            .Enrich.With<ThreadIdEnricher>()
            .WriteTo.Sink(sink)
            .CreateLogger();

        using var activity = new Activity("pipeline-test").SetIdFormat(ActivityIdFormat.W3C).Start();
        using (LogContext.PushProperty("CorrelationId", "corr-42"))
        {
            logger.ForContext("SourceContext", "Meridian.Application.Pipeline.EventPipeline")
                .Warning("Pipeline queue utilization at {Utilization:P0}", 0.85);
        }

        var formatter = new MessageTemplateTextFormatter(LoggingSetup.FileOutputTemplate);
        using var writer = new StringWriter();
        formatter.Format(sink.Events.Single(), writer);
        var line = writer.ToString();

        line.Should().Contain("[WRN]");
        line.Should().Contain("Meridian.Application.Pipeline.EventPipeline");
        line.Should().Contain("\"CorrelationId\":\"corr-42\"");
        line.Should().Contain(activity.TraceId.ToHexString());
        line.Should().Contain($"[{Environment.CurrentManagedThreadId}]");
    }

    private static Task<int> RunOnDedicatedThread(Action action, CountdownEvent bothLogged)
    {
        var completion = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            action();
            bothLogged.Signal();
            bothLogged.Wait(TimeSpan.FromSeconds(10));
            completion.SetResult(Environment.CurrentManagedThreadId);
        });
        thread.Start();
        return completion.Task;
    }

    private sealed class CollectingSink : ILogEventSink
    {
        private readonly ConcurrentQueue<LogEvent> _events = new();

        public IReadOnlyCollection<LogEvent> Events => _events;

        public void Emit(LogEvent logEvent) => _events.Enqueue(logEvent);
    }
}

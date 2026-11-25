using FluentAssertions;
using Meridian.Application.Monitoring;
using Meridian.Core.Logging;
using Meridian.Platform.Diagnostics;
using Xunit;

namespace Meridian.Tests.Application.Monitoring;

/// <summary>
/// Error and Fatal log events reach the <c>/api/errors</c> buffer through <see cref="ErrorLogTap"/>;
/// lower levels do not, and disposing the recorder stops recording.
/// </summary>
[Trait("Category", "Unit")]
public sealed class ErrorLogRecorderTests
{
    [Fact]
    public void ErrorEvents_AreRecordedWithTheirCategoryAndException_WarningsAreNot()
    {
        // The tap is process-wide, so each assertion looks for this test's own unique messages.
        var marker = Guid.NewGuid().ToString("N");
        using var recorder = new ErrorLogRecorder(new ErrorRingBuffer());
        using var logger = new Serilog.LoggerConfiguration()
            .MinimumLevel.Verbose()
            .WriteTo.Sink(ErrorLogTap.Instance)
            .CreateLogger();
        var categorized = logger.ForContext("SourceContext", "Meridian.Storage.Sinks.JsonlStorageSink");

        categorized.Warning("warning {Marker}", marker);
        categorized.Error(new IOException("disk full"), "flush failed {Marker}", marker);
        categorized.Fatal("fatal {Marker}", marker);

        var mine = recorder.Buffer.GetRecent(100).Where(entry => entry.Message.Contains(marker, StringComparison.Ordinal)).ToList();
        mine.Should().HaveCount(2, "only Error and Fatal reach the buffer");
        var error = mine.Single(entry => entry.Level == ErrorLevel.Error);
        error.Source.Should().Be("Meridian.Storage.Sinks.JsonlStorageSink");
        error.ExceptionType.Should().Be(nameof(IOException));
        mine.Should().ContainSingle(entry => entry.Level == ErrorLevel.Critical);
    }

    [Fact]
    public void DisposingTheRecorder_StopsRecording()
    {
        var marker = Guid.NewGuid().ToString("N");
        var recorder = new ErrorLogRecorder(new ErrorRingBuffer());
        using var logger = new Serilog.LoggerConfiguration().WriteTo.Sink(ErrorLogTap.Instance).CreateLogger();

        recorder.Dispose();
        logger.Error("after dispose {Marker}", marker);

        recorder.Buffer.GetRecent(100).Should().NotContain(entry => entry.Message.Contains(marker, StringComparison.Ordinal));
    }
}

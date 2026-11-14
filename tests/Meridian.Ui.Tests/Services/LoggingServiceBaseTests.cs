using System;
using System.Collections.Generic;
using FluentAssertions;
using Meridian.Ui.Services.Services;
using Xunit;

namespace Meridian.Ui.Tests.Services;

/// <summary>
/// Concrete test implementation of LoggingServiceBase.
/// </summary>
internal sealed class TestLoggingService : LoggingServiceBase
{
    public List<string> WrittenMessages { get; } = new();

    protected override void WriteOutput(string formattedMessage)
    {
        WrittenMessages.Add(formattedMessage);
    }
}

public sealed class LoggingServiceBaseTests
{
    private readonly TestLoggingService _sut = new();

    [Fact]
    public void LogInfo_WritesFormattedMessage()
    {
        _sut.LogInfo("Test message");

        _sut.WrittenMessages.Should().ContainSingle();
        _sut.WrittenMessages[0].Should().Contain("[INFO ]");
        _sut.WrittenMessages[0].Should().Contain("Test message");
    }

    [Fact]
    public void LogWarning_WritesWarningLevel()
    {
        _sut.LogWarning("Warning here");

        _sut.WrittenMessages.Should().ContainSingle();
        _sut.WrittenMessages[0].Should().Contain("[WARN ]");
        _sut.WrittenMessages[0].Should().Contain("Warning here");
    }

    [Fact]
    public void LogError_WritesErrorLevel()
    {
        _sut.LogError("Error occurred");

        _sut.WrittenMessages.Should().ContainSingle();
        _sut.WrittenMessages[0].Should().Contain("[ERROR]");
        _sut.WrittenMessages[0].Should().Contain("Error occurred");
    }

    [Fact]
    public void LogDebug_WritesDebugLevel()
    {
        _sut.LogDebug("Debug info");

        _sut.WrittenMessages.Should().ContainSingle();
        _sut.WrittenMessages[0].Should().Contain("[DEBUG]");
    }

    [Fact]
    public void LogInfo_IncludesStructuredProperties()
    {
        _sut.LogInfo("Processing", ("Symbol", "AAPL"), ("Count", "42"));

        _sut.WrittenMessages[0].Should().Contain("Symbol=AAPL");
        _sut.WrittenMessages[0].Should().Contain("Count=42");
    }

    [Fact]
    public void LogError_IncludesExceptionDetails()
    {
        var ex = new InvalidOperationException("test error");

        _sut.LogError("Failed", ex);

        _sut.WrittenMessages[0].Should().Contain("InvalidOperationException");
        _sut.WrittenMessages[0].Should().Contain("test error");
    }

    [Fact]
    public void LogError_IncludesInnerException()
    {
        var inner = new ArgumentException("inner cause");
        var ex = new InvalidOperationException("outer", inner);

        _sut.LogError("Failed", ex);

        _sut.WrittenMessages[0].Should().Contain("InnerException");
        _sut.WrittenMessages[0].Should().Contain("inner cause");
    }

    [Fact]
    public void MinimumLevel_FiltersLowerLevels()
    {
        _sut.MinimumLevel = LogLevel.Warning;

        _sut.LogDebug("debug");
        _sut.LogInfo("info");
        _sut.LogWarning("warning");
        _sut.LogError("error");

        _sut.WrittenMessages.Should().HaveCount(2);
        _sut.WrittenMessages[0].Should().Contain("warning");
        _sut.WrittenMessages[1].Should().Contain("error");
    }

    [Fact]
    public void LogWritten_EventRaised()
    {
        LogEntryEventArgs? received = null;
        _sut.LogWritten += (_, e) => received = e;

        _sut.LogInfo("test event", ("key", "value"));

        received.Should().NotBeNull();
        received!.Level.Should().Be(LogLevel.Info);
        received.Message.Should().Be("test event");
        received.Properties.Should().Contain(p => p.key == "key" && p.value == "value");
    }

    [Fact]
    public void LogWritten_IncludesExceptionInEventArgs()
    {
        LogEntryEventArgs? received = null;
        _sut.LogWritten += (_, e) => received = e;
        var ex = new InvalidOperationException("boom");

        _sut.LogError("failure", ex);

        received.Should().NotBeNull();
        received!.Exception.Should().BeSameAs(ex);
    }

    [Fact]
    public void LogError_RendersContextAndRetainsStructuredPropertiesAndException()
    {
        LogEntryEventArgs? received = null;
        _sut.LogWritten += (_, e) => received = e;
        var exception = new InvalidOperationException("backend unavailable");

        _sut.LogError("Failed to load {Symbol} in {Workspace}", exception,
            ("Symbol", "AAPL"), ("Workspace", "Trading"));

        received.Should().NotBeNull();
        received!.Message.Should().Be("Failed to load AAPL in Trading");
        received.Exception.Should().BeSameAs(exception);
        received.Properties.Should().Equal(("Symbol", "AAPL"), ("Workspace", "Trading"));
        _sut.WrittenMessages.Should().ContainSingle();
        _sut.WrittenMessages[0].Should().Contain(received.Message)
            .And.Contain("Symbol=AAPL").And.Contain("Workspace=Trading")
            .And.Contain("backend unavailable");
    }

    [Theory]
    [InlineData(LogLevel.Info)]
    [InlineData(LogLevel.Warning)]
    [InlineData(LogLevel.Debug)]
    public void Log_RendersTemplatesForEachPropertyBasedLevel(LogLevel level)
    {
        LogEntryEventArgs? received = null;
        _sut.LogWritten += (_, e) => received = e;

        switch (level)
        {
            case LogLevel.Info:
                _sut.LogInfo("Selected {Symbol}", ("Symbol", "AAPL"));
                break;
            case LogLevel.Warning:
                _sut.LogWarning("Selected {Symbol}", ("Symbol", "AAPL"));
                break;
            case LogLevel.Debug:
                _sut.LogDebug("Selected {Symbol}", ("Symbol", "AAPL"));
                break;
        }

        received.Should().NotBeNull();
        received!.Level.Should().Be(level);
        received.Message.Should().Be("Selected AAPL");
        received.Properties.Should().Equal(("Symbol", "AAPL"));
    }

    [Theory]
    [InlineData("{Symbol}/{Symbol}", "AAPL", "AAPL/AAPL")]
    [InlineData("{Symbol} {Missing}", "AAPL", "AAPL {Missing}")]
    [InlineData("{{Symbol}} {Symbol}", "AAPL", "{Symbol} AAPL")]
    [InlineData("{Symbol} {Other}", "{Other}", "{Other} value")]
    [InlineData("{Symbol} {unfinished", "AAPL", "AAPL {unfinished")]
    [InlineData("{Symbol}", "", "")]
    public void LogWritten_TemplateRenderingPreservesLiteralValuesAndUnknownTokens(
        string template, string symbol, string expected)
    {
        LogEntryEventArgs? received = null;
        _sut.LogWritten += (_, e) => received = e;

        _sut.LogInfo(template, ("Symbol", symbol), ("Other", "value"));

        received.Should().NotBeNull();
        received!.Message.Should().Be(expected);
        received.Properties.Should().Contain(("Symbol", symbol));
        _sut.WrittenMessages[0].Should().Contain(expected);
    }

    [Fact]
    public void LogWritten_WithoutPropertiesPreservesLegacyLiteralBraces()
    {
        LogEntryEventArgs? received = null;
        _sut.LogWritten += (_, e) => received = e;

        _sut.LogError("Literal {Symbol} and {{braces}}");

        received.Should().NotBeNull();
        received!.Message.Should().Be("Literal {Symbol} and {{braces}}");
    }

    [Fact]
    public void LogError_ThroughInterfaceRendersPropertiesAndRetainsException()
    {
        LogEntryEventArgs? received = null;
        _sut.LogWritten += (_, e) => received = e;
        var exception = new InvalidOperationException("backend unavailable");
        Meridian.Ui.Services.Contracts.ILoggingService service = _sut;

        service.LogError("Failed to load {Symbol}", exception, ("Symbol", "AAPL"));

        received.Should().NotBeNull();
        received!.Message.Should().Be("Failed to load AAPL");
        received.Exception.Should().BeSameAs(exception);
        received.Properties.Should().Equal(("Symbol", "AAPL"));
    }

    [Fact]
    public void LogWritten_NotRaisedWhenFiltered()
    {
        _sut.MinimumLevel = LogLevel.Error;
        var eventRaised = false;
        _sut.LogWritten += (_, _) => eventRaised = true;

        _sut.LogInfo("filtered out");

        eventRaised.Should().BeFalse();
    }

    [Fact]
    public void FormatLogEntry_IncludesTimestamp()
    {
        var ts = new DateTime(2025, 6, 15, 10, 30, 45, 123, DateTimeKind.Utc);
        var result = LoggingServiceBase.FormatLogEntry(LogLevel.Info, ts, "test", null, []);

        result.Should().Contain("2025-06-15 10:30:45.123");
    }

    [Fact]
    public void FormatLogEntry_NoPropertiesNoException_CleanFormat()
    {
        var ts = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var result = LoggingServiceBase.FormatLogEntry(LogLevel.Debug, ts, "msg", null, []);

        result.Should().NotContain("{");
        result.Should().NotContain("Exception");
    }
}

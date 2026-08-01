using FluentAssertions;
using Meridian.Execution.Sdk;

namespace Meridian.Tests.Execution;

public sealed class ExecutionLogTextTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void ForLog_NullOrEmpty_PassesThrough(string? value)
    {
        ExecutionLogText.ForLog(value).Should().Be(value);
    }

    [Theory]
    [InlineData("AAPL")]
    [InlineData("BRK.B")]
    [InlineData("AAPL260116C00150000")]
    [InlineData("Position limit exceeded: projected 150 > max 100 for AAPL")]
    public void ForLog_CleanValue_IsUnchanged(string value)
    {
        ExecutionLogText.ForLog(value).Should().Be(value);
    }

    /// <summary>
    /// The reason this helper exists. A submitter who controls the symbol must not be able to end
    /// the current log line and write a second one that reads like a risk decision.
    /// </summary>
    [Theory]
    [InlineData("AAPL\nWARN: Pre-trade risk admitted order for EVIL")]
    [InlineData("AAPL\r\nWARN: approved")]
    [InlineData("AAPL\u2028WARN: approved")]
    [InlineData("AAPL\u2029WARN: approved")]
    public void ForLog_ValueCarryingALineBreak_CannotStartANewLine(string value)
    {
        var rendered = ExecutionLogText.ForLog(value);

        rendered.Should().NotBeNull();
        rendered!.Should().NotContainAny("\n", "\r", "\u2028", "\u2029");
        rendered.Should().StartWith("AAPL");
    }

    [Fact]
    public void ForLog_ControlCharacters_AreReplacedNotDropped()
    {
        // Dropping them would silently rewrite "AA\0PL" into the real symbol "AAPL".
        var rendered = ExecutionLogText.ForLog("AA\0PL\t");

        rendered.Should().Be("AA?PL?");
    }

    [Fact]
    public void ForLog_OverLongValue_IsTruncatedAndMarked()
    {
        var rendered = ExecutionLogText.ForLog(new string('A', ExecutionLogText.MaxRenderedLength + 50));

        rendered.Should().HaveLength(ExecutionLogText.MaxRenderedLength + 3);
        rendered.Should().EndWith("...");
    }

    [Fact]
    public void ForLog_ValueAtTheLengthCap_IsNotMarked()
    {
        var value = new string('A', ExecutionLogText.MaxRenderedLength);

        ExecutionLogText.ForLog(value).Should().Be(value);
    }

    /// <summary>
    /// Truncation must not become an escape hatch: a line break past the cap is cut, but one inside
    /// it still has to be neutralised.
    /// </summary>
    [Fact]
    public void ForLog_LineBreakWithinAnOverLongValue_IsStillReplaced()
    {
        var value = new string('A', 10) + "\n" + new string('B', ExecutionLogText.MaxRenderedLength);

        var rendered = ExecutionLogText.ForLog(value);

        rendered.Should().NotContain("\n");
        rendered.Should().StartWith(new string('A', 10) + "?");
        rendered.Should().EndWith("...");
    }
}

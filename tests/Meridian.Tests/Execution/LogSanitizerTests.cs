using FluentAssertions;
using Meridian.Execution.Logging;
using Xunit;

namespace Meridian.Tests.Execution;

/// <summary>
/// The execution module renders caller-supplied order text through <see cref="LogSanitizer"/>
/// before it reaches a logger, because a client order id or symbol carrying a line break would
/// otherwise render as an extra line in a text sink and let a submitter forge log entries.
/// </summary>
public sealed class LogSanitizerTests
{
    [Theory]
    [InlineData("AAPL")]
    [InlineData("BRK.B")]
    [InlineData("client-order-1")]
    public void Sanitize_LeavesOrdinaryValuesUnchanged(string value)
    {
        LogSanitizer.Sanitize(value).Should().Be(value);
    }

    [Fact]
    public void Sanitize_NullOrEmpty_ReturnsEmpty()
    {
        LogSanitizer.Sanitize(null).Should().BeEmpty();
        LogSanitizer.Sanitize(string.Empty).Should().BeEmpty();
    }

    /// <summary>
    /// The forging case: without this the second line renders as its own log record.
    /// </summary>
    [Theory]
    [InlineData("AAPL\nrisk decision: APPROVED")]
    [InlineData("AAPL\rrisk decision: APPROVED")]
    [InlineData("AAPL\r\nrisk decision: APPROVED")]
    public void Sanitize_RemovesLineBreaks(string value)
    {
        var rendered = LogSanitizer.Sanitize(value);

        rendered.Should().NotContain("\n").And.NotContain("\r");
        rendered.Should().StartWith("AAPL");
        rendered.Should().HaveLength(value.Length, "neutralizing replaces characters rather than dropping them");
    }

    [Fact]
    public void Sanitize_ReplacesOtherControlCharacters()
    {
        var rendered = LogSanitizer.Sanitize("AA\0PL\t");

        rendered.Should().Be("AA_PL_");
    }

    /// <summary>
    /// An unbounded value would otherwise let a submitter flood a sink through one order field.
    /// </summary>
    [Fact]
    public void Sanitize_TruncatesOverlongValues()
    {
        var rendered = LogSanitizer.Sanitize(new string('A', 512));

        rendered.Should().HaveLength(256);
    }

    [Fact]
    public void Sanitize_KeepsValuesAtTheLimitIntact()
    {
        var value = new string('A', 256);

        LogSanitizer.Sanitize(value).Should().Be(value);
    }

    /// <summary>
    /// Truncation must not become an escape hatch: a line break beyond the limit is dropped with
    /// the tail, and one inside it is still neutralized.
    /// </summary>
    [Fact]
    public void Sanitize_TruncatedValueStillCarriesNoLineBreak()
    {
        var value = new string('A', 10) + "\n" + new string('B', 512);

        var rendered = LogSanitizer.Sanitize(value);

        rendered.Should().NotContain("\n");
        rendered.Should().HaveLength(256);
    }
}

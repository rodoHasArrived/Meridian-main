using FluentAssertions;
using Meridian.Ui.Shared.Streaming;
using Xunit;

namespace Meridian.Tests.Ui;

public sealed class StreamTopicTests
{
    [Fact]
    public void Quotes_CanonicalizesOrderAndCase()
    {
        var a = StreamTopic.Quotes("msft,aapl");
        var b = StreamTopic.Quotes("AAPL, MSFT");

        a.Key.Should().Be("quotes:AAPL,MSFT");
        a.SymbolFilter.Should().Be("AAPL,MSFT");
        a.Should().Be(b);
        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    [Fact]
    public void Quotes_DeduplicatesSymbols()
    {
        StreamTopic.Quotes("AAPL,aapl,AAPL").Key.Should().Be("quotes:AAPL");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(",")]
    public void Quotes_EmptyFilter_IsAllQuotes(string? filter)
    {
        var topic = StreamTopic.Quotes(filter);

        topic.Should().Be(StreamTopic.AllQuotes);
        topic.Key.Should().Be(StreamTopic.AllQuotesKey);
        topic.SymbolFilter.Should().BeEmpty();
    }

    [Fact]
    public void DistinctTopics_AreNotEqual()
    {
        StreamTopic.Quotes("AAPL").Should().NotBe(StreamTopic.Quotes("MSFT"));
        StreamTopic.Quotes("AAPL").Should().NotBe(StreamTopic.AllQuotes);
        (StreamTopic.Quotes("AAPL") != StreamTopic.Quotes("MSFT")).Should().BeTrue();
    }

    [Fact]
    public void Default_BehavesAsAllQuotes()
    {
        default(StreamTopic).Should().Be(StreamTopic.AllQuotes);
        (default(StreamTopic) == StreamTopic.AllQuotes).Should().BeTrue();
        default(StreamTopic).Key.Should().Be(StreamTopic.AllQuotesKey);
        default(StreamTopic).SymbolFilter.Should().BeEmpty();
    }

    [Fact]
    public void Quotes_ArgumentAliasesSymbolFilter()
    {
        var topic = StreamTopic.Quotes("aapl,msft");

        topic.Argument.Should().Be("AAPL,MSFT");
        topic.SymbolFilter.Should().Be(topic.Argument);
    }

    [Fact]
    public void ReportRun_KeyedAndTrimmedByRunId()
    {
        var topic = StreamTopic.ReportRun("  job-1-20260501  ");

        topic.Key.Should().Be("report-run:job-1-20260501");
        topic.Argument.Should().Be("job-1-20260501");
    }

    [Fact]
    public void ReportRun_SameRunId_AreEqual()
    {
        var a = StreamTopic.ReportRun("run-1");
        var b = StreamTopic.ReportRun("run-1");

        a.Should().Be(b);
        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    [Fact]
    public void ReportRun_IsCaseSensitive_AndDistinctFromQuotes()
    {
        // Run ids are opaque — unlike symbols they are not case-folded.
        StreamTopic.ReportRun("Run-A").Should().NotBe(StreamTopic.ReportRun("run-a"));
        // A report-run topic never collides with a quotes topic of the same argument.
        StreamTopic.ReportRun("AAPL").Should().NotBe(StreamTopic.Quotes("AAPL"));
        (StreamTopic.ReportRun("run-1") != StreamTopic.ReportRun("run-2")).Should().BeTrue();
    }
}

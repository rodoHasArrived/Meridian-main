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
    }
}

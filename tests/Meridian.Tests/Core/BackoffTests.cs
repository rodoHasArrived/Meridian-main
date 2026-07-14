using FluentAssertions;
using Meridian.Core.Resilience;

namespace Meridian.Tests.Core;

public sealed class BackoffTests
{
    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 4)]
    [InlineData(6, 32)]
    public void ExponentialDelay_GrowsByPowersOfTwo(int attempt, double expectedSeconds)
    {
        var delay = Backoff.ExponentialDelay(attempt, TimeSpan.FromSeconds(1));

        delay.Should().Be(TimeSpan.FromSeconds(expectedSeconds));
    }

    [Fact]
    public void ExponentialDelay_TreatsAttemptsBelowOneAsFirstAttempt()
    {
        Backoff.ExponentialDelay(0, TimeSpan.FromSeconds(2)).Should().Be(TimeSpan.FromSeconds(2));
        Backoff.ExponentialDelay(-5, TimeSpan.FromSeconds(2)).Should().Be(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void ExponentialDelay_CapsAtMaxDelay()
    {
        var delay = Backoff.ExponentialDelay(10, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(30));

        delay.Should().Be(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void ExponentialDelay_JitterStaysWithinConfiguredFraction()
    {
        var baseDelay = TimeSpan.FromSeconds(4);

        for (var i = 0; i < 250; i++)
        {
            var delay = Backoff.ExponentialDelay(1, baseDelay, jitterFraction: 0.2);

            delay.TotalSeconds.Should().BeInRange(4 * 0.8, 4 * 1.2);
        }
    }

    [Fact]
    public void ExponentialDelay_JitterIsDeterministicWithSeededRandom()
    {
        var first = Backoff.ExponentialDelay(3, TimeSpan.FromSeconds(1), jitterFraction: 0.25, random: new Random(42));
        var second = Backoff.ExponentialDelay(3, TimeSpan.FromSeconds(1), jitterFraction: 0.25, random: new Random(42));

        first.Should().Be(second);
    }

    [Fact]
    public void ExponentialDelay_SupportsCustomMultiplier()
    {
        var delay = Backoff.ExponentialDelay(3, TimeSpan.FromSeconds(1), multiplier: 3.0);

        delay.Should().Be(TimeSpan.FromSeconds(9));
    }

    [Fact]
    public void ExponentialDelay_DoesNotOverflowOnHugeAttemptCounts()
    {
        var act = () => Backoff.ExponentialDelay(500, TimeSpan.FromSeconds(1));

        act.Should().NotThrow();
        act().Should().BePositive();
    }

    [Fact]
    public void ExponentialDelay_NeverReturnsNegativeDelay()
    {
        for (var i = 0; i < 100; i++)
        {
            Backoff.ExponentialDelay(1, TimeSpan.Zero, jitterFraction: 1.0)
                .Should().BeGreaterThanOrEqualTo(TimeSpan.Zero);
        }
    }
}

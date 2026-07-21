using FluentAssertions;
using Meridian.Backtesting.Sdk;
using Meridian.Strategies.Interfaces;
using Meridian.Strategies.Live;
using Meridian.Strategies.Live.Strategies;
using Xunit;

namespace Meridian.Tests.Strategies;

/// <summary>
/// Covers the live-catalog fallback seam: promoted runs whose strategy id has no hand-written
/// live twin resolve through <see cref="IBacktestStrategyLiveSource"/> fallbacks and reach live
/// execution wrapped in <see cref="BacktestStrategyLiveAdapter"/>.
/// </summary>
public sealed class LiveStrategyCatalogFallbackTests
{
    public sealed class FakeUserStrategy : BacktestStrategyBase
    {
        public override string Name => "fake-user-strategy";
    }

    private sealed class FakeSource : IBacktestStrategyLiveSource
    {
        private readonly string? _handledStrategyId;
        private readonly string? _failureReason;

        public FakeSource(string? handledStrategyId, string? failureReason = null)
        {
            _handledStrategyId = handledStrategyId;
            _failureReason = failureReason;
        }

        public int Calls { get; private set; }

        public bool TryCreate(
            LiveStrategyCreationContext context,
            out IBacktestStrategy? strategy,
            out string? failureReason)
        {
            Calls++;
            if (_handledStrategyId is not null &&
                string.Equals(context.StrategyId, _handledStrategyId, StringComparison.OrdinalIgnoreCase))
            {
                strategy = new FakeUserStrategy();
                failureReason = null;
                return true;
            }

            strategy = null;
            failureReason = _failureReason;
            return false;
        }
    }

    private static LiveStrategyCatalog CatalogWithSources(params IBacktestStrategyLiveSource[] sources)
    {
        var catalog = LiveStrategyCatalog.CreateDefault();
        foreach (var source in sources)
        {
            var capturedSource = source;
            catalog.RegisterFallback((LiveStrategyCreationContext context, out ILiveStrategy? strategy, out string? failureReason) =>
            {
                if (!capturedSource.TryCreate(context, out var inner, out failureReason) || inner is null)
                {
                    strategy = null;
                    return false;
                }

                strategy = new BacktestStrategyLiveAdapter(context.StrategyId, inner);
                return true;
            });
        }

        return catalog;
    }

    [Fact]
    public void TryCreate_UnknownId_ResolvesThroughFallbackAsAdapter()
    {
        var catalog = CatalogWithSources(new FakeSource("user-strategy-42"));

        var created = catalog.TryCreate("user-strategy-42", parameters: null, out var strategy, out var failureReason);

        created.Should().BeTrue();
        failureReason.Should().BeNull();
        strategy.Should().BeOfType<BacktestStrategyLiveAdapter>();
        strategy!.StrategyId.Should().Be("user-strategy-42",
            "lifecycle and audit records must stay aligned with the promoted run id");
        strategy.Name.Should().Be("fake-user-strategy");
    }

    [Fact]
    public void TryCreate_RegisteredFactory_WinsOverFallback()
    {
        var source = new FakeSource(BuyAndHoldLiveStrategy.CatalogId);
        var catalog = CatalogWithSources(source);

        var created = catalog.TryCreate(BuyAndHoldLiveStrategy.CatalogId, parameters: null, out var strategy, out _);

        created.Should().BeTrue();
        strategy.Should().BeOfType<BuyAndHoldLiveStrategy>();
        source.Calls.Should().Be(0, "fallbacks are consulted only when no factory id matches");
    }

    [Fact]
    public void TryCreate_NoFallbackMatch_SurfacesFallbackReasons()
    {
        var catalog = CatalogWithSources(new FakeSource(handledStrategyId: null, failureReason: "plugin assembly missing"));

        var created = catalog.TryCreate("unknown-strategy", parameters: null, out var strategy, out var failureReason);

        created.Should().BeFalse();
        strategy.Should().BeNull();
        failureReason.Should().Contain("unknown-strategy");
        failureReason.Should().Contain("plugin assembly missing");
    }

    [Fact]
    public void TryCreate_FirstMatchingFallbackWins()
    {
        var first = new FakeSource("shared-id");
        var second = new FakeSource("shared-id");
        var catalog = CatalogWithSources(first, second);

        var created = catalog.TryCreate("shared-id", parameters: null, out var strategy, out _);

        created.Should().BeTrue();
        strategy.Should().NotBeNull();
        first.Calls.Should().Be(1);
        second.Calls.Should().Be(0, "resolution stops at the first fallback that produces a strategy");
    }
}

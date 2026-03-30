using FluentAssertions;
using Meridian.Contracts.Domain.Models;
using Meridian.Infrastructure.Adapters.Core;
using Xunit;

namespace Meridian.Tests.Infrastructure.Providers;

/// <summary>
/// Tests for ProviderBehaviorBuilder — fluent builder for IHistoricalDataProvider.
/// </summary>
public sealed class ProviderBehaviorBuilderTests
{
    // -----------------------------------------------------------------------
    // Guard rails
    // -----------------------------------------------------------------------

    [Fact]
    public void Build_WithoutDailyBars_ThrowsInvalidOperationException()
    {
        var builder = ProviderBehaviorBuilder.Create()
            .WithName("test")
            .WithDisplayName("Test Provider");

        var act = () => builder.Build();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*WithDailyBars*");
    }

    [Fact]
    public void WithName_NullOrWhiteSpace_ThrowsArgumentException()
    {
        var builder = ProviderBehaviorBuilder.Create();

        builder.Invoking(b => b.WithName("")).Should().Throw<ArgumentException>();
        builder.Invoking(b => b.WithName("   ")).Should().Throw<ArgumentException>();
    }

    [Fact]
    public void WithDisplayName_NullOrWhiteSpace_ThrowsArgumentException()
    {
        var builder = ProviderBehaviorBuilder.Create();

        builder.Invoking(b => b.WithDisplayName("")).Should().Throw<ArgumentException>();
    }

    [Fact]
    public void WithRateLimit_ZeroMaxRequests_ThrowsArgumentOutOfRangeException()
    {
        var builder = ProviderBehaviorBuilder.Create();

        builder.Invoking(b => b.WithRateLimit(0, TimeSpan.FromMinutes(1)))
            .Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void WithDailyBars_NullDelegate_ThrowsArgumentNullException()
    {
        var builder = ProviderBehaviorBuilder.Create();

        builder.Invoking(b => b.WithDailyBars(null!))
            .Should().Throw<ArgumentNullException>();
    }

    // -----------------------------------------------------------------------
    // Identity and metadata
    // -----------------------------------------------------------------------

    [Fact]
    public void Build_WithAllMetadata_ExposesCorrectProperties()
    {
        var provider = ProviderBehaviorBuilder.Create()
            .WithName("my-provider")
            .WithDisplayName("My Provider")
            .WithDescription("A test provider")
            .WithPriority(42)
            .WithCapabilities(HistoricalDataCapabilities.BarsOnly)
            .WithRateLimit(100, TimeSpan.FromMinutes(1), TimeSpan.FromMilliseconds(200))
            .WithDailyBars(EmptyBarsDelegate)
            .Build();

        provider.Name.Should().Be("my-provider");
        provider.DisplayName.Should().Be("My Provider");
        provider.Description.Should().Be("A test provider");
        provider.Priority.Should().Be(42);
        provider.Capabilities.Should().Be(HistoricalDataCapabilities.BarsOnly);
        provider.MaxRequestsPerWindow.Should().Be(100);
        provider.RateLimitWindow.Should().Be(TimeSpan.FromMinutes(1));
        provider.RateLimitDelay.Should().Be(TimeSpan.FromMilliseconds(200));
    }

    [Fact]
    public void Build_DefaultsApplied_WhenNotConfigured()
    {
        var provider = ProviderBehaviorBuilder.Create()
            .WithDailyBars(EmptyBarsDelegate)
            .Build();

        provider.Name.Should().Be("custom");
        provider.DisplayName.Should().Be("Custom Provider");
        provider.Description.Should().BeEmpty();
        provider.Priority.Should().Be(100);
        provider.Capabilities.Should().Be(HistoricalDataCapabilities.None);
        provider.MaxRequestsPerWindow.Should().Be(int.MaxValue);
        provider.RateLimitDelay.Should().Be(TimeSpan.Zero);
    }

    // -----------------------------------------------------------------------
    // GetDailyBarsAsync delegation
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GetDailyBarsAsync_CallsSuppliedDelegate()
    {
        var callCount = 0;
        var provider = ProviderBehaviorBuilder.Create()
            .WithDailyBars((symbol, from, to, ct) =>
            {
                callCount++;
                return Task.FromResult<IReadOnlyList<HistoricalBar>>(new[]
                {
                    MakeBar(symbol, from ?? DateOnly.FromDateTime(DateTime.Today))
                });
            })
            .Build();

        var result = await provider.GetDailyBarsAsync("AAPL", null, null);

        callCount.Should().Be(1);
        result.Should().HaveCount(1);
        result[0].Symbol.Should().Be("AAPL");
    }

    [Fact]
    public async Task GetDailyBarsAsync_PassesParametersThrough()
    {
        string? capturedSymbol = null;
        DateOnly? capturedFrom = null;
        DateOnly? capturedTo = null;

        var provider = ProviderBehaviorBuilder.Create()
            .WithDailyBars((symbol, from, to, ct) =>
            {
                capturedSymbol = symbol;
                capturedFrom = from;
                capturedTo = to;
                return Task.FromResult<IReadOnlyList<HistoricalBar>>(Array.Empty<HistoricalBar>());
            })
            .Build();

        var from = new DateOnly(2024, 1, 1);
        var to = new DateOnly(2024, 12, 31);

        await provider.GetDailyBarsAsync("MSFT", from, to);

        capturedSymbol.Should().Be("MSFT");
        capturedFrom.Should().Be(from);
        capturedTo.Should().Be(to);
    }

    // -----------------------------------------------------------------------
    // GetAdjustedDailyBarsAsync — default projection
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GetAdjustedDailyBarsAsync_NoCustomDelegate_ProjectsDailyBars()
    {
        var date = new DateOnly(2024, 3, 1);
        var provider = ProviderBehaviorBuilder.Create()
            .WithDailyBars((symbol, _, _, _) =>
                Task.FromResult<IReadOnlyList<HistoricalBar>>(new[]
                {
                    MakeBar(symbol, date)
                }))
            .Build();

        var result = await provider.GetAdjustedDailyBarsAsync("SPY", null, null);

        result.Should().HaveCount(1);
        result[0].Symbol.Should().Be("SPY");
        result[0].SessionDate.Should().Be(date);
    }

    [Fact]
    public async Task GetAdjustedDailyBarsAsync_WithCustomDelegate_UsesCustomDelegate()
    {
        var customCalled = false;
        var provider = ProviderBehaviorBuilder.Create()
            .WithDailyBars(EmptyBarsDelegate)
            .WithAdjustedDailyBars((symbol, from, to, ct) =>
            {
                customCalled = true;
                return Task.FromResult<IReadOnlyList<AdjustedHistoricalBar>>(
                    Array.Empty<AdjustedHistoricalBar>());
            })
            .Build();

        await provider.GetAdjustedDailyBarsAsync("QQQ", null, null);

        customCalled.Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // IsAvailableAsync
    // -----------------------------------------------------------------------

    [Fact]
    public async Task IsAvailableAsync_NoCheckDelegate_ReturnsTrue()
    {
        var provider = ProviderBehaviorBuilder.Create()
            .WithDailyBars(EmptyBarsDelegate)
            .Build();

        var result = await provider.IsAvailableAsync();

        result.Should().BeTrue();
    }

    [Fact]
    public async Task IsAvailableAsync_CustomCheckDelegate_ReturnsProvidedValue()
    {
        var provider = ProviderBehaviorBuilder.Create()
            .WithDailyBars(EmptyBarsDelegate)
            .WithAvailabilityCheck(_ => Task.FromResult(false))
            .Build();

        var result = await provider.IsAvailableAsync();

        result.Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // IProviderMetadata bridge
    // -----------------------------------------------------------------------

    [Fact]
    public void IProviderMetadata_ProviderId_MatchesName()
    {
        var provider = ProviderBehaviorBuilder.Create()
            .WithName("bridge-test")
            .WithDailyBars(EmptyBarsDelegate)
            .Build();

        var meta = (IProviderMetadata)provider;

        meta.ProviderId.Should().Be("bridge-test");
    }

    // -----------------------------------------------------------------------
    // Fluent chaining
    // -----------------------------------------------------------------------

    [Fact]
    public void FluentChain_AllSetters_ReturnSameBuilderInstance()
    {
        var builder = ProviderBehaviorBuilder.Create();

        var b1 = builder.WithName("x");
        var b2 = b1.WithDisplayName("X");
        var b3 = b2.WithDescription("desc");
        var b4 = b3.WithPriority(1);
        var b5 = b4.WithCapabilities(HistoricalDataCapabilities.BarsOnly);
        var b6 = b5.WithRateLimit(100, TimeSpan.FromMinutes(1));
        var b7 = b6.WithDailyBars(EmptyBarsDelegate);
        var b8 = b7.WithAvailabilityCheck(_ => Task.FromResult(true));

        b1.Should().BeSameAs(builder);
        b2.Should().BeSameAs(builder);
        b3.Should().BeSameAs(builder);
        b4.Should().BeSameAs(builder);
        b5.Should().BeSameAs(builder);
        b6.Should().BeSameAs(builder);
        b7.Should().BeSameAs(builder);
        b8.Should().BeSameAs(builder);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static readonly Func<string, DateOnly?, DateOnly?, CancellationToken,
        Task<IReadOnlyList<HistoricalBar>>> EmptyBarsDelegate =
        (_, _, _, _) => Task.FromResult<IReadOnlyList<HistoricalBar>>(Array.Empty<HistoricalBar>());

    private static HistoricalBar MakeBar(string symbol, DateOnly date) =>
        new(symbol, date, 100m, 110m, 90m, 105m, 1_000_000L, "test");
}

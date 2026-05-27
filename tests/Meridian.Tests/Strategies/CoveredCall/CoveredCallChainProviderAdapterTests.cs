using FluentAssertions;
using Meridian.Backtesting.Sdk.Strategies.OptionsOverwrite;
using Meridian.Contracts.Domain.Enums;
using Meridian.Ui.Shared.Services.CoveredCall;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Meridian.Tests.Strategies.CoveredCall;

public sealed class CoveredCallChainProviderAdapterTests
{
    private static OptionCandidateInfo MakeCandidate(DateOnly expiry, decimal strike = 500m) => new(
        UnderlyingSymbol: "SPY",
        Strike: strike,
        Expiration: expiry,
        Style: OptionStyle.American,
        Multiplier: 100,
        Bid: 4.50m,
        Ask: 4.55m,
        DaysToExpiration: 30,
        OpenInterest: 5000,
        Volume: 250,
        Delta: 0.30,
        ImpliedVolatility: 0.20);

    [Fact]
    public void GetCalls_ReturnsSnapshotForKnownDate()
    {
        var scanDate = new DateOnly(2024, 1, 15);
        var snapshots = new Dictionary<DateOnly, IReadOnlyList<OptionCandidateInfo>>
        {
            [scanDate] = new[] { MakeCandidate(new DateOnly(2024, 2, 16)) }
        };
        var adapter = new CoveredCallChainProviderAdapter(snapshots, NullLogger<CoveredCallChainProviderAdapter>.Instance);

        var calls = adapter.GetCalls("SPY", scanDate, 500m);

        calls.Should().HaveCount(1);
        adapter.TotalCandidates.Should().Be(1);
    }

    [Fact]
    public void GetCalls_UnknownDateReturnsEmpty()
    {
        var adapter = new CoveredCallChainProviderAdapter(
            new Dictionary<DateOnly, IReadOnlyList<OptionCandidateInfo>>(),
            NullLogger<CoveredCallChainProviderAdapter>.Instance);

        var calls = adapter.GetCalls("SPY", new DateOnly(2024, 1, 1), 500m);

        calls.Should().BeEmpty();
    }

    [Fact]
    public void GetCalls_EmptySymbolReturnsEmpty()
    {
        var scanDate = new DateOnly(2024, 1, 15);
        var snapshots = new Dictionary<DateOnly, IReadOnlyList<OptionCandidateInfo>>
        {
            [scanDate] = new[] { MakeCandidate(new DateOnly(2024, 2, 16)) }
        };
        var adapter = new CoveredCallChainProviderAdapter(snapshots, NullLogger<CoveredCallChainProviderAdapter>.Instance);

        adapter.GetCalls("", scanDate, 500m).Should().BeEmpty();
        adapter.GetCalls("   ", scanDate, 500m).Should().BeEmpty();
    }

    [Fact]
    public void GetCalls_SymbolMismatchReturnsEmpty()
    {
        var scanDate = new DateOnly(2024, 1, 15);
        var snapshots = new Dictionary<DateOnly, IReadOnlyList<OptionCandidateInfo>>
        {
            [scanDate] = new[] { MakeCandidate(new DateOnly(2024, 2, 16)) }
        };
        var adapter = new CoveredCallChainProviderAdapter(snapshots, NullLogger<CoveredCallChainProviderAdapter>.Instance);

        adapter.GetCalls("QQQ", scanDate, 500m).Should().BeEmpty();
    }

    [Fact]
    public void GetCalls_CaseInsensitiveSymbolMatch()
    {
        var scanDate = new DateOnly(2024, 1, 15);
        var snapshots = new Dictionary<DateOnly, IReadOnlyList<OptionCandidateInfo>>
        {
            [scanDate] = new[] { MakeCandidate(new DateOnly(2024, 2, 16)) }
        };
        var adapter = new CoveredCallChainProviderAdapter(snapshots, NullLogger<CoveredCallChainProviderAdapter>.Instance);

        adapter.GetCalls("spy", scanDate, 500m).Should().HaveCount(1);
        adapter.GetCalls(" SPY ", scanDate, 500m).Should().HaveCount(1);
    }
}

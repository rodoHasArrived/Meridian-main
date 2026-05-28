using FluentAssertions;
using FsCheck.Xunit;
using Meridian.Execution.Allocation;
using Meridian.Ledger;

namespace Meridian.Tests.Execution.Enhancements;

/// <summary>
/// Tests for the Dynamic Trade Allocation Rules (Phase 6).
/// Validates <see cref="AllocationRule"/>, <see cref="ProportionalAllocationEngine"/>,
/// and <see cref="BlockTradeAllocator"/>.
/// </summary>
public sealed class AllocationEngineTests
{
    private readonly IAllocationEngine _engine = new ProportionalAllocationEngine();

    // -------------------------------------------------------------------------
    // AllocationRule
    // -------------------------------------------------------------------------

    [Fact]
    public void AllocationRule_EqualWeight_DistributesEvenly()
    {
        var rule = AllocationRule.EqualWeight("Equal Split", ["sleeveA", "sleeveB", "sleeveC"]);

        rule.SliceWeights.Should().ContainKeys("sleeveA", "sleeveB", "sleeveC");
        rule.SliceWeights.Values.Distinct().Should().ContainSingle();
    }

    [Fact]
    public void AllocationRule_NormalizedWeights_SumsToOne()
    {
        var rule = AllocationRule.Create("Custom", new Dictionary<string, decimal>
        {
            ["A"] = 1m,
            ["B"] = 2m,
            ["C"] = 1m
        });

        var normalized = rule.NormalizedWeights();
        normalized.Values.Sum().Should().BeApproximately(1m, 0.000001m);
        normalized["B"].Should().BeApproximately(0.5m, 0.000001m);
    }

    [Fact]
    public void AllocationRule_Create_ThrowsOnAllZeroWeights()
    {
        var act = () => AllocationRule.Create("Bad", new Dictionary<string, decimal>
        {
            ["A"] = 0m,
            ["B"] = 0m
        });

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void AllocationRule_Create_ThrowsOnNegativeWeights()
    {
        var act = () => AllocationRule.Create("Bad", new Dictionary<string, decimal>
        {
            ["A"] = 1m,
            ["B"] = -0.5m
        });

        act.Should().Throw<ArgumentException>();
    }

    // -------------------------------------------------------------------------
    // ProportionalAllocationEngine
    // -------------------------------------------------------------------------

    [Fact]
    public void Allocate_TwoSlices_SplitsQuantityProportionally()
    {
        var rule = AllocationRule.Create("50/50", new Dictionary<string, decimal>
        {
            ["S1"] = 1m,
            ["S2"] = 1m
        });

        var result = _engine.Allocate("AAPL", 100, 150m, rule, DateTimeOffset.UtcNow);

        result.Slices.Should().HaveCount(2);
        result.Slices.Single(s => s.DestinationId == "S1").AllocatedQuantity.Should().Be(50);
        result.Slices.Single(s => s.DestinationId == "S2").AllocatedQuantity.Should().Be(50);
        result.IsBalanced.Should().BeTrue();
    }

    [Fact]
    public void Allocate_UnevenSplit_QuantitiesSumToTotal()
    {
        var rule = AllocationRule.Create("60/40", new Dictionary<string, decimal>
        {
            ["S1"] = 3m,
            ["S2"] = 2m
        });

        var result = _engine.Allocate("MSFT", 100, 300m, rule, DateTimeOffset.UtcNow);

        result.Slices.Sum(s => s.AllocatedQuantity).Should().Be(100);
        result.IsBalanced.Should().BeTrue();
    }

    [Fact]
    public void Allocate_OddQuantity_RoundingResidualsAreSafe()
    {
        var rule = AllocationRule.EqualWeight("3-way split", ["A", "B", "C"]);

        var result = _engine.Allocate("SPY", 100, 450m, rule, DateTimeOffset.UtcNow);

        result.Slices.Sum(s => s.AllocatedQuantity).Should().Be(100);
        result.IsBalanced.Should().BeTrue();
    }

    [Fact]
    public void Allocate_ThreeWayEqualSplit_QuantitiesAreWithinOneOfEachOther()
    {
        var rule = AllocationRule.EqualWeight("3-way", ["A", "B", "C"]);

        var result = _engine.Allocate("QQQ", 100, 400m, rule, DateTimeOffset.UtcNow);

        var quantities = result.Slices.Select(s => s.AllocatedQuantity).ToList();
        var max = quantities.Max();
        var min = quantities.Min();
        (max - min).Should().BeLessThanOrEqualTo(1);
    }

    [Property(MaxTest = 200)]
    public void Scenario_MultiAccountAllocation_GeneratedBlockTradesRemainFullyAllocated(
        int quantitySeed,
        int priceSeed,
        int destinationSeed,
        int weightSeed)
    {
        var totalQuantity = BoundedLong(quantitySeed, min: 1, max: 100_000);
        var fillPrice = BoundedLong(priceSeed, min: 1, max: 1_000_000) / 100m;
        var weights = BuildGeneratedWeights(destinationSeed, weightSeed);
        var rule = AllocationRule.Create("Generated multi-account block", weights);

        var result = _engine.Allocate("SPY", totalQuantity, fillPrice, rule, DateTimeOffset.UnixEpoch);

        result.Slices.Should().HaveCount(weights.Count);
        result.Slices.Select(slice => slice.DestinationId).Should().BeEquivalentTo(weights.Keys);
        result.Slices.Should().OnlyContain(slice => slice.AllocatedQuantity >= 0);
        result.Slices.Sum(slice => slice.AllocatedQuantity).Should().Be(totalQuantity);
        result.Slices.Sum(slice => slice.ProRataValue).Should().Be(result.TotalGrossValue);
        result.Slices.Should().OnlyContain(slice => slice.ProRataValue == slice.AllocatedQuantity * fillPrice);
        result.IsBalanced.Should().BeTrue();
    }

    [Property(MaxTest = 200)]
    public void Scenario_MultiAccountAllocation_GeneratedWeightOrderDoesNotChangeBlockTotals(
        int quantitySeed,
        int priceSeed,
        int destinationSeed,
        int weightSeed)
    {
        var totalQuantity = BoundedLong(quantitySeed, min: 1, max: 100_000);
        var fillPrice = BoundedLong(priceSeed, min: 1, max: 1_000_000) / 100m;
        var weights = BuildGeneratedWeights(destinationSeed, weightSeed);
        var reversedWeights = weights.Reverse().ToDictionary(pair => pair.Key, pair => pair.Value);
        var allocatedAt = DateTimeOffset.UnixEpoch.AddSeconds(BoundedLong(weightSeed, min: 0, max: 86_400));

        var forward = _engine.Allocate(
            "SPY",
            totalQuantity,
            fillPrice,
            AllocationRule.Create("Forward generated block", weights),
            allocatedAt);
        var reversed = _engine.Allocate(
            "SPY",
            totalQuantity,
            fillPrice,
            AllocationRule.Create("Reversed generated block", reversedWeights),
            allocatedAt);

        forward.Slices.Sum(slice => slice.AllocatedQuantity).Should().Be(totalQuantity);
        reversed.Slices.Sum(slice => slice.AllocatedQuantity).Should().Be(totalQuantity);
        forward.Slices.Sum(slice => slice.ProRataValue).Should().Be(reversed.Slices.Sum(slice => slice.ProRataValue));
        forward.Slices.Select(slice => slice.DestinationId).Should().BeEquivalentTo(reversed.Slices.Select(slice => slice.DestinationId));
    }

    // -------------------------------------------------------------------------
    // BlockTradeAllocator + FundLedgerBook integration
    // -------------------------------------------------------------------------

    [Fact]
    public void BlockTradeAllocator_PostToSleeveLedgers_PostsBuyEntries()
    {
        var book = new FundLedgerBook("FUND-001");
        var rule = AllocationRule.EqualWeight("2-sleeve", ["SleevA", "SleevB"]);
        var result = _engine.Allocate("AAPL", 100, 200m, rule, DateTimeOffset.UtcNow);

        BlockTradeAllocator.PostToSleeveLedgers(book, result, isBuy: true);

        var sleeveA = book.SleeveLedger("SleevA");
        var sleeveB = book.SleeveLedger("SleevB");

        sleeveA.GetBalance(LedgerAccounts.Securities("AAPL")).Should().Be(50 * 200m);
        sleeveB.GetBalance(LedgerAccounts.Securities("AAPL")).Should().Be(50 * 200m);
    }

    [Fact]
    public void BlockTradeAllocator_PostToSleeveLedgers_PostsSellEntries()
    {
        var book = new FundLedgerBook("FUND-001");
        var rule = AllocationRule.EqualWeight("2-sleeve", ["SleevA", "SleevB"]);
        var result = _engine.Allocate("AAPL", 100, 200m, rule, DateTimeOffset.UtcNow);

        // First buy
        BlockTradeAllocator.PostToSleeveLedgers(book, result, isBuy: true);
        // Then sell
        BlockTradeAllocator.PostToSleeveLedgers(book, result, isBuy: false);

        var sleeveA = book.SleeveLedger("SleevA");
        sleeveA.GetBalance(LedgerAccounts.Cash).Should().Be(0m);
        // Securities debits == credits after buy+sell, net = 0
        sleeveA.GetBalance(LedgerAccounts.Securities("AAPL")).Should().Be(0m);
    }

    private static Dictionary<string, decimal> BuildGeneratedWeights(int destinationSeed, int weightSeed)
    {
        var destinationCount = (int)BoundedLong(destinationSeed, min: 1, max: 12);
        var weights = new Dictionary<string, decimal>(destinationCount);

        for (var index = 0; index < destinationCount; index++)
        {
            var sliceSeed = HashCode.Combine(destinationSeed, weightSeed, index);
            weights[$"SLEEVE-{index:00}"] = BoundedLong(sliceSeed, min: 1, max: 1_000);
        }

        return weights;
    }

    private static long BoundedLong(int seed, long min, long max)
    {
        var range = (ulong)(max - min + 1);
        return min + (long)((uint)seed % range);
    }
}

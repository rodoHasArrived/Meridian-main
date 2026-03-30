using FluentAssertions;
using Meridian.Application.Treasury;
using Meridian.Contracts.Treasury;

namespace Meridian.Tests.Treasury;

/// <summary>
/// Unit tests for projection rebuild orchestration in <see cref="InMemoryMoneyMarketFundService"/>.
/// Verifies checkpoint creation, idempotency of repeated rebuilds, and multi-fund rebuild coverage.
/// </summary>
public sealed class MmfRebuildTests
{
    [Fact]
    public async Task RebuildProjectionsAsync_CreatesCheckpointForFund()
    {
        var service = new InMemoryMoneyMarketFundService();
        var securityId = Guid.NewGuid();
        await service.RegisterAsync(securityId, "Rebuild Test Fund", "USD",
            null, isSweepEligible: true, weightedAverageMaturityDays: 15, hasLiquidityFee: false);

        await service.RebuildProjectionsAsync(securityId);

        var checkpoints = await service.GetRebuildCheckpointsAsync();
        checkpoints.Should().ContainSingle(c => c.SecurityId == securityId);
    }

    [Fact]
    public async Task RebuildProjectionsAsync_CheckpointContainsVersionAndSource()
    {
        var service = new InMemoryMoneyMarketFundService();
        var securityId = Guid.NewGuid();
        await service.RegisterAsync(securityId, "Source Test Fund", "USD",
            null, isSweepEligible: false, weightedAverageMaturityDays: 30, hasLiquidityFee: false);

        var before = DateTimeOffset.UtcNow;
        await service.RebuildProjectionsAsync(securityId);

        var checkpoints = await service.GetRebuildCheckpointsAsync();
        var checkpoint = checkpoints.Single(c => c.SecurityId == securityId);
        checkpoint.AggregateVersion.Should().Be(1L);
        checkpoint.RebuildSource.Should().Be("in-memory");
        checkpoint.CheckpointedAt.Should().BeOnOrAfter(before);
    }

    [Fact]
    public async Task RebuildProjectionsAsync_CalledTwice_ReplacesExistingCheckpoint()
    {
        var service = new InMemoryMoneyMarketFundService();
        var securityId = Guid.NewGuid();
        await service.RegisterAsync(securityId, "Idempotent Fund", "USD",
            null, isSweepEligible: false, weightedAverageMaturityDays: 30, hasLiquidityFee: false);

        await service.RebuildProjectionsAsync(securityId);
        await service.RebuildProjectionsAsync(securityId);

        var checkpoints = await service.GetRebuildCheckpointsAsync();
        checkpoints.Where(c => c.SecurityId == securityId).Should().ContainSingle(
            because: "repeated rebuilds replace, not duplicate, the checkpoint");
    }

    [Fact]
    public async Task RebuildProjectionsAsync_NonexistentSecurity_IsNoOp()
    {
        var service = new InMemoryMoneyMarketFundService();

        await service.RebuildProjectionsAsync(Guid.NewGuid());

        var checkpoints = await service.GetRebuildCheckpointsAsync();
        checkpoints.Should().BeEmpty();
    }

    [Fact]
    public async Task RebuildProjectionsAsync_MultipleSecurities_AllCheckpointed()
    {
        var service = new InMemoryMoneyMarketFundService();
        var ids = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };

        foreach (var id in ids)
        {
            await service.RegisterAsync(id, $"Fund {id}", "USD", null, true, 10, false);
            await service.RebuildProjectionsAsync(id);
        }

        var checkpoints = await service.GetRebuildCheckpointsAsync();
        checkpoints.Should().HaveCount(ids.Length);
        foreach (var id in ids)
            checkpoints.Should().ContainSingle(c => c.SecurityId == id);
    }

    [Fact]
    public async Task GetRebuildCheckpointsAsync_InitiallyEmpty()
    {
        var service = new InMemoryMoneyMarketFundService();
        await service.RegisterAsync(Guid.NewGuid(), "No Rebuild Fund", "USD",
            null, false, 20, false);

        var checkpoints = await service.GetRebuildCheckpointsAsync();

        checkpoints.Should().BeEmpty(
            because: "a checkpoint is only created after an explicit rebuild call");
    }

    [Fact]
    public async Task RebuildProjectionsAsync_MixedExistingAndMissing_OnlyKnownFundsCheckpointed()
    {
        var service = new InMemoryMoneyMarketFundService();
        var knownId = Guid.NewGuid();
        var unknownId = Guid.NewGuid();
        await service.RegisterAsync(knownId, "Known Fund", "USD", null, true, 20, false);

        await service.RebuildProjectionsAsync(knownId);
        await service.RebuildProjectionsAsync(unknownId); // should silently ignore

        var checkpoints = await service.GetRebuildCheckpointsAsync();
        checkpoints.Should().ContainSingle(c => c.SecurityId == knownId);
        checkpoints.Should().NotContain(c => c.SecurityId == unknownId);
    }
}

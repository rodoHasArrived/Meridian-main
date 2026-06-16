using FluentAssertions;
using Meridian.Contracts.Futures;
using Meridian.Instruments.Futures;
using Meridian.Storage.SecurityMaster;
using NSubstitute;

namespace Meridian.Tests.Futures;

public sealed class FutureProjectionServiceTests
{
    [Fact]
    public async Task GetExpiryLadderAsync_ExcludesRetiredAndExpiredContracts()
    {
        IReadOnlyList<FutureProjectionRow> rows =
        [
            MakeRow(Guid.NewGuid(), "ES", "ESZ6", new DateOnly(2026, 12, 19), false, "Active"),
            MakeRow(Guid.NewGuid(), "ES", "ESH7", new DateOnly(2027, 3, 19), false, "Active"),
            MakeRow(Guid.NewGuid(), "ES", "ESH5", new DateOnly(2025, 3, 21), false, "Retired"),
            MakeRow(Guid.NewGuid(), "ES", "ESM6", new DateOnly(2026, 6, 19), false, "Expired")
        ];

        var projectionStore = Substitute.For<IFutureReferenceProjectionStore>();
        projectionStore.GetByRootSymbolAsync("ES", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(rows));
        var service = new FutureProjectionService(Substitute.For<ISecurityMasterStore>(), projectionStore);

        var ladder = await service.GetExpiryLadderAsync("ES");

        ladder.Should().HaveCount(2);
        ladder.Select(r => r.PrimaryIdentifier).Should().ContainInOrder(["ESZ6", "ESH7"]);
    }

    [Fact]
    public async Task GetFrontMonthAsync_PrefersRollTargetOverActive()
    {
        IReadOnlyList<FutureProjectionRow> rows =
        [
            MakeRow(Guid.NewGuid(), "ES", "ESZ6", new DateOnly(2026, 12, 19), true, "RollTarget"),
            MakeRow(Guid.NewGuid(), "ES", "ESH7", new DateOnly(2027, 3, 19), false, "Active")
        ];

        var projectionStore = Substitute.For<IFutureReferenceProjectionStore>();
        projectionStore.GetByRootSymbolAsync("ES", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(rows));
        var service = new FutureProjectionService(Substitute.For<ISecurityMasterStore>(), projectionStore);

        var frontMonth = await service.GetFrontMonthAsync("ES");

        frontMonth.Should().NotBeNull();
        frontMonth!.PrimaryIdentifier.Should().Be("ESZ6");
    }

    [Fact]
    public async Task GetFrontMonthAsync_IgnoresExpiredRollTargets()
    {
        IReadOnlyList<FutureProjectionRow> rows =
        [
            MakeRow(Guid.NewGuid(), "ES", "ESM5", new DateOnly(2025, 6, 20), true, "Expired"),
            MakeRow(Guid.NewGuid(), "ES", "ESU6", new DateOnly(2026, 9, 18), false, "Active")
        ];

        var projectionStore = Substitute.For<IFutureReferenceProjectionStore>();
        projectionStore.GetByRootSymbolAsync("ES", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(rows));
        var service = new FutureProjectionService(Substitute.For<ISecurityMasterStore>(), projectionStore);

        var frontMonth = await service.GetFrontMonthAsync("ES");

        frontMonth.Should().NotBeNull();
        frontMonth!.PrimaryIdentifier.Should().Be("ESU6");
    }


    [Fact]
    public async Task GetFrontMonthAsync_IgnoresPastExpiryRollTargetsEvenWhenLifecycleIsRollTarget()
    {
        IReadOnlyList<FutureProjectionRow> rows =
        [
            MakeRow(Guid.NewGuid(), "ES", "ESM5", new DateOnly(2025, 6, 20), true, "RollTarget"),
            MakeRow(Guid.NewGuid(), "ES", "ESU6", new DateOnly(2026, 9, 18), false, "Active")
        ];

        var projectionStore = Substitute.For<IFutureReferenceProjectionStore>();
        projectionStore.GetByRootSymbolAsync("ES", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(rows));
        var service = new FutureProjectionService(Substitute.For<ISecurityMasterStore>(), projectionStore);

        var frontMonth = await service.GetFrontMonthAsync("ES");

        frontMonth.Should().NotBeNull();
        frontMonth!.PrimaryIdentifier.Should().Be("ESU6");
    }
    [Fact]
    public async Task GetExpiryLadderAsync_ReturnsEmpty_ForBlankRootSymbol()
    {
        var service = new FutureProjectionService(
            Substitute.For<ISecurityMasterStore>(),
            Substitute.For<IFutureReferenceProjectionStore>());

        var result = await service.GetExpiryLadderAsync("");

        result.Should().BeEmpty();
    }

    private static FutureProjectionRow MakeRow(Guid securityId, string root, string symbol, DateOnly expiry, bool isRollTarget, string stat)
        => new(securityId, symbol, "USD", root, expiry.ToString("MMMy"), expiry,
            50m, "Cash", isRollTarget, null, expiry, null, null, null, stat, symbol, 1);
}

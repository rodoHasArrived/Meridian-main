using FluentAssertions;
using Meridian.Instruments.Futures;
using Meridian.Contracts.Futures;
using Meridian.Storage.SecurityMaster;
using NSubstitute;

namespace Meridian.Tests.Futures;

public sealed class FutureProjectionServiceTests
{
    [Fact]
    public async Task GetExpiryLadderAsync_ExcludesRetiredAndExpiredContracts()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        IReadOnlyList<FutureProjectionRow> rows =
        [
            MakeRow(Guid.NewGuid(), "ES", "ESZ6", today.AddDays(30), false, "Active"),
            MakeRow(Guid.NewGuid(), "ES", "ESH7", today.AddDays(120), false, "Active"),
            MakeRow(Guid.NewGuid(), "ES", "ESH5", today.AddDays(-365), false, "Retired"),
            MakeRow(Guid.NewGuid(), "ES", "ESM6", today.AddDays(-30), false, "Expired")
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
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        IReadOnlyList<FutureProjectionRow> rows =
        [
            MakeRow(Guid.NewGuid(), "ES", "ESZ6", today.AddDays(30), true, "RollTarget"),
            MakeRow(Guid.NewGuid(), "ES", "ESH7", today.AddDays(120), false, "Active")
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
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        IReadOnlyList<FutureProjectionRow> rows =
        [
            MakeRow(Guid.NewGuid(), "ES", "ESM5", today.AddDays(-30), true, "Expired"),
            MakeRow(Guid.NewGuid(), "ES", "ESU6", today.AddDays(30), false, "Active")
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
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        IReadOnlyList<FutureProjectionRow> rows =
        [
            MakeRow(Guid.NewGuid(), "ES", "ESM5", today.AddDays(-30), true, "RollTarget"),
            MakeRow(Guid.NewGuid(), "ES", "ESU6", today.AddDays(30), false, "Active")
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

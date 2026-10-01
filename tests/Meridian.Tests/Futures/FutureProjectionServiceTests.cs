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
        var front = Expiry(3);
        var deferred = Expiry(6);
        var retired = Expiry(9);
        var expired = Expiry(-3);

        IReadOnlyList<FutureProjectionRow> rows =
        [
            MakeRow(Guid.NewGuid(), "ES", Code("ES", front), front, false, "Active"),
            MakeRow(Guid.NewGuid(), "ES", Code("ES", deferred), deferred, false, "Active"),
            MakeRow(Guid.NewGuid(), "ES", Code("ES", retired), retired, false, "Retired"),
            MakeRow(Guid.NewGuid(), "ES", Code("ES", expired), expired, false, "Expired")
        ];

        var projectionStore = Substitute.For<IFutureReferenceProjectionStore>();
        projectionStore.GetByRootSymbolAsync("ES", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(rows));
        var service = new FutureProjectionService(Substitute.For<ISecurityMasterStore>(), projectionStore);

        var ladder = await service.GetExpiryLadderAsync("ES");

        // The retired contract expires after both survivors, so only the lifecycle stat can exclude it.
        ladder.Should().HaveCount(2);
        ladder.Select(r => r.PrimaryIdentifier).Should().ContainInOrder([Code("ES", front), Code("ES", deferred)]);
    }

    [Fact]
    public async Task GetFrontMonthAsync_PrefersRollTargetOverActive()
    {
        var rollTarget = Expiry(3);
        var active = Expiry(6);

        IReadOnlyList<FutureProjectionRow> rows =
        [
            MakeRow(Guid.NewGuid(), "ES", Code("ES", rollTarget), rollTarget, true, "RollTarget"),
            MakeRow(Guid.NewGuid(), "ES", Code("ES", active), active, false, "Active")
        ];

        var projectionStore = Substitute.For<IFutureReferenceProjectionStore>();
        projectionStore.GetByRootSymbolAsync("ES", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(rows));
        var service = new FutureProjectionService(Substitute.For<ISecurityMasterStore>(), projectionStore);

        var frontMonth = await service.GetFrontMonthAsync("ES");

        frontMonth.Should().NotBeNull();
        frontMonth!.PrimaryIdentifier.Should().Be(Code("ES", rollTarget));
    }

    [Fact]
    public async Task GetFrontMonthAsync_IgnoresExpiredRollTargets()
    {
        var lapsed = Expiry(-15);
        var active = Expiry(3);

        IReadOnlyList<FutureProjectionRow> rows =
        [
            MakeRow(Guid.NewGuid(), "ES", Code("ES", lapsed), lapsed, true, "Expired"),
            MakeRow(Guid.NewGuid(), "ES", Code("ES", active), active, false, "Active")
        ];

        var projectionStore = Substitute.For<IFutureReferenceProjectionStore>();
        projectionStore.GetByRootSymbolAsync("ES", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(rows));
        var service = new FutureProjectionService(Substitute.For<ISecurityMasterStore>(), projectionStore);

        var frontMonth = await service.GetFrontMonthAsync("ES");

        frontMonth.Should().NotBeNull();
        frontMonth!.PrimaryIdentifier.Should().Be(Code("ES", active));
    }


    [Fact]
    public async Task GetFrontMonthAsync_IgnoresPastExpiryRollTargetsEvenWhenLifecycleIsRollTarget()
    {
        var lapsed = Expiry(-15);
        var active = Expiry(3);

        IReadOnlyList<FutureProjectionRow> rows =
        [
            MakeRow(Guid.NewGuid(), "ES", Code("ES", lapsed), lapsed, true, "RollTarget"),
            MakeRow(Guid.NewGuid(), "ES", Code("ES", active), active, false, "Active")
        ];

        var projectionStore = Substitute.For<IFutureReferenceProjectionStore>();
        projectionStore.GetByRootSymbolAsync("ES", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(rows));
        var service = new FutureProjectionService(Substitute.For<ISecurityMasterStore>(), projectionStore);

        var frontMonth = await service.GetFrontMonthAsync("ES");

        frontMonth.Should().NotBeNull();
        frontMonth!.PrimaryIdentifier.Should().Be(Code("ES", active));
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

    // FutureProjectionService filters the ladder against DateOnly.FromDateTime(DateTime.UtcNow) and
    // takes no clock, so fixed calendar expiries turn these cases into time bombs: they pass until the
    // hard-coded contract lapses and then fail on an unchanged commit. Expiries are therefore anchored
    // to the run date, in whole months so a midnight boundary cannot reclassify a contract mid-run.
    private static readonly DateOnly RunDate = DateOnly.FromDateTime(DateTime.UtcNow);

    private static DateOnly Expiry(int monthsFromRunDate) => RunDate.AddMonths(monthsFromRunDate);

    /// <summary>CME contract month codes, January through December.</summary>
    private const string MonthCodes = "FGHJKMNQUVXZ";

    /// <summary>Renders the venue-style contract code for a root and expiry, e.g. ES + Dec 2026 = ESZ6.</summary>
    private static string Code(string root, DateOnly expiry)
        => $"{root}{MonthCodes[expiry.Month - 1]}{expiry.Year % 10}";

    private static FutureProjectionRow MakeRow(Guid securityId, string root, string symbol, DateOnly expiry, bool isRollTarget, string stat)
        => new(securityId, symbol, "USD", root, expiry.ToString("MMMy"), expiry,
            50m, "Cash", isRollTarget, null, expiry, null, null, null, stat, symbol, 1);
}

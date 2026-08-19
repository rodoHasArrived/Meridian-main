using FluentAssertions;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
using Meridian.Risk;
using Meridian.Risk.Rules;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Meridian.Tests.Risk;

/// <summary>
/// Covers the account-qualified in-flight exposure reservation that makes the gross-exposure
/// check and its consumption atomic. Evaluating without reserving let orders validated beside one
/// another each measure against a settled snapshot none of them appeared in yet, so every one
/// passed and the book breached a ceiling none of them breached alone.
/// </summary>
public sealed class ExposureReservationTests
{
    private const decimal Ceiling = 100_000m;

    [Fact]
    public async Task ConcurrentOrders_CannotEachSpendTheSameHeadroom()
    {
        // Room for one order of this size, not two. Both are evaluated before either settles,
        // which is exactly the window a snapshot-only check cannot see.
        var ledger = new ExposureReservationLedger();
        var rule = CreateRule(ledger, settledGross: 40_000m);

        var first = await rule.EvaluateAndReserveAsync(CreateOrder(quantity: 400m));
        var second = await rule.EvaluateAndReserveAsync(CreateOrder(quantity: 400m));

        first.Result.IsApproved.Should().BeTrue("the settled book leaves room for the first order");
        first.Reservation.Should().NotBeNull("an approved order must take the capacity it consumes");

        second.Result.IsApproved.Should().BeFalse(
            "the first order's exposure is in flight and must be counted against the second");
        second.Reservation.Should().BeNull("a refused order takes no capacity");
    }

    [Fact]
    public async Task SettlingAReservation_ReturnsTheHeadroomExactlyOnce()
    {
        var ledger = new ExposureReservationLedger();
        var rule = CreateRule(ledger, settledGross: 40_000m);

        var reserved = await rule.EvaluateAndReserveAsync(CreateOrder(quantity: 400m));
        reserved.Reservation.Should().NotBeNull();
        ledger.HeldReservationCount.Should().Be(1);

        // Rollback returns the capacity; repeating it must not return it twice, or a later order
        // would measure against a book lighter than the one that exists.
        reserved.Reservation!.Rollback();
        reserved.Reservation.Rollback();
        reserved.Reservation.Commit();

        ledger.HeldReservationCount.Should().Be(0);
        ledger.TotalGrossInFlight.Should().Be(0m, "settling must not leave residual exposure behind");

        var afterSettlement = await rule.EvaluateAndReserveAsync(CreateOrder(quantity: 400m));
        afterSettlement.Result.IsApproved.Should().BeTrue(
            "the returned headroom must be available to the next order");
        afterSettlement.Reservation.Should().NotBeNull();
    }

    [Fact]
    public async Task InFlightExposure_CountsTowardTheBookAcrossAccounts()
    {
        // Gross is a property of the whole book, so one fund's in-flight order consumes the
        // portfolio ceiling for every other fund too.
        var ledger = new ExposureReservationLedger();
        var rule = CreateRule(ledger, settledGross: 40_000m);
        var fundA = Guid.NewGuid();
        var fundB = Guid.NewGuid();

        var first = await rule.EvaluateAndReserveAsync(CreateOrder(quantity: 400m, fundAccountId: fundA));
        first.Reservation.Should().NotBeNull();

        var second = await rule.EvaluateAndReserveAsync(CreateOrder(quantity: 400m, fundAccountId: fundB));
        second.Result.IsApproved.Should().BeFalse(
            "a portfolio-wide ceiling is shared, so another fund's in-flight order still consumes it");

        ledger.GrossInFlightFor(fundA).Should().BeGreaterThan(0m);
        ledger.GrossInFlightFor(fundB).Should().Be(0m,
            "a refused order must not leave exposure attributed to its account");
    }

    [Fact]
    public async Task InFlightExposure_IsAttributedToTheOwningAccount()
    {
        var ledger = new ExposureReservationLedger();
        var rule = CreateRule(ledger, settledGross: 0m);
        var fundA = Guid.NewGuid();
        var fundB = Guid.NewGuid();

        var reserved = await rule.EvaluateAndReserveAsync(CreateOrder(quantity: 100m, fundAccountId: fundA));
        reserved.Reservation.Should().NotBeNull();

        // Attribution is per account: another fund's book must not appear to carry this exposure,
        // which is what keeps direction-aware projection from netting one fund against another.
        ledger.GrossInFlightFor(fundA).Should().BeGreaterThan(0m);
        ledger.GrossInFlightFor(fundB).Should().Be(0m);
        ledger.GrossInFlightFor(null).Should().Be(0m,
            "an attributed order must not land in the unattributed pool");

        reserved.Reservation!.Commit();
        ledger.GrossInFlightFor(fundA).Should().Be(0m);
    }

    [Fact]
    public async Task RefusedOrder_TakesNoCapacity()
    {
        // Already over the ceiling: the order is refused on the settled book alone, and must not
        // leave exposure behind that would tighten the limit for everything after it.
        var ledger = new ExposureReservationLedger();
        var rule = CreateRule(ledger, settledGross: 99_000m);

        var refused = await rule.EvaluateAndReserveAsync(CreateOrder(quantity: 400m));

        refused.Result.IsApproved.Should().BeFalse();
        refused.Reservation.Should().BeNull();
        ledger.TotalGrossInFlight.Should().Be(0m);
        ledger.HeldReservationCount.Should().Be(0);
    }

    [Fact]
    public async Task UnconfiguredCeiling_ReservesNothing()
    {
        // With no ceiling there is no finite capacity to consume, so nothing is held and the
        // caller has nothing to settle.
        var ledger = new ExposureReservationLedger();
        var rule = new GrossExposureRule(
            Provider(grossExposure: 1_000_000m),
            () => null,
            NullLogger<GrossExposureRule>.Instance,
            ledger);

        var result = await rule.EvaluateAndReserveAsync(CreateOrder(quantity: 400m));

        result.Result.IsApproved.Should().BeTrue();
        result.Reservation.Should().BeNull();
        ledger.HeldReservationCount.Should().Be(0);
    }

    [Fact]
    public async Task EvaluateAsync_RemainsSideEffectFree()
    {
        // The non-reserving entry point is declared side-effect free by contract: the composite
        // validator evaluates every rule before deciding, so a rule that consumed capacity here
        // would charge orders that a later rule then blocked.
        var ledger = new ExposureReservationLedger();
        var rule = CreateRule(ledger, settledGross: 40_000m);

        for (var i = 0; i < 5; i++)
        {
            (await rule.EvaluateAsync(CreateOrder(quantity: 400m))).IsApproved.Should().BeTrue();
        }

        ledger.HeldReservationCount.Should().Be(0);
        ledger.TotalGrossInFlight.Should().Be(0m);
    }

    private static GrossExposureRule CreateRule(ExposureReservationLedger ledger, decimal settledGross) =>
        new(Provider(grossExposure: settledGross),
            () => Ceiling,
            NullLogger<GrossExposureRule>.Instance,
            ledger);

    private static OrderRequest CreateOrder(
        decimal quantity,
        Guid? fundAccountId = null,
        string symbol = "AAPL") => new()
        {
            Symbol = symbol,
            Side = OrderSide.Buy,
            Type = OrderType.Limit,
            Quantity = quantity,
            LimitPrice = 100m,
            FundAccountId = fundAccountId
        };

    private static StubExposureProvider Provider(decimal grossExposure) => new(new PortfolioExposureSnapshot(
        GrossExposure: grossExposure,
        NetExposure: grossExposure,
        PortfolioValue: 1_000_000m,
        SymbolExposures: new Dictionary<string, SymbolExposure>(StringComparer.OrdinalIgnoreCase),
        AsOf: DateTimeOffset.UtcNow));

    private sealed class StubExposureProvider(PortfolioExposureSnapshot snapshot) : IPortfolioExposureProvider
    {
        public PortfolioExposureSnapshot GetSnapshot() => snapshot;

        public decimal? TryGetReferencePrice(string symbol) => 100m;
    }
}

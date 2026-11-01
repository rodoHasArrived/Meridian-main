using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.SecurityMaster;
using Meridian.Instruments.FixedIncome;
using Meridian.Storage.SecurityMaster;
using NSubstitute;

namespace Meridian.Tests.FixedIncome;

public sealed class BondReferenceExpansionTests
{
    private static readonly DateOnly AsOf = new(2030, 1, 15);
    private static readonly Guid SecurityId = Guid.Parse("d7ad7a66-7cdf-4f79-b07e-a1a5e99156de");

    [Fact]
    public async Task CanonicalStructures_ArePreserved_WithoutConflatingPrincipalAndInflationFactors()
    {
        var terms = JsonSerializer.SerializeToElement(new
        {
            assetClassTermsVersion = 3,
            couponType = "InflationLinked",
            inflationIndex = "CPI-U",
            inflationBaseIndexValue = 240m,
            inflationIndexRatio = 1.2m,
            currentFactor = 0.8m,
            factorDate = "2030-01-01",
            principalSchedule = new[] { new { paymentDate = "2031-01-15", amount = 20m } },
            stepSchedule = new[] { new { effectiveDate = "2032-01-15", rate = 5m } },
            capRate = 8m,
            floorRate = 1m,
            scheduleConventions = new { accrualAdjusted = false, couponRateUnit = "Percent" },
            callSchedule = new[] { new { callDate = "2032-01-15", callPrice = 101m, exerciseStyle = "European", priceBasis = "PercentOfPar" } },
            putSchedule = new[] { new { putDate = "2033-01-15", putPrice = 100m, exerciseStyle = "European" } }
        });
        var (service, _) = Create(Row() with { CanonicalTerms = terms });

        var result = await service.GetReferenceAsOfAsync(SecurityId, AsOf);

        result!.SinkingFund!.Schedule.Should().ContainSingle().Which.Amount.Should().Be(20m);
        result.InflationLinked!.CurrentFactor.Should().Be(1.2m);
        result.PrincipalFactor!.CurrentFactor.Should().Be(0.8m);
        result.PrincipalFactor.FactorDate.Should().Be(new DateOnly(2030, 1, 1));
        result.CouponStructure!.CapRate.Should().Be(8m);
        result.CouponStructure.FloorRate.Should().Be(1m);
        result.CouponStructure.StepSchedule!.Value.GetArrayLength().Should().Be(1);
        result.ScheduleConventions!.CouponRateUnit.Should().Be("Percent");
        result.CallSchedule!.Value[0].GetProperty("callPrice").GetDecimal().Should().Be(101m);
        result.PutSchedule!.Value.GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task UndeclaredExerciseContainers_AreNotPromotedToCanonicalSchedules()
    {
        var (service, _) = Create(Row() with
        {
            CanonicalTerms = JsonSerializer.SerializeToElement(new
            {
                assetClassTermsVersion = 1,
                callSchedule = new[] { new { callDate = "2031-01-15" } },
                scheduleConventions = new { couponRateUnit = "Percent" }
            })
        });
        var result = await service.GetReferenceAsOfAsync(SecurityId, AsOf);
        result!.CallSchedule.Should().BeNull();
        result.ScheduleConventions.Should().BeNull();
    }

    [Theory]
    [InlineData("2020-01-01", BondLifecycleStat.WhenIssued)]
    [InlineData("2028-01-01", BondLifecycleStat.Active)]
    [InlineData("2030-01-01", BondLifecycleStat.Callable)]
    [InlineData("2036-01-01", BondLifecycleStat.Matured)]
    public async Task Lifecycle_IsDerivedFromExplicitDate_RegardlessOfStaleStoredStatus(string date, BondLifecycleStat expected)
    {
        var (service, _) = Create(Row());
        var result = await service.GetReferenceAsOfAsync(SecurityId, DateOnly.Parse(date));
        result!.Lifecycle!.LifecycleStat.Should().Be(expected);
    }

    [Theory]
    [InlineData("999")]
    [InlineData("+1")]
    [InlineData("Unknown")]
    public async Task InvalidLifecycleText_DoesNotBecomeAnUndefinedOrNumericEnum(string raw)
    {
        var (service, _) = Create(Row() with { LifecycleStat = raw });
        (await service.GetReferenceAsOfAsync(SecurityId, AsOf))!.Lifecycle.Should().BeNull();
    }

    [Fact]
    public async Task Paging_UsesLastVisibleRowAsKeyset_AndRejectsCrossQueryCursor()
    {
        var first = Row();
        var second = Row() with { SecurityId = Guid.NewGuid(), MaturityDate = new DateOnly(2036, 1, 15) };
        var (service, store) = Create(first);
        store.GetIssuerLadderPageAsync("Issuer", 1, null, null, Arg.Any<CancellationToken>())
            .Returns(new[] { first, second });
        var page = await service.GetIssuerLadderPageAsync("Issuer", 1, asOf: AsOf);
        page.Items.Should().ContainSingle().Which.SecurityId.Should().Be(SecurityId);
        page.NextCursor.Should().NotBeNull();
        store.GetIssuerLadderPageAsync("Issuer", 1, first.MaturityDate, first.SecurityId, Arg.Any<CancellationToken>())
            .Returns(new[] { second });
        var last = await service.GetIssuerLadderPageAsync("Issuer", 1, page.NextCursor, AsOf);
        last.Items.Should().ContainSingle().Which.SecurityId.Should().Be(second.SecurityId);
        last.NextCursor.Should().BeNull();
        var wrongQuery = () => service.GetIssuerLadderPageAsync("Other", 1, page.NextCursor, AsOf);
        await wrongQuery.Should().ThrowAsync<ArgumentException>();
    }

    [Theory]
    [InlineData(0, null)]
    [InlineData(501, null)]
    [InlineData(1, "invalid")]
    public async Task InvalidPageRequests_AreRejectedBeforeStorage(int size, string? cursor)
    {
        var (service, store) = Create(Row());
        var act = () => service.GetIssuerLadderPageAsync("Issuer", size, cursor, AsOf);
        await act.Should().ThrowAsync<ArgumentException>();
        await store.DidNotReceiveWithAnyArgs().GetIssuerLadderPageAsync(default!, default, default, default, default);
    }

    private static (BondProjectionService Service, IBondReferenceProjectionStore Store) Create(BondProjectionRow row)
    {
        var master = Substitute.For<ISecurityMasterStore>();
        master.GetProjectionAsync(SecurityId, Arg.Any<CancellationToken>()).Returns(new SecurityProjectionRecord(
            SecurityId, "Bond", SecurityStatusDto.Active, "Bond", "USD", "Ticker", "BOND",
            JsonSerializer.SerializeToElement(new { }), JsonSerializer.SerializeToElement(new { }),
            JsonSerializer.SerializeToElement(new { }), 1, DateTimeOffset.UnixEpoch, null, [], []));
        var store = Substitute.For<IBondReferenceProjectionStore>();
        store.GetBondAsync(SecurityId, Arg.Any<CancellationToken>()).Returns(row);
        return (new BondProjectionService(master, store), store);
    }

    private static BondProjectionRow Row() => new(SecurityId, "Bond", "USD", "BOND", "Issuer", "Senior",
        new DateOnly(2035, 1, 15), "Active", new DateOnly(2024, 1, 15), new DateOnly(2029, 1, 15), true,
        "30/360", 2, "WEEKENDS", "Fixed", 5m, null, null, 1, Par: 100m, PaymentFrequency: "SemiAnnual");
}

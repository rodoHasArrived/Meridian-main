using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SecurityMasterQueryContract = Meridian.Application.SecurityMaster.ISecurityMasterQueryService;

namespace Meridian.Tests.Application;

public sealed class SecurityMasterCashFlowServiceTests
{
    [Fact]
    public async Task GetProjectionAsync_CalculatedBullet_ShouldGenerateCouponAndMaturityScheduleFromRetainedTerms()
    {
        var securityId = Guid.Parse("11111111-aaaa-aaaa-aaaa-111111111111");
        var asOf = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var store = Substitute.For<ISecurityMasterCashFlowStore>();
        store.GetSourceAsync(securityId, Arg.Any<CancellationToken>())
            .Returns(new SecurityCashFlowSourceDto(
                securityId,
                StructuredCashFlowSourceKind.CalculatedBullet,
                DateTimeOffset.UtcNow,
                false,
                null,
                null));
        var query = Substitute.For<SecurityMasterQueryContract>();
        query.GetByIdAsync(securityId, Arg.Any<CancellationToken>())
            .Returns(BuildSecurity(
                securityId,
                JsonSerializer.SerializeToElement(new
                {
                    issueDate = asOf,
                    maturityDate = asOf.AddYears(1),
                    par = 100m,
                    couponRate = 6m,
                    paymentFrequency = "SemiAnnual",
                    dayCountConvention = "30/360"
                })));
        var service = new SecurityMasterCashFlowService(
            store,
            Array.Empty<IStructuredCashFlowProvider>(),
            query,
            NullLogger<SecurityMasterCashFlowService>.Instance);

        var projection = await service.GetProjectionAsync(securityId, StructuredCashFlowScenario.Base);

        projection.Should().NotBeNull();
        projection!.SourceKind.Should().Be(StructuredCashFlowSourceKind.CalculatedBullet);
        projection.Schedule.Should().HaveCount(2);
        projection.Schedule.Should().Contain(static row => row.InterestAmount == 3m && row.PrincipalAmount == 0m);
        projection.Schedule.Last().Should().Match<StructuredCashFlowScheduleEntry>(row =>
            row.InterestAmount == 3m &&
            row.PrincipalAmount == 100m &&
            row.Factor == 0m);
    }

    [Fact]
    public async Task GetProjectionAsync_FutureDatedScalarFactor_DoesNotReduceTodaysBalance()
    {
        // A scalar currentFactor dated NEXT MONTH does not describe today's balance: applying it
        // would understate today's outstanding — and every projected interest row and the
        // maturity principal — by the factor's paydown. Until the factor's date arrives, the
        // unfactored balance (or the latest eligible schedule point) governs.
        var securityId = Guid.Parse("33333333-cccc-cccc-cccc-333333333333");
        var asOf = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var store = Substitute.For<ISecurityMasterCashFlowStore>();
        store.GetSourceAsync(securityId, Arg.Any<CancellationToken>())
            .Returns(new SecurityCashFlowSourceDto(
                securityId,
                StructuredCashFlowSourceKind.CalculatedBullet,
                DateTimeOffset.UtcNow,
                false,
                null,
                null));
        var query = Substitute.For<SecurityMasterQueryContract>();
        query.GetByIdAsync(securityId, Arg.Any<CancellationToken>())
            .Returns(BuildSecurity(
                securityId,
                JsonSerializer.SerializeToElement(new
                {
                    issueDate = asOf,
                    maturityDate = asOf.AddYears(1),
                    par = 100m,
                    couponRate = 6m,
                    paymentFrequency = "SemiAnnual",
                    dayCountConvention = "30/360",
                    currentFactor = 0.8m,
                    factorDate = asOf.AddMonths(1)
                })));
        var service = new SecurityMasterCashFlowService(
            store,
            Array.Empty<IStructuredCashFlowProvider>(),
            query,
            NullLogger<SecurityMasterCashFlowService>.Instance);

        var projection = await service.GetProjectionAsync(securityId, StructuredCashFlowScenario.Base);

        projection.Should().NotBeNull();
        projection!.Schedule.Last().PrincipalAmount.Should().Be(100m,
            "a factor dated after the projection as-of must not reduce today's outstanding");
    }

    [Fact]
    public async Task GetProjectionAsync_AssertedEmptyScheduleOnSinker_PaysPrincipalAtMaturity()
    {
        // principalSchedule: [] is an ASSERTED contractual bullet structure, not absence: a bullet
        // record misassigned CalculatedSinker must not synthesize equal instalments its own
        // schedule contradicts — principal pays at maturity, and projected interest accrues on
        // the full balance until then.
        var securityId = Guid.Parse("44444444-dddd-dddd-dddd-444444444444");
        var asOf = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var store = Substitute.For<ISecurityMasterCashFlowStore>();
        store.GetSourceAsync(securityId, Arg.Any<CancellationToken>())
            .Returns(new SecurityCashFlowSourceDto(
                securityId,
                StructuredCashFlowSourceKind.CalculatedSinker,
                DateTimeOffset.UtcNow,
                false,
                null,
                null));
        var query = Substitute.For<SecurityMasterQueryContract>();
        query.GetByIdAsync(securityId, Arg.Any<CancellationToken>())
            .Returns(BuildSecurity(
                securityId,
                JsonSerializer.SerializeToElement(new
                {
                    issueDate = asOf,
                    maturityDate = asOf.AddYears(1),
                    par = 100m,
                    couponRate = 6m,
                    paymentFrequency = "SemiAnnual",
                    dayCountConvention = "30/360",
                    principalSchedule = Array.Empty<object>()
                })));
        var service = new SecurityMasterCashFlowService(
            store,
            Array.Empty<IStructuredCashFlowProvider>(),
            query,
            NullLogger<SecurityMasterCashFlowService>.Instance);

        var projection = await service.GetProjectionAsync(securityId, StructuredCashFlowScenario.Base);

        projection.Should().NotBeNull();
        projection!.Schedule.Should().HaveCount(2);
        projection.Schedule[0].PrincipalAmount.Should().Be(0m,
            "the asserted-empty schedule contractually pays no interim principal");
        projection.Schedule.Last().PrincipalAmount.Should().Be(100m,
            "the asserted bullet structure pays all principal at maturity");
    }

    [Fact]
    public async Task GetProjectionAsync_CalculatedSinker_ShouldAmortizePrincipalAcrossScheduleDates()
    {
        var securityId = Guid.Parse("22222222-bbbb-bbbb-bbbb-222222222222");
        var asOf = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var store = Substitute.For<ISecurityMasterCashFlowStore>();
        store.GetSourceAsync(securityId, Arg.Any<CancellationToken>())
            .Returns(new SecurityCashFlowSourceDto(
                securityId,
                StructuredCashFlowSourceKind.CalculatedSinker,
                DateTimeOffset.UtcNow,
                false,
                null,
                null));
        var query = Substitute.For<SecurityMasterQueryContract>();
        query.GetByIdAsync(securityId, Arg.Any<CancellationToken>())
            .Returns(BuildSecurity(
                securityId,
                JsonSerializer.SerializeToElement(new
                {
                    issueDate = asOf,
                    maturityDate = asOf.AddYears(1),
                    originalFace = 120m,
                    couponRate = 0.12m,
                    paymentFrequency = "Quarterly",
                    dayCountConvention = "ACT/360"
                })));
        var service = new SecurityMasterCashFlowService(
            store,
            Array.Empty<IStructuredCashFlowProvider>(),
            query,
            NullLogger<SecurityMasterCashFlowService>.Instance);

        var projection = await service.GetProjectionAsync(securityId, StructuredCashFlowScenario.Down100);

        projection.Should().NotBeNull();
        projection!.SourceKind.Should().Be(StructuredCashFlowSourceKind.CalculatedSinker);
        projection.Schedule.Should().HaveCount(4);
        projection.Schedule.Select(static row => row.PrincipalAmount).Should().OnlyContain(static amount => amount > 0m);
        projection.Schedule.Sum(static row => row.PrincipalAmount).Should().Be(120m);
        projection.Schedule.Last().Factor.Should().Be(0m);
        projection.Schedule.First().InterestAmount.Should().BeGreaterThan(0m);
    }

    [Theory]
    [InlineData(StructuredCashFlowSourceKind.CalculatedBullet)]
    [InlineData(StructuredCashFlowSourceKind.CalculatedSinker)]
    public async Task GetProjectionAsync_ContractualPrincipalSchedule_ShouldOverrideSyntheticPrincipal(
        StructuredCashFlowSourceKind sourceKind)
    {
        var securityId = Guid.NewGuid();
        var nextMonth = DateOnly.FromDateTime(DateTime.UtcNow.Date).AddMonths(1);
        var issueDate = new DateOnly(nextMonth.Year, nextMonth.Month, 1);
        var firstPrincipalDate = issueDate.AddMonths(3);
        var firstCouponDate = issueDate.AddMonths(6);
        var secondPrincipalDate = issueDate.AddMonths(9);
        var maturity = issueDate.AddYears(1);
        var service = BuildService(
            StoreWith(securityId, sourceKind),
            QueryWith(securityId, JsonSerializer.SerializeToElement(new
            {
                issueDate,
                maturityDate = maturity,
                par = 100m,
                couponRate = 6m,
                paymentFrequency = "SemiAnnual",
                dayCountConvention = "30/360",
                principalSchedule = new object[]
                {
                    new { paymentDate = secondPrincipalDate, amount = 20m },
                    new { paymentDate = firstPrincipalDate, amount = 30m }
                }
            })));

        var projection = await service.GetProjectionAsync(securityId, StructuredCashFlowScenario.Base);

        projection.Should().NotBeNull();
        projection!.Schedule
            .Select(static row => DateOnly.FromDateTime(row.PeriodDate.UtcDateTime.Date))
            .Should().Equal(firstPrincipalDate, firstCouponDate, secondPrincipalDate, maturity);
        projection.Schedule.Select(static row => row.PrincipalAmount)
            .Should().Equal(30m, 0m, 20m, 50m);
        projection.Schedule.Select(static row => row.InterestAmount)
            .Should().Equal(new[] { 0m, 2.55m, 0m, 1.8m },
                "interest accrues on each balance segment and is paid only on coupon dates");
        projection.Schedule.Last().Factor.Should().Be(0m);
        projection.TermsUsed!.PrincipalSchedule.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetProjectionAsync_PrincipalScheduleWithoutPrincipalBasis_IsNotTreatedAsContractual()
    {
        // DirectLoan-shaped terms: a contractual schedule but no par/face/notional. The 100-unit
        // fallback basis would cap a real 1,000,000 instalment at 100 and feed that distortion to
        // the ledger bridge, so without a resolvable basis the schedule is not treated as
        // contractual and the record keeps the calculated bullet walk.
        var securityId = Guid.NewGuid();
        var nextMonth = DateOnly.FromDateTime(DateTime.UtcNow.Date).AddMonths(1);
        var issueDate = new DateOnly(nextMonth.Year, nextMonth.Month, 1);
        var instalmentDate = issueDate.AddMonths(6);
        var maturity = issueDate.AddYears(1);
        var service = BuildService(
            StoreWith(securityId, StructuredCashFlowSourceKind.CalculatedBullet),
            QueryWith(securityId, JsonSerializer.SerializeToElement(new
            {
                issueDate,
                maturityDate = maturity,
                couponRate = 6m,
                paymentFrequency = "SemiAnnual",
                dayCountConvention = "30/360",
                principalSchedule = new object[]
                {
                    new { paymentDate = instalmentDate, amount = 1_000_000m }
                }
            })));

        var projection = await service.GetProjectionAsync(securityId, StructuredCashFlowScenario.Base);

        projection.Should().NotBeNull();
        projection!.Schedule.Sum(static row => row.PrincipalAmount).Should().Be(100m,
            "without a resolvable principal basis the record projects as a calculated bullet at the fallback basis");
        projection.Schedule.Should().NotContain(row => row.PrincipalAmount == 1_000_000m,
            "the schedule must not project instalments it has no basis to cap correctly");
    }

    [Fact]
    public async Task GetProjectionAsync_PrincipalPaymentAfterFactorAsOf_ReducesTheOpeningBalance()
    {
        // A DATED factor reflects principal events only up to its own as-of date. Here the factor
        // (0.8, as of five months ago) predates a completed contractual payment of 10 (four months
        // ago): today's opening balance is 100 x 0.8 - 10 = 70, not 80 — treating the stale factor
        // as current overstates later interest, maturity principal, and ledger postings.
        var securityId = Guid.Parse("55555555-eeee-eeee-eeee-555555555555");
        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var issueDate = new DateOnly(today.Year, today.Month, 1).AddMonths(-6);
        var factorDate = issueDate.AddMonths(1);
        var completedPaymentDate = issueDate.AddMonths(2);
        var maturity = issueDate.AddMonths(24);
        var store = Substitute.For<ISecurityMasterCashFlowStore>();
        store.GetSourceAsync(securityId, Arg.Any<CancellationToken>())
            .Returns(new SecurityCashFlowSourceDto(
                securityId,
                StructuredCashFlowSourceKind.CalculatedBullet,
                DateTimeOffset.UtcNow,
                false,
                null,
                null));
        var query = Substitute.For<SecurityMasterQueryContract>();
        query.GetByIdAsync(securityId, Arg.Any<CancellationToken>())
            .Returns(BuildSecurity(
                securityId,
                JsonSerializer.SerializeToElement(new
                {
                    issueDate,
                    maturityDate = maturity,
                    par = 100m,
                    couponRate = 6m,
                    paymentFrequency = "SemiAnnual",
                    dayCountConvention = "30/360",
                    factorScheduleEntries = new object[]
                    {
                        new { asOfDate = factorDate, factor = 0.8m }
                    },
                    principalSchedule = new object[]
                    {
                        new { paymentDate = completedPaymentDate, amount = 10m }
                    }
                })));
        var service = new SecurityMasterCashFlowService(
            store,
            Array.Empty<IStructuredCashFlowProvider>(),
            query,
            NullLogger<SecurityMasterCashFlowService>.Instance);

        var projection = await service.GetProjectionAsync(securityId, StructuredCashFlowScenario.Base);

        projection.Should().NotBeNull();
        projection!.Schedule.Sum(static row => row.PrincipalAmount).Should().Be(70m,
            "the completed contractual payment dated after the factor's as-of must reduce the opening balance");
    }

    [Fact]
    public async Task GetProjectionAsync_PrincipalPaymentAfterScalarFactorDate_ReducesTheOpeningBalance()
    {
        // Same dated-factor rule for a SCALAR currentFactor with a retained factorDate: the 0.8
        // factor is evidence through its own date only, so the completed payment of 10 dated after
        // it still reduces today's opening balance (100 x 0.8 - 10 = 70). Only a genuinely undated
        // scalar is assumed current.
        var securityId = Guid.Parse("66666666-eeee-eeee-eeee-666666666666");
        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var issueDate = new DateOnly(today.Year, today.Month, 1).AddMonths(-6);
        var factorDate = issueDate.AddMonths(1);
        var completedPaymentDate = issueDate.AddMonths(2);
        var maturity = issueDate.AddMonths(24);
        var store = Substitute.For<ISecurityMasterCashFlowStore>();
        store.GetSourceAsync(securityId, Arg.Any<CancellationToken>())
            .Returns(new SecurityCashFlowSourceDto(
                securityId,
                StructuredCashFlowSourceKind.CalculatedBullet,
                DateTimeOffset.UtcNow,
                false,
                null,
                null));
        var query = Substitute.For<SecurityMasterQueryContract>();
        query.GetByIdAsync(securityId, Arg.Any<CancellationToken>())
            .Returns(BuildSecurity(
                securityId,
                JsonSerializer.SerializeToElement(new
                {
                    issueDate,
                    maturityDate = maturity,
                    par = 100m,
                    couponRate = 6m,
                    paymentFrequency = "SemiAnnual",
                    dayCountConvention = "30/360",
                    currentFactor = 0.8m,
                    factorDate,
                    principalSchedule = new object[]
                    {
                        new { paymentDate = completedPaymentDate, amount = 10m }
                    }
                })));
        var service = new SecurityMasterCashFlowService(
            store,
            Array.Empty<IStructuredCashFlowProvider>(),
            query,
            NullLogger<SecurityMasterCashFlowService>.Instance);

        var projection = await service.GetProjectionAsync(securityId, StructuredCashFlowScenario.Base);

        projection.Should().NotBeNull();
        projection!.Schedule.Sum(static row => row.PrincipalAmount).Should().Be(70m,
            "the completed contractual payment dated after the scalar factor's retained date must reduce the opening balance");
    }

    [Fact]
    public async Task GetProjectionAsync_ProfileNestedScalarFactorDate_ReducesTheOpeningBalance()
    {
        // A profile-backed record persists its governed scalar terms beneath profileFields, where
        // the term resolver reads the factor and schedule from. The factor-date lookup must walk
        // the SAME nested-first sources: probing only the envelope root would miss the retained
        // date, treat the dated 0.8 factor as current, and skip the completed-payment deduction.
        var securityId = Guid.Parse("77777777-eeee-eeee-eeee-777777777777");
        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var issueDate = new DateOnly(today.Year, today.Month, 1).AddMonths(-6);
        var factorDate = issueDate.AddMonths(1);
        var completedPaymentDate = issueDate.AddMonths(2);
        var maturity = issueDate.AddMonths(24);
        var store = Substitute.For<ISecurityMasterCashFlowStore>();
        store.GetSourceAsync(securityId, Arg.Any<CancellationToken>())
            .Returns(new SecurityCashFlowSourceDto(
                securityId,
                StructuredCashFlowSourceKind.CalculatedBullet,
                DateTimeOffset.UtcNow,
                false,
                null,
                null));
        var query = Substitute.For<SecurityMasterQueryContract>();
        query.GetByIdAsync(securityId, Arg.Any<CancellationToken>())
            .Returns(BuildSecurity(
                securityId,
                JsonSerializer.SerializeToElement(new
                {
                    customProfileId = "structured-credit-io-po",
                    profileVersion = 1,
                    profileFields = new
                    {
                        issueDate,
                        maturityDate = maturity,
                        par = 100m,
                        couponRate = 6m,
                        paymentFrequency = "SemiAnnual",
                        dayCountConvention = "30/360",
                        currentFactor = 0.8m,
                        factorDate,
                        principalSchedule = new object[]
                        {
                            new { paymentDate = completedPaymentDate, amount = 10m }
                        }
                    }
                })));
        var service = new SecurityMasterCashFlowService(
            store,
            Array.Empty<IStructuredCashFlowProvider>(),
            query,
            NullLogger<SecurityMasterCashFlowService>.Instance);

        var projection = await service.GetProjectionAsync(securityId, StructuredCashFlowScenario.Base);

        projection.Should().NotBeNull();
        projection!.Schedule.Sum(static row => row.PrincipalAmount).Should().Be(70m,
            "the profile-nested factor date is dated evidence exactly as a root-level one is");
    }

    [Fact]
    public async Task GetProjectionAsync_MonthEndIssueDate_ShouldAnchorScheduleWithoutStubPeriod()
    {
        // Regression: payment dates used to compound AddMonths from the previous payment date.
        // AddMonths clamps a month-end anchor into shorter months (31 Jan plus three months is
        // 30 Apr), and compounding that clamp walked the schedule earlier every period until the
        // final coupon landed one day before maturity and emitted a spurious one-day stub, so a
        // one-year quarterly bond produced five payments instead of four.
        //
        // The issue date is pinned to the end of the current month so this exercises the clamping
        // path on every calendar day rather than only when the suite happens to run on a month end.
        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var issueDate = new DateOnly(today.Year, today.Month, DateTime.DaysInMonth(today.Year, today.Month));
        var maturityDate = issueDate.AddMonths(12);
        var securityId = Guid.Parse("44444444-dddd-dddd-dddd-444444444444");
        var store = Substitute.For<ISecurityMasterCashFlowStore>();
        store.GetSourceAsync(securityId, Arg.Any<CancellationToken>())
            .Returns(new SecurityCashFlowSourceDto(
                securityId,
                StructuredCashFlowSourceKind.CalculatedBullet,
                DateTimeOffset.UtcNow,
                false,
                null,
                null));
        var query = Substitute.For<SecurityMasterQueryContract>();
        query.GetByIdAsync(securityId, Arg.Any<CancellationToken>())
            .Returns(BuildSecurity(
                securityId,
                JsonSerializer.SerializeToElement(new
                {
                    issueDate,
                    maturityDate,
                    par = 100m,
                    couponRate = 4m,
                    paymentFrequency = "Quarterly",
                    dayCountConvention = "30/360"
                })));
        var service = BuildService(store, query);

        var projection = await service.GetProjectionAsync(securityId, StructuredCashFlowScenario.Base);

        projection.Should().NotBeNull();
        projection!.Schedule.Should().HaveCount(4);
        projection.Schedule
            .Select(static row => DateOnly.FromDateTime(row.PeriodDate.UtcDateTime.Date))
            .Should().Equal(
                issueDate.AddMonths(3),
                issueDate.AddMonths(6),
                issueDate.AddMonths(9),
                maturityDate);
        // Principal is repaid once, at maturity, with no trailing stub period.
        projection.Schedule.Should().ContainSingle(static row => row.PrincipalAmount > 0m);
        projection.Schedule.Last().PrincipalAmount.Should().Be(100m);
        projection.Schedule.Last().Factor.Should().Be(0m);
        // Every accrual period is a whole quarter, so no coupon collapses to a stub. A quarter of a
        // 4% coupon on par 100 is ~1.00 (day-count fractions vary slightly with month length, so
        // this is a floor rather than an equality); the one-day stub the old schedule produced
        // accrued about 0.01.
        projection.Schedule.Should().OnlyContain(static row => row.InterestAmount > 0.9m);
    }

    [Fact]
    public async Task GetProjectionAsync_MidPeriod_ShouldAccrueThroughCompletedPrincipalWithoutReemittingIt()
    {
        var securityId = Guid.NewGuid();
        var asOf = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var issueMonth = asOf.AddMonths(-4);
        var issueDate = new DateOnly(issueMonth.Year, issueMonth.Month, 1);
        var completedPrincipalDate = issueDate.AddMonths(3);
        var firstCouponDate = issueDate.AddMonths(6);
        var maturity = issueDate.AddYears(1);
        var service = BuildService(
            StoreWith(securityId, StructuredCashFlowSourceKind.CalculatedSinker),
            QueryWith(securityId, JsonSerializer.SerializeToElement(new
            {
                issueDate,
                maturityDate = maturity,
                par = 100m,
                couponRate = 6m,
                paymentFrequency = "SemiAnnual",
                dayCountConvention = "30/360",
                principalSchedule = new object[]
                {
                    new { paymentDate = completedPrincipalDate, amount = 30m }
                }
            })));

        var projection = await service.GetProjectionAsync(securityId, StructuredCashFlowScenario.Base);

        projection.Should().NotBeNull();
        projection!.Schedule.Should().NotContain(row =>
            DateOnly.FromDateTime(row.PeriodDate.UtcDateTime.Date) == completedPrincipalDate);
        projection.Schedule.First().Should().Match<StructuredCashFlowScheduleEntry>(row =>
            DateOnly.FromDateTime(row.PeriodDate.UtcDateTime.Date) == firstCouponDate
            && row.PrincipalAmount == 0m
            && row.InterestAmount == 2.55m);
        projection.Schedule.Last().PrincipalAmount.Should().Be(70m);
    }

    [Fact]
    public async Task GetProjectionAsync_StaleSource_ShouldFlagStalenessButStillReturnForDisplay()
    {
        var securityId = Guid.Parse("33333333-cccc-cccc-cccc-333333333333");
        var asOf = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var store = Substitute.For<ISecurityMasterCashFlowStore>();
        store.GetSourceAsync(securityId, Arg.Any<CancellationToken>())
            .Returns(new SecurityCashFlowSourceDto(
                securityId,
                StructuredCashFlowSourceKind.CalculatedBullet,
                DateTimeOffset.UtcNow.AddDays(-30),
                false,
                null,
                null));
        var query = Substitute.For<SecurityMasterQueryContract>();
        query.GetByIdAsync(securityId, Arg.Any<CancellationToken>())
            .Returns(BuildSecurity(
                securityId,
                JsonSerializer.SerializeToElement(new
                {
                    issueDate = asOf,
                    maturityDate = asOf.AddYears(1),
                    par = 100m,
                    couponRate = 6m,
                    paymentFrequency = "SemiAnnual",
                    dayCountConvention = "30/360"
                })));
        var service = BuildService(store, query);

        var projection = await service.GetProjectionAsync(securityId, StructuredCashFlowScenario.Base);

        projection.Should().NotBeNull();
        projection!.Staleness.Should().Be(StructuredCashFlowStaleness.Stale);
        projection.SourceLastUpdatedUtc.Should().NotBeNull();
        // Stale projections are still returned so the UI can render a flagged view.
        projection.Schedule.Should().NotBeEmpty();
    }

    [Fact]
    public async Task GetProjectionAsync_WithTypedFactorSchedule_ShouldExposeScheduleAndSeedOutstanding()
    {
        var securityId = Guid.Parse("44444444-dddd-dddd-dddd-444444444444");
        var asOf = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var store = Substitute.For<ISecurityMasterCashFlowStore>();
        store.GetSourceAsync(securityId, Arg.Any<CancellationToken>())
            .Returns(new SecurityCashFlowSourceDto(
                securityId,
                StructuredCashFlowSourceKind.CalculatedSinker,
                DateTimeOffset.UtcNow,
                false,
                null,
                null));
        var query = Substitute.For<SecurityMasterQueryContract>();
        query.GetByIdAsync(securityId, Arg.Any<CancellationToken>())
            .Returns(BuildSecurity(
                securityId,
                JsonSerializer.SerializeToElement(new
                {
                    issueDate = asOf,
                    maturityDate = asOf.AddYears(1),
                    originalFace = 120m,
                    couponRate = 0.12m,
                    paymentFrequency = "Quarterly",
                    dayCountConvention = "ACT/360",
                    factorSchedule = new[]
                    {
                        new { asOfDate = new DateOnly(2020, 1, 1), factor = 0.5m }
                    }
                })));
        var service = BuildService(store, query);

        var projection = await service.GetProjectionAsync(securityId, StructuredCashFlowScenario.Base);

        projection.Should().NotBeNull();
        projection!.FactorSchedule.Should().NotBeNull();
        projection.FactorSchedule!.Should().ContainSingle(entry => entry.Factor == 0.5m);
        // Outstanding seeded from the 0.5 factor: 120 * 0.5 = 60 amortized across the sinker schedule.
        projection.Schedule.Sum(static row => row.PrincipalAmount).Should().Be(60m);
    }

    [Fact]
    public async Task BuildLedgerPostingsAsync_FreshBaseProjection_ShouldProduceBalancedCouponAccruals()
    {
        var securityId = Guid.Parse("55555555-eeee-eeee-eeee-555555555555");
        var asOf = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var store = Substitute.For<ISecurityMasterCashFlowStore>();
        store.GetSourceAsync(securityId, Arg.Any<CancellationToken>())
            .Returns(new SecurityCashFlowSourceDto(
                securityId,
                StructuredCashFlowSourceKind.CalculatedBullet,
                DateTimeOffset.UtcNow,
                false,
                null,
                null));
        var query = Substitute.For<SecurityMasterQueryContract>();
        query.GetByIdAsync(securityId, Arg.Any<CancellationToken>())
            .Returns(BuildSecurity(
                securityId,
                JsonSerializer.SerializeToElement(new
                {
                    issueDate = asOf,
                    maturityDate = asOf.AddYears(1),
                    par = 100m,
                    couponRate = 6m,
                    paymentFrequency = "SemiAnnual",
                    dayCountConvention = "30/360"
                })));
        var service = BuildService(store, query);

        var result = await service.BuildLedgerPostingsAsync(securityId);

        result.IsPostable.Should().BeTrue();
        result.BlockedReason.Should().BeNull();
        result.Postings.Should().HaveCount(2);
        result.Postings.Should().OnlyContain(posting =>
            posting.IsBalanced && posting.TotalDebits == 3m && posting.TotalCredits == 3m);
        result.Postings[0].Lines.Should().Contain(line =>
            line.Account == "Accrued Interest Receivable" && line.Debit == 3m && line.Credit == 0m);
        result.Postings[0].Lines.Should().Contain(line =>
            line.Account == "Coupon Income" && line.Credit == 3m && line.Debit == 0m);
    }

    [Fact]
    public async Task BuildLedgerPostingsAsync_StaleSource_ShouldBlockPostingAsAGate()
    {
        var securityId = Guid.Parse("66666666-ffff-ffff-ffff-666666666666");
        var asOf = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var store = Substitute.For<ISecurityMasterCashFlowStore>();
        store.GetSourceAsync(securityId, Arg.Any<CancellationToken>())
            .Returns(new SecurityCashFlowSourceDto(
                securityId,
                StructuredCashFlowSourceKind.CalculatedBullet,
                DateTimeOffset.UtcNow.AddDays(-30),
                false,
                null,
                null));
        var query = Substitute.For<SecurityMasterQueryContract>();
        query.GetByIdAsync(securityId, Arg.Any<CancellationToken>())
            .Returns(BuildSecurity(
                securityId,
                JsonSerializer.SerializeToElement(new
                {
                    issueDate = asOf,
                    maturityDate = asOf.AddYears(1),
                    par = 100m,
                    couponRate = 6m,
                    paymentFrequency = "SemiAnnual",
                    dayCountConvention = "30/360"
                })));
        var service = BuildService(store, query);

        var result = await service.BuildLedgerPostingsAsync(securityId);

        result.IsPostable.Should().BeFalse();
        result.BlockedReason.Should().Contain("stale");
        result.Postings.Should().BeEmpty();
    }

    [Fact]
    public async Task GetProjectionAsync_SwapLegsWithDirections_ShouldNetReceiveMinusPay()
    {
        var securityId = Guid.Parse("66666666-ffff-ffff-ffff-666666666666");
        var asOf = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var service = BuildService(
            StoreWith(securityId, StructuredCashFlowSourceKind.CalculatedBullet),
            QueryWith(securityId, JsonSerializer.SerializeToElement(new
            {
                issueDate = asOf,
                maturityDate = asOf.AddYears(1),
                paymentFrequency = "SemiAnnual",
                dayCountConvention = "30/360",
                legs = new object[]
                {
                    new { legType = "Fixed", direction = "Receive", fixedRate = 0.04m, notional = 1000m },
                    new { legType = "Float", direction = "Pay", index = "SOFR", currentIndexRate = 0.03m, spreadBps = 25m, notional = 1000m }
                }
            })));

        var projection = await service.GetProjectionAsync(securityId, StructuredCashFlowScenario.Base);

        projection.Should().NotBeNull();
        projection!.TermsUsed!.HasLegs.Should().BeTrue();

        // Receive fixed: 1000 x 4% x 0.5 = 20 per period; pay float at last fixing + spread:
        // 1000 x 3.25% x 0.5 = 16.25 -> net 3.75, no principal (no exchange declared).
        projection.Schedule.Should().HaveCount(2);
        projection.Schedule.Should().OnlyContain(static row => row.InterestAmount == 3.75m && row.PrincipalAmount == 0m);

        projection.LegSchedules.Should().HaveCount(2);
        projection.LegSchedules![0].Schedule.Should().OnlyContain(static row => row.InterestAmount == 20m);
        projection.LegSchedules[1].Schedule.Should().OnlyContain(static row => row.InterestAmount == 16.25m);
    }

    [Fact]
    public async Task GetProjectionAsync_SingleFloatingLeg_ProjectsFloatingRateNoteWithPrincipalExchange()
    {
        var securityId = Guid.Parse("77777777-aaaa-bbbb-cccc-777777777777");
        var asOf = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var terms = JsonSerializer.SerializeToElement(new
        {
            issueDate = asOf,
            maturityDate = asOf.AddYears(1),
            paymentFrequency = "Quarterly",
            dayCountConvention = "30/360",
            legs = new object[]
            {
                new { legType = "Float", index = "SOFR", currentIndexRate = 0.05m, spreadBps = 100m, notional = 500m, exchangesPrincipal = true }
            }
        });
        var service = BuildService(
            StoreWith(securityId, StructuredCashFlowSourceKind.CalculatedBullet),
            QueryWith(securityId, terms));

        var projection = await service.GetProjectionAsync(securityId, StructuredCashFlowScenario.Base);

        // A single directionless leg projects as Receive: 500 x (5% + 100bps) x 0.25 = 7.5 per
        // quarter, principal returned at maturity.
        projection.Should().NotBeNull();
        projection!.Schedule.Should().HaveCount(4);
        projection.Schedule.Should().OnlyContain(static row => row.InterestAmount == 7.5m);
        projection.Schedule.Last().PrincipalAmount.Should().Be(500m);
        projection.Schedule.Last().Factor.Should().Be(0m);
        projection.Schedule.Take(3).Should().OnlyContain(static row => row.PrincipalAmount == 0m);
    }

    [Fact]
    public async Task GetProjectionAsync_FloatingLeg_AppliesScenarioShiftToFloatingRatesOnly()
    {
        var securityId = Guid.Parse("88888888-aaaa-bbbb-cccc-888888888888");
        var asOf = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var service = BuildService(
            StoreWith(securityId, StructuredCashFlowSourceKind.CalculatedBullet),
            QueryWith(securityId, JsonSerializer.SerializeToElement(new
            {
                issueDate = asOf,
                maturityDate = asOf.AddYears(1),
                paymentFrequency = "SemiAnnual",
                dayCountConvention = "30/360",
                legs = new object[]
                {
                    new { legType = "Fixed", direction = "Receive", fixedRate = 0.04m, notional = 1000m },
                    new { legType = "Float", direction = "Pay", index = "SOFR", currentIndexRate = 0.03m, spreadBps = 25m, notional = 1000m }
                }
            })));

        var projection = await service.GetProjectionAsync(securityId, StructuredCashFlowScenario.Up200);

        // +200bps moves only the floating leg: pay side becomes 5.25% -> 26.25 per period, while
        // the contractual fixed 4% is unchanged -> net 20 - 26.25 = -6.25 (the position pays net).
        projection.Should().NotBeNull();
        projection!.Schedule.Should().OnlyContain(static row => row.InterestAmount == -6.25m);
    }

    [Fact]
    public async Task GetProjectionAsync_PersistedSwapLegsWithoutDirections_ProducesLegSchedulesButNoNet()
    {
        var securityId = Guid.Parse("99999999-aaaa-bbbb-cccc-999999999999");
        var asOf = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var service = BuildService(
            StoreWith(securityId, StructuredCashFlowSourceKind.CalculatedBullet),
            QueryWith(securityId, JsonSerializer.SerializeToElement(new
            {
                issueDate = asOf,
                maturityDate = asOf.AddYears(1),
                paymentFrequency = "SemiAnnual",
                dayCountConvention = "30/360",
                notional = 1000m,
                legs = new object[]
                {
                    new { legType = "Fixed", currency = "USD", fixedRate = 0.041m },
                    new { legType = "Float", currency = "USD", index = "SOFR" }
                }
            })));

        var projection = await service.GetProjectionAsync(securityId, StructuredCashFlowScenario.Base);

        // Directions are unknown for a persisted F#-shape swap, so netting would be a guess: the
        // flat schedule stays empty (nothing can post) while per-leg detail remains available.
        projection.Should().NotBeNull();
        projection!.Schedule.Should().BeEmpty();
        projection.LegSchedules.Should().HaveCount(2);
        projection.LegSchedules![0].Schedule.Should().NotBeEmpty();
    }

    private static ISecurityMasterCashFlowStore StoreWith(Guid securityId, StructuredCashFlowSourceKind sourceKind)
    {
        var store = Substitute.For<ISecurityMasterCashFlowStore>();
        store.GetSourceAsync(securityId, Arg.Any<CancellationToken>())
            .Returns(new SecurityCashFlowSourceDto(securityId, sourceKind, DateTimeOffset.UtcNow, false, null, null));
        return store;
    }

    private static SecurityMasterQueryContract QueryWith(Guid securityId, JsonElement assetSpecificTerms)
    {
        var query = Substitute.For<SecurityMasterQueryContract>();
        query.GetByIdAsync(securityId, Arg.Any<CancellationToken>())
            .Returns(BuildSecurity(securityId, assetSpecificTerms));
        return query;
    }

    private static SecurityMasterCashFlowService BuildService(
        ISecurityMasterCashFlowStore store,
        SecurityMasterQueryContract query)
        => new(
            store,
            Array.Empty<IStructuredCashFlowProvider>(),
            query,
            NullLogger<SecurityMasterCashFlowService>.Instance);

    private static SecurityDetailDto BuildSecurity(Guid securityId, JsonElement assetSpecificTerms)
        => new(
            securityId,
            "Bond",
            SecurityStatusDto.Active,
            "Calculated bond",
            "USD",
            JsonSerializer.SerializeToElement(new { displayName = "Calculated bond", currency = "USD" }),
            assetSpecificTerms,
            [new SecurityIdentifierDto(SecurityIdentifierKind.Cusip, "123456789", true, DateTimeOffset.UtcNow)],
            [],
            1,
            DateTimeOffset.UtcNow,
            null);
}

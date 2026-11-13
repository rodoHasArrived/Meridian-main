using System.Globalization;
using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Calendars;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.SecurityMaster;
using Meridian.Instruments.FixedIncome;
using Meridian.Storage.FixedIncome;
using NSubstitute;

namespace Meridian.Tests.FixedIncome;

public sealed class AccountBondIncomePlanningEntitlementTests
{
    [Theory]
    [InlineData(false, 5)]
    [InlineData(true, 5)]
    [InlineData(false, -5)]
    [InlineData(true, -5)]
    public async Task ExCouponRefreshBlocksUnprovenHolderEntitlementAndRetainsSignedPartialCollection(bool isShort, int coupon)
    {
        using var fixture = new Fixture(isShort, coupon);
        var earlier = await fixture.RecordBeforeWindowAsync();
        var prior = earlier.Inputs.Obligations.Single(row => row.FlowType == "Coupon" && row.DueDate == Fixture.PaymentDate);
        fixture.Clock.Now = Fixture.InWindow;

        var refreshed = await fixture.PlanAsync();

        refreshed.Calculations.Single().HasDetachedCoupon.Should().BeTrue();
        refreshed.Calculations.Single().CashFlows.Where(row => row.PaymentDate == Fixture.PaymentDate).Sum(row => row.Interest).Should().Be(0m);
        refreshed.Blockers.Should().Contain(message => message.Contains("retained holder coupon entitlement", StringComparison.Ordinal));
        refreshed.BaseCurrencyLadder.IsDecisionReady.Should().BeFalse();
        refreshed.CurrencyLadders.Should().OnlyContain(row => !row.IsDecisionReady);
        refreshed.MonitoringInputs.Obligations.Single(row => row.ObligationId == prior.ObligationId)
            .Should().BeEquivalentTo(prior with { IsCurrentHolding = false });
        refreshed.MonitoringInputs.Allocations.Should().BeEquivalentTo(earlier.Inputs.Allocations);
        refreshed.MonitoringInputs.Payments.Should().BeEquivalentTo(earlier.Inputs.Payments);
        var collection = BondMonitoringEngine.Build(refreshed.MonitoringInputs).Collections.Single(row => row.ObligationId == prior.ObligationId);
        collection.ExpectedAmount.Should().Be((isShort ? -1m : 1m) * coupon);
        collection.RemainingAmount.Should().Be(prior.Amount * 0.6m);
        var capture = () => fixture.Monitor.CaptureAsync(refreshed.MonitoringInputs);
        await capture.Should().ThrowAsync<InvalidOperationException>().WithMessage("*holder coupon entitlement*");
        (await fixture.Monitor.GetHistoryAsync(Fixture.Scope)).Should().ContainSingle().Which.SnapshotId.Should().Be(earlier.SnapshotId);
        (await fixture.Monitor.CaptureAsync(earlier.Inputs)).SnapshotId.Should().Be(earlier.SnapshotId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FirstPlanInsideExCouponWindowRequiresHolderEvidenceEvenWithAcceptedFace(bool isShort)
    {
        using var fixture = new Fixture(isShort, 5);
        fixture.Clock.Now = Fixture.InWindow;
        var plan = await fixture.PlanAsync();
        plan.HoldingsBatchId.Should().NotBeNull();
        plan.Blockers.Should().Contain(message => message.Contains("holding face alone does not prove continuous ownership", StringComparison.Ordinal));
        plan.BaseCurrencyLadder.IsDecisionReady.Should().BeFalse();
        plan.MonitoringInputs.Obligations.Should().Contain(row => row.FlowType == "Principal");
        var capture = () => fixture.Monitor.CaptureAsync(plan.MonitoringInputs);
        await capture.Should().ThrowAsync<InvalidOperationException>().WithMessage("*holder coupon entitlement*");
        (await fixture.Monitor.GetHistoryAsync(Fixture.Scope)).Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingUncollectedFutureCouponBlocksLegacyMarkerAbsenceWithoutGuessingContinuousOwnership(bool isShort)
    {
        using var fixture = new Fixture(isShort, 5, legacy: true);
        var earlier = await fixture.RecordBeforeWindowAsync();
        fixture.Clock.Now = Fixture.InWindow;
        var plan = await fixture.PlanAsync();
        plan.Calculations.Single().HasDetachedCoupon.Should().BeNull();
        plan.Blockers.Should().Contain(message => message.Contains("previously retained uncollected coupon", StringComparison.Ordinal));
        plan.BaseCurrencyLadder.IsDecisionReady.Should().BeFalse();
        var capture = () => fixture.Monitor.CaptureAsync(plan.MonitoringInputs);
        await capture.Should().ThrowAsync<InvalidOperationException>().WithMessage("*holder coupon entitlement*");
        (await fixture.Monitor.GetHistoryAsync(Fixture.Scope)).Should().ContainSingle().Which.SnapshotId.Should().Be(earlier.SnapshotId);
    }

    [Fact]
    public async Task MissingCouponComparisonDoesNotBlockAnAlreadyFullyCollectedSignedObligation()
    {
        using var fixture = new Fixture(false, -5, legacy: true);
        var earlier = await fixture.RecordBeforeWindowAsync(paidFraction: 1m);
        fixture.Clock.Now = new(2027, 1, 4, 12, 0, 0, TimeSpan.Zero);
        var plan = await fixture.PlanAsync();
        plan.Blockers.Should().BeEmpty();
        plan.BaseCurrencyLadder.IsDecisionReady.Should().BeTrue();
        var paid = earlier.Inputs.Obligations.Single(row => row.FlowType == "Coupon" && row.DueDate == Fixture.PaymentDate);
        BondMonitoringEngine.Build(plan.MonitoringInputs).Collections.Single(row => row.ObligationId == paid.ObligationId)
            .RemainingAmount.Should().Be(0m);
        await fixture.Monitor.CaptureAsync(plan.MonitoringInputs);
        (await fixture.Monitor.GetHistoryAsync(Fixture.Scope)).Should().HaveCount(2);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AnOlderInactiveCouponRetainsItsUnpaidClaimIncludingAnEvidencedRevisedReceiptDate(bool revisedReceipt)
    {
        using var fixture = new Fixture(false, -5, legacy: true);
        var earlier = await fixture.RecordBeforeWindowAsync();
        var coupon = earlier.Inputs.Obligations.Single(row => row.FlowType == "Coupon" && row.DueDate == Fixture.PaymentDate);
        // Reproduce a receipt captured by the earlier producer that marked detached rights inactive.
        var inputs = earlier.Inputs with
        {
            RunId = "earlier-inactive-refresh",
            RecordedAtUtc = Fixture.InWindow,
            Obligations = earlier.Inputs.Obligations.Select(row => row == coupon ? row with { IsCurrentHolding = false } : row).ToArray(),
            ReceiptRevisions = revisedReceipt ? [new(coupon.ObligationId, new(2027, 1, 8), Fixture.Evidence("receipt-revision"))] : [],
            PreviousSnapshotId = earlier.SnapshotId,
            CorrectionRationale = "Retained earlier ex-coupon refresh."
        };
        var retained = await fixture.Store.AppendAsync(BondMonitoringEngine.Build(inputs));
        fixture.Clock.Now = revisedReceipt ? new(2027, 1, 4, 12, 0, 0, TimeSpan.Zero) : Fixture.InWindow.AddDays(1);
        var plan = await fixture.PlanAsync();
        plan.Blockers.Should().Contain(message => message.Contains("previously retained uncollected coupon", StringComparison.Ordinal));
        plan.BaseCurrencyLadder.IsDecisionReady.Should().BeFalse();
        plan.MonitoringInputs.Allocations.Should().BeEquivalentTo(earlier.Inputs.Allocations);
        var capture = () => fixture.Monitor.CaptureAsync(plan.MonitoringInputs);
        await capture.Should().ThrowAsync<InvalidOperationException>().WithMessage("*holder coupon entitlement*");
        (await fixture.Monitor.GetHistoryAsync(Fixture.Scope)).Last().SnapshotId.Should().Be(retained.SnapshotId);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public async Task ContractualPaymentDateDoesNotProveThatTheRetainedSignedCouponWasSettled(int daysAfterPayment)
    {
        using var fixture = new Fixture(false, -5, legacy: true);
        await fixture.RecordBeforeWindowAsync();
        fixture.Clock.Now = new DateTimeOffset(2027, 1, 1, 12, 0, 0, TimeSpan.Zero).AddDays(daysAfterPayment);
        var plan = await fixture.PlanAsync();
        plan.BaseCurrencyLadder.IsDecisionReady.Should().BeFalse();
        plan.Blockers.Should().Contain(message => message.Contains("previously retained uncollected coupon", StringComparison.Ordinal));
        var capture = () => fixture.Monitor.CaptureAsync(plan.MonitoringInputs);
        await capture.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task AFirstLegacyZeroedCouponIntervalRequiresExplicitCurrentRefresh()
    {
        using var fixture = new Fixture(false, 5, legacy: true);
        fixture.Clock.Now = Fixture.InWindow;
        var plan = await fixture.PlanAsync();
        plan.Calculations.Single().HasDetachedCoupon.Should().BeNull();
        plan.Blockers.Should().Contain(message => message.Contains(BondWorkflowModelVersions.Current, StringComparison.Ordinal));
        plan.BaseCurrencyLadder.IsDecisionReady.Should().BeFalse();
        var capture = () => fixture.Monitor.CaptureAsync(plan.MonitoringInputs);
        await capture.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Explicitly refresh*");
    }

    [Fact]
    public async Task CurrentLegitimateZeroCouponPeriodsDoNotAcquireALegacyAmbiguityBlocker()
    {
        using var fixture = new Fixture(false, 0);
        fixture.Clock.Now = Fixture.InWindow;
        var plan = await fixture.PlanAsync();
        plan.Calculations.Single().HasDetachedCoupon.Should().BeFalse();
        plan.Blockers.Should().BeEmpty();
        plan.BaseCurrencyLadder.IsDecisionReady.Should().BeTrue();
        (await fixture.Monitor.CaptureAsync(plan.MonitoringInputs)).Issues.Should().BeEmpty();
    }

    [Fact]
    public async Task OffsettingPhysicalPositionsDoNotProveThatGrossCouponRightsCanBeSetOff()
    {
        using var fixture = new Fixture(false, 5, offsetting: true);
        fixture.Clock.Now = Fixture.InWindow;
        var plan = await fixture.PlanAsync();
        plan.Blockers.Should().Contain(message => message.Contains("right to set off coupon cash", StringComparison.Ordinal));
        plan.BaseCurrencyLadder.IsDecisionReady.Should().BeFalse();
        var capture = () => fixture.Monitor.CaptureAsync(plan.MonitoringInputs);
        await capture.Should().ThrowAsync<InvalidOperationException>().WithMessage("*right to set off coupon cash*");
    }

    [Fact]
    public async Task AnObservedLatePaymentCanResolveTheRetainedClaimWithoutInventingHolderEntitlement()
    {
        using var fixture = new Fixture(false, -5);
        var earlier = await fixture.RecordBeforeWindowAsync();
        fixture.Clock.Now = new(2027, 1, 4, 12, 0, 0, TimeSpan.Zero);
        var plan = await fixture.PlanAsync();
        plan.BaseCurrencyLadder.IsDecisionReady.Should().BeFalse();
        var claim = earlier.Inputs.Obligations.Single(row => row.FlowType == "Coupon" && row.DueDate == Fixture.PaymentDate);
        var remaining = claim.Amount - earlier.Inputs.Allocations.Where(row => row.ExpectedFlowId == claim.ObligationId).Sum(row => row.GrossAmount);
        var day = DateOnly.FromDateTime(fixture.Clock.Now.UtcDateTime);
        var payment = new AccountCashFlowDto("late-coupon", "late-coupon-v1", Fixture.Scope.AccountId, Fixture.Scope.BookId,
            AccountCashFlowStatus.Actual, "Coupon", remaining, "USD", day, day, day,
            Fixture.Evidence("late-coupon", day, fixture.Clock.Now), claim.SecurityId);
        var captured = await fixture.Monitor.CaptureAsync(plan.MonitoringInputs with
        {
            Payments = [.. plan.MonitoringInputs.Payments, payment],
            Allocations = [.. plan.MonitoringInputs.Allocations, new("late-allocation", claim.ObligationId, payment.VersionId, remaining, remaining)]
        });
        captured.Collections.Single(row => row.ObligationId == claim.ObligationId).RemainingAmount.Should().Be(0m);
        var resolved = await fixture.PlanAsync();
        resolved.Blockers.Should().BeEmpty();
        resolved.BaseCurrencyLadder.IsDecisionReady.Should().BeTrue();
        resolved.MonitoringInputs.Payments.Should().HaveCount(2);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ADisappearingOrZeroFaceSnapshotDoesNotExtinguishAnUnpaidSignedCoupon(bool zeroFace)
    {
        using var fixture = new Fixture(false, -5);
        await fixture.RecordBeforeWindowAsync();
        fixture.ClearPositions(zeroFace);
        fixture.Clock.Now = new(2027, 1, 4, 12, 0, 0, TimeSpan.Zero);
        var plan = await fixture.PlanAsync();
        plan.BaseCurrencyLadder.IsDecisionReady.Should().BeFalse();
        plan.Blockers.Should().Contain(message => message.Contains("previously retained uncollected coupon", StringComparison.Ordinal));
        var capture = () => fixture.Monitor.CaptureAsync(plan.MonitoringInputs);
        await capture.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task ThaiHistoricalIdentitiesAndAllocationsRemainOneClaimAfterAnEnglishRefresh()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("th-TH");
            using var fixture = new Fixture(false, 5);
            var earlier = await fixture.RetainThaiBeforeWindowAsync();
            var oldCoupon = earlier.Inputs.Obligations.Single(row => row.FlowType == "Coupon" && row.DueDate == Fixture.PaymentDate);
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            fixture.Clock.Now = fixture.Clock.Now.AddDays(1);
            var plan = await fixture.PlanAsync();
            plan.Blockers.Should().BeEmpty();
            plan.BaseCurrencyLadder.IsDecisionReady.Should().BeTrue();
            plan.MonitoringInputs.Obligations.Should().HaveCount(earlier.Inputs.Obligations.Count);
            plan.MonitoringInputs.Obligations.Should().OnlyContain(row => row.IsCurrentHolding);
            plan.MonitoringInputs.Obligations.Single(row => row.DueDate == Fixture.PaymentDate && row.FlowType == "Coupon")
                .ObligationId.Should().Be(oldCoupon.ObligationId);
            plan.MonitoringInputs.Allocations.Should().BeEquivalentTo(earlier.Inputs.Allocations);
            var captured = await fixture.Monitor.CaptureAsync(plan.MonitoringInputs);
            captured.Collections.Single(row => row.ObligationId == oldCoupon.ObligationId).RemainingAmount.Should().Be(3m);

            foreach (var forgedId in new[] { new string('c', 64), fixture.WrongPeriodThaiCouponId() })
            {
                var forged = plan.MonitoringInputs with
                {
                    RunId = "forged-" + forgedId,
                    Obligations = plan.MonitoringInputs.Obligations.Select(row => row.ObligationId == oldCoupon.ObligationId
                        ? row with { ObligationId = forgedId } : row).ToArray(),
                    Allocations = plan.MonitoringInputs.Allocations.Select(row => row.ExpectedFlowId == oldCoupon.ObligationId
                        ? row with { ExpectedFlowId = forgedId } : row).ToArray()
                };
                var capture = () => fixture.Monitor.CaptureAsync(forged);
                await capture.Should().ThrowAsync<InvalidOperationException>();
            }
            (await fixture.Monitor.GetHistoryAsync(Fixture.Scope)).Should().HaveCount(2);
        }
        finally { CultureInfo.CurrentCulture = original; }
    }

    [Fact]
    public void NewObligationIdentitiesDoNotDependOnTheProcessCalendar()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            var english = AccountBondIncomePlanningService.ObligationIdentity(Fixture.Scope, Guid.Empty, "Coupon", new(2027, 1, 1), new(2026, 1, 1));
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("th-TH");
            AccountBondIncomePlanningService.ObligationIdentity(Fixture.Scope, Guid.Empty, "Coupon", new(2027, 1, 1), new(2026, 1, 1))
                .Should().Be(english);
        }
        finally { CultureInfo.CurrentCulture = original; }
    }

    [Fact]
    public async Task ADurableLocalizedHashForAnotherAccrualPeriodCannotAliasTheCurrentCoupon()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("th-TH");
            using var fixture = new Fixture(false, 5);
            var earlier = await fixture.RetainThaiBeforeWindowAsync(wrongPeriod: true);
            var differentPeriod = earlier.Inputs.Obligations.Single(row => row.FlowType == "Coupon" && row.DueDate == Fixture.PaymentDate);
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            var plan = await fixture.PlanAsync();
            plan.BaseCurrencyLadder.IsDecisionReady.Should().BeFalse();
            plan.Blockers.Should().Contain(message => message.Contains("previously retained uncollected coupon", StringComparison.Ordinal));
            var current = plan.MonitoringInputs.Obligations.Single(row => row.FlowType == "Coupon" && row.DueDate == Fixture.PaymentDate && row.IsCurrentHolding);
            current.ObligationId.Should().NotBe(differentPeriod.ObligationId);
            plan.MonitoringInputs.Obligations.Single(row => row.ObligationId == differentPeriod.ObligationId).IsCurrentHolding.Should().BeFalse();
            var capture = () => fixture.Monitor.CaptureAsync(plan.MonitoringInputs);
            await capture.Should().ThrowAsync<InvalidOperationException>();
        }
        finally { CultureInfo.CurrentCulture = original; }
    }

    private sealed class Fixture : IDisposable
    {
        private static readonly DateOnly Start = new(2026, 12, 1);
        private static readonly DateTimeOffset Before = new(2026, 12, 1, 12, 0, 0, TimeSpan.Zero);
        public static readonly DateTimeOffset InWindow = new(2026, 12, 28, 12, 0, 0, TimeSpan.Zero);
        public static readonly DateOnly PaymentDate = new(2027, 1, 1);
        private static readonly Guid Account = Guid.Parse("fa82adbb-99ce-4b0b-b1b5-6dbd1c9b7be4");
        private static readonly Guid Security = Guid.Parse("e372e9a6-119d-43bb-a449-c4030791d96f");
        private static readonly Guid Batch = Guid.Parse("08a13b17-7bc4-4f0c-9a98-9e1ed60cfd1c");
        public static readonly AccountCashForecastScopeDto Scope = new("synthetic-tenant", "synthetic-company", Account.ToString("D"), "synthetic-book", "USD");
        private readonly string _root = Path.Combine(Path.GetTempPath(), "bond-holder-entitlement-" + Guid.NewGuid().ToString("N"));
        private readonly AccountBondIncomePlanningService _planner;
        private AccountBondHoldingsSnapshotDto _holdings;
        public MutableClock Clock { get; } = new() { Now = Before };
        public BondMonitoringService Monitor { get; }
        public BondMonitoringFileStore Store { get; }

        public Fixture(bool isShort, decimal coupon, bool legacy = false, bool offsetting = false)
        {
            var bond = new SecurityDetailDto(Security, "Bond", SecurityStatusDto.Active, "Synthetic ex-coupon bond", "USD",
                JsonSerializer.SerializeToElement(new { }), JsonSerializer.SerializeToElement(new
                {
                    assetClassTermsVersion = 5,
                    issueDate = "2026-01-01",
                    maturity = "2028-01-01",
                    couponType = "Fixed",
                    couponRate = coupon,
                    dayCount = "30/360",
                    par = 100m,
                    paymentFrequency = "Annual",
                    isCallable = false,
                    bondWorkflow = new { exCouponBusinessDays = 10 },
                    scheduleConventions = new
                    {
                        accrualAdjusted = false,
                        couponRateUnit = "Percent",
                        couponCashBasis = "Level",
                        paymentCalendars = new[] { "WEEKENDS" },
                        businessDayConvention = "Following"
                    }
                }), [], [], 1, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), null);
            var query = Substitute.For<ISecurityMasterQueryService>();
            query.GetRecordedByIdAsOfAsync(Security, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(bond);
            query.GetByIdAsync(Security, Arg.Any<CancellationToken>()).Returns(bond);
            var source = Substitute.For<IAccountBondHoldingsSource>();
            var position = new CustodianPositionLineDto(Guid.Parse("2ee7d387-f6be-4379-8d34-bb0d6f0a314d"), Batch, Account, Start, "bond", "ISIN",
                        isShort ? -100m : 100m, isShort ? -100m : 100m, "USD", "Synthetic ex-coupon bond", "Bond", isShort,
                        SecurityId: Security, Bond: new(100m, 100m, 1m, 1m, BondHoldingQuoteBasis.PercentOfCurrentFace, 100m, false, 0m,
                            InvoiceBasis: BondHoldingInvoiceBasis.Nominal));
            _holdings = new AccountBondHoldingsSnapshotDto(Account, Start, Batch, Sha256Digest.ComputeUtf8("unchanged accepted face"), "synthetic",
                    offsetting ? [position, position with
                    {
                        LineId = Guid.Parse("5a6ea006-b2b3-48d0-8e81-48fd558cdb87"), IsShort = !isShort,
                        Quantity = -position.Quantity, MarketValue = -position.MarketValue
                    }] : [position]);
            source.GetHoldingsAsync(Account, Arg.Any<DateOnly>(), Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>()).Returns(_ => _holdings);
            IBondWorkflowService workflow = new BondWorkflowService(query, new StaticBusinessDayCalendarSource([BusinessDayCalendars.WeekendsOnly]));
            if (legacy)
                workflow = new LegacyWorkflow(workflow);
            Store = new(_root);
            Monitor = new(Store, source, workflow);
            _planner = new(source, workflow, Monitor, Clock);
        }

        public Task<AccountBondIncomePlanDto> PlanAsync() => _planner.PlanAsync(new(Scope, Start, new(2027, 2, 1),
            new(Clock.Now, DateOnly.FromDateTime(Clock.Now.UtcDateTime), Clock.Now),
            [new(Scope.AccountId, Scope.BookId, Start, 1000m, "USD", true, Evidence("opening"))], []));

        public async Task<BondMonitoringSnapshotDto> RecordBeforeWindowAsync(decimal paidFraction = 0.4m)
        {
            var plan = await PlanAsync();
            plan.Blockers.Should().BeEmpty();
            var coupon = plan.MonitoringInputs.Obligations.Single(row => row.FlowType == "Coupon" && row.DueDate == PaymentDate);
            var amount = coupon.Amount * paidFraction;
            var inputs = plan.MonitoringInputs with
            {
                Payments = [new("observed-coupon", "observed-coupon-v1", Scope.AccountId, Scope.BookId, AccountCashFlowStatus.Actual,
                    "Coupon", amount, "USD", Start, Start, Start, Evidence("observed-coupon"), Security)],
                Allocations = [new("observed-allocation", coupon.ObligationId, "observed-coupon-v1", amount, amount)]
            };
            return await Monitor.CaptureAsync(inputs);
        }

        public async Task<BondMonitoringSnapshotDto> RetainThaiBeforeWindowAsync(bool wrongPeriod = false)
        {
            // Freeze successful Gregorian financial facts before reproducing the earlier Thai
            // identity/receipt renderer. The legacy schedule anchor reader itself follows the
            // process calendar, so its separate calendar-compatibility policy is not changed here.
            var culture = CultureInfo.CurrentCulture;
            AccountBondIncomePlanDto plan;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
                plan = await PlanAsync();
            }
            finally { CultureInfo.CurrentCulture = culture; }
            plan.Blockers.Should().BeEmpty();
            plan.Calculations.Should().ContainSingle().Which.IsBlocked.Should().BeFalse();
            plan.MonitoringInputs.Obligations.Should().Contain(row => row.FlowType == "Coupon" && row.DueDate == PaymentDate);
            var obligations = plan.MonitoringInputs.Obligations.Select(row =>
            {
                var flow = plan.Calculations.Single().CashFlows.Single(flow => flow.PaymentDate == row.DueDate
                    && (row.FlowType == "Coupon" ? flow.Interest != 0m : flow.Principal != 0m));
                FormattableString source = $"{Scope.AccountId}|{Scope.BookId}|{Security:D}|{row.FlowType}|{flow.PaymentDate:yyyy-MM-dd}|{flow.AccrualStart:yyyy-MM-dd}";
                return row with
                {
                    ObligationId = wrongPeriod && row.FlowType == "Coupon" && row.DueDate == PaymentDate
                    ? WrongPeriodThaiCouponId() : Sha256Digest.ComputeUtf8(source.ToString(CultureInfo.GetCultureInfo("th-TH")))
                };
            }).ToArray();
            var coupon = obligations.Single(row => row.FlowType == "Coupon" && row.DueDate == PaymentDate);
            var payment = new AccountCashFlowDto("observed-coupon", "observed-coupon-v1", Scope.AccountId, Scope.BookId,
                AccountCashFlowStatus.Actual, "Coupon", 2m, "USD", Start, Start, Start, Evidence("observed-coupon"), Security);
            return await Store.AppendAsync(BondMonitoringEngine.Build(plan.MonitoringInputs with
            {
                Obligations = obligations,
                Payments = [payment],
                Allocations = [new("observed-allocation", coupon.ObligationId, payment.VersionId, 2m, 2m)]
            }, BondMonitoringEngine.PreviousEngineVersion));
        }

        public string WrongPeriodThaiCouponId()
        {
            FormattableString source = $"{Scope.AccountId}|{Scope.BookId}|{Security:D}|Coupon|{PaymentDate:yyyy-MM-dd}|{new DateOnly(2026, 1, 2):yyyy-MM-dd}";
            return Sha256Digest.ComputeUtf8(source.ToString(CultureInfo.GetCultureInfo("th-TH")));
        }

        public void ClearPositions(bool zeroFace) => _holdings = _holdings with
        {
            Positions = zeroFace ? _holdings.Positions.Select(row => row with
            { Quantity = 0m, MarketValue = 0m, Bond = row.Bond! with { OriginalFace = 0m, CurrentFace = 0m } }).ToArray() : []
        };

        public static AccountCashProvenanceDto Evidence(string id, DateOnly? effective = null, DateTimeOffset? recordedAt = null)
        {
            var hash = Sha256Digest.ComputeUtf8(id);
            var at = recordedAt ?? Before;
            return new("SyntheticCustodian", id, hash, AccountCashEvidenceRole.Observation,
                new(id, "https://evidence.example.test/" + id, hash, "SyntheticCustodian", id, "Accepted",
                    "synthetic-reviewer", at.AddHours(-2), effective ?? Start, 1, at.AddHours(-1), "synthetic-retainer", "AccountCash", Scope.AccountId));
        }

        public void Dispose()
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, true);
        }
    }

    private sealed class LegacyWorkflow(IBondWorkflowService inner) : IBondWorkflowService
    {
        public Task<BondWorkflowResult?> CalculateAsync(BondWorkflowRequest request, CancellationToken ct = default) =>
            inner.CalculateAsync(request with { ModelVersion = BondWorkflowModelVersions.Legacy }, ct);
        public Task<BondWorkflowResult?> CalculateIncomeScheduleAsync(BondWorkflowRequest request, DateOnly freezeIndexAt, CancellationToken ct = default) =>
            inner.CalculateIncomeScheduleAsync(request with { ModelVersion = BondWorkflowModelVersions.Legacy }, freezeIndexAt, ct);
    }

    private sealed class MutableClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; }
        public override DateTimeOffset GetUtcNow() => Now;
    }
}

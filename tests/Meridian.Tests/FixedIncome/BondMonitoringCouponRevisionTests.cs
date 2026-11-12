using System.Globalization;
using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Calendars;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.SecurityMaster.Schedules;
using Meridian.Instruments.FixedIncome;
using Meridian.Storage.FixedIncome;

namespace Meridian.Tests.FixedIncome;

public sealed class BondMonitoringCouponRevisionTests
{
    [Theory]
    [InlineData(.01, .09, true)]
    [InlineData(.09, .10, false)]
    [InlineData(-.03, -.02, false)]
    public async Task WholeCouponRevisionComparesActualCappedFlooredModelOnceAcrossDailyResets(
        double firstRate, double revisedRate, bool changesCoupon)
    {
        var root = Path.Combine(Path.GetTempPath(), "bond-coupon-watch-" + Guid.NewGuid().ToString("N"));
        try
        {
            var holdings = new Holdings();
            var model = new OvernightModel { Rate = (decimal)firstRate };
            var monitor = new BondMonitoringService(new BondMonitoringFileStore(root), holdings, model);
            var planning = new AccountBondIncomePlanningService(holdings, model, monitor, new Clock());
            var first = await planning.PlanAsync(Request());
            first.MonitoringInputs.Resets.Should().HaveCountGreaterThan(1);
            first.MonitoringInputs.Resets.Should().OnlyContain(row => row.PreviousRate == null && row.PreviousCouponAmount == null);
            var firstWatch = await monitor.CaptureAsync(first.MonitoringInputs);
            firstWatch.Watch.Should().ContainSingle(row => row.Kind == "NextCoupon" && row.Summary.Contains("previous estimate unavailable"));
            firstWatch.Watch.Should().NotContain(row => row.Kind == "CouponRevision");
            var priorCoupon = first.MonitoringInputs.Obligations.Where(row => row.FlowType == "Coupon").Sum(row => row.Amount);

            model.Rate = (decimal)revisedRate;
            model.ObserveElapsedFixings = true;
            var next = await planning.PlanAsync(Request());
            var currentCoupon = next.MonitoringInputs.Obligations.Where(row => row.FlowType == "Coupon").Sum(row => row.Amount);
            next.MonitoringInputs.Resets.Should().OnlyContain(row => row.CouponPaymentDate == Today.AddDays(7)
                && row.CouponAmount == currentCoupon && row.PreviousCouponAmount == priorCoupon && row.PreviousRate != null);
            next.MonitoringInputs.Resets.Should().Contain(row => row.IsObserved);
            next.MonitoringInputs.Resets.Should().Contain(row => !row.IsObserved);
            var watch = BondMonitoringEngine.Build(next.MonitoringInputs);
            watch.Issues.Should().BeEmpty();
            if (changesCoupon)
            {
                currentCoupon.Should().NotBe(priorCoupon);
                watch.Watch.Should().ContainSingle(row => row.Kind == "CouponRevision"
                    && row.AffectedObligationIds.Count == 1 && row.Summary.Contains($"total change {currentCoupon - priorCoupon}"));
            }
            else
            {
                currentCoupon.Should().Be(priorCoupon, "the shared model applies the contractual cap/floor before payable rounding");
                watch.Watch.Should().NotContain(row => row.Kind == "CouponRevision");
            }
            watch.Watch.Where(row => row.Kind is "ObservedReset" or "ForecastReset")
                .Should().OnlyContain(row => !row.Summary.Contains("total change"));
            var json = JsonSerializer.Serialize(new BondMonitoringResetDto(Security, Today, Today, .01m, false,
                "synthetic", []), BondMonitoringJsonContext.Default.Options);
            json.Should().NotContain("CouponAmount").And.NotContain("PreviousRate");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private static readonly DateOnly Today = new(2026, 11, 12);
    private static readonly DateTimeOffset Now = new(2026, 11, 12, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Account = Guid.Parse("ab35ef3b-80df-4fb3-8686-aaeeea16e797");
    private static readonly Guid Security = Guid.Parse("a26c423f-b393-4f20-b52d-e46ff5240ddc");
    private static readonly Guid Batch = Guid.Parse("a5c8ad92-8ff9-46da-b7f6-51fd68b382bf");
    private static readonly AccountCashForecastScopeDto Scope = new("tenant", "company", Account.ToString("D"), Guid.NewGuid().ToString("D"), "USD");
    private static AccountBondIncomePlanningRequest Request() => new(Scope, Today, Today.AddDays(30), new(Now), [], []);
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => Now; }
    private sealed class Holdings : IAccountBondHoldingsSource
    {
        public Task<AccountBondHoldingsSnapshotDto?> GetHoldingsAsync(Guid accountId, DateOnly asOf, DateTimeOffset? knownAt = null, CancellationToken ct = default) =>
            Task.FromResult<AccountBondHoldingsSnapshotDto?>(new(Account, Today, Batch, Sha256Digest.ComputeUtf8("held"), "synthetic",
                [new(Guid.NewGuid(), Batch, Account, Today, "bond", "ISIN", 2000m, 2000m, "USD", "Held overnight bond", "Bond", false,
                    SecurityId: Security, Bond: new(2000m, 2000m, 1m, 1m, BondHoldingQuoteBasis.PercentOfCurrentFace, 100m, false, 0m,
                        InvoiceBasis: BondHoldingInvoiceBasis.Nominal))]));
    }
    private sealed class OvernightModel : IBondWorkflowService
    {
        public decimal Rate { get; set; }
        public bool ObserveElapsedFixings { get; set; }
        public Task<BondWorkflowResult?> CalculateAsync(BondWorkflowRequest request, CancellationToken ct = default)
        {
            var schedule = ScheduleGenerator.Generate(ScheduleSpec.IssueAnchoredForward(Today, Today.AddDays(7), ScheduleTenor.Term));
            var terms = new BondWorkflowProjectionTerms("Floating", 0m, 0m, .05m, .02m,
                DayCountConvention.Actual360, "DayCount", request.OriginalFace, 1m, [],
                new(BusinessDayCalendars.WeekendsOnly, 0, false, true, 0));
            // Use the real signed-cash kernel, including daily compounding and contractual cap/floor.
            var flows = BondWorkflowProjection.Project(schedule, terms, Today,
                date => new(Rate, ObserveElapsedFixings && date <= Today, "SyntheticOvernightSource"));
            return Task.FromResult<BondWorkflowResult?>(new(Security, "USD", BondWorkflowProjection.ModelVersion,
                request.Context with { SecurityVersion = 1 }, request.OriginalFace, request.OriginalFace, 0m,
                null, null, 0m, null, null, null, null, null, null, null, flows,
                [new("ForwardCurveContent", "overnight", ContentSha256: Sha256Digest.ComputeUtf8(Rate.ToString(CultureInfo.InvariantCulture)))], [], []));
        }
        public Task<BondWorkflowResult?> CalculateIncomeScheduleAsync(BondWorkflowRequest request, DateOnly freezeIndexAt,
            CancellationToken ct = default) => CalculateAsync(request, ct);
    }
}

using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.SecurityMaster.Schedules;
using Meridian.Instruments.FixedIncome;
using Meridian.Storage.FixedIncome;

namespace Meridian.Tests.FixedIncome;

public sealed class AccountBondIncomePlanningTests
{
    private static readonly DateOnly Start = new(2026, 11, 12);
    private static readonly DateTimeOffset Now = new(2026, 11, 12, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Account = Guid.Parse("c3522a83-0ef6-4580-a95f-8ef86d9729a1");
    private static readonly Guid Security = Guid.Parse("b84d60d4-6857-4088-8697-dc49cd4f7ee1");
    private static readonly Guid Batch = Guid.Parse("ec539088-16ce-425d-a1c0-bc17bf86b263");
    private static readonly AccountCashForecastScopeDto Scope = new("tenant", "company", Account.ToString("D"), "book", "USD");

    [Fact]
    public async Task PlanningUsesAcceptedOriginalFaceAndPreservesSignedShortCash()
    {
        var source = new Holdings();
        var bonds = new Bonds();
        var service = new AccountBondIncomePlanningService(source, bonds, new NoHistory(), new FrozenClock());
        var result = await service.PlanAsync(Request());
        bonds.LastRequest!.OriginalFace.Should().Be(2000m);
        bonds.LastRequest.Context.KnownAt.Should().Be(Now);
        result.HoldingsBatchId.Should().Be(Batch);
        result.Blockers.Should().BeEmpty();
        result.MonitoringInputs.Obligations.Should().HaveCount(2);
        result.CurrencyLadders.Single().Contributions.Sum(row => row.Amount).Should().Be(-2010m);
        result.BaseCurrencyLadder.IsDecisionReady.Should().BeTrue();
    }

    [Fact]
    public async Task CanonicalCaptureRejectsWorkstationChangedExpectedEconomics()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-canonical-monitor", Guid.NewGuid().ToString("N"));
        try
        {
            var source = new Holdings();
            var bonds = new Bonds();
            var monitor = new BondMonitoringService(new BondMonitoringFileStore(root), source, bonds);
            var service = new AccountBondIncomePlanningService(source, bonds, monitor, new FrozenClock());
            var plan = await service.PlanAsync(Request());
            var altered = plan.MonitoringInputs with
            { Obligations = plan.MonitoringInputs.Obligations.Select(row => row with { Amount = row.Amount + 1m }).ToArray() };
            var capture = () => monitor.CaptureAsync(altered);
            await capture.Should().ThrowAsync<InvalidOperationException>();
            (await monitor.GetHistoryAsync(Scope)).Should().BeEmpty();
            await monitor.CaptureAsync(plan.MonitoringInputs);
            (await monitor.GetHistoryAsync(Scope)).Should().ContainSingle();
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FinalInstallmentAndResidualBalloonBecomeOneCanonicalPrincipalObligation(bool isShort)
    {
        var final = Start.AddYears(1);
        var schedule = ScheduleGenerator.Generate(ScheduleSpec.IssueAnchoredForward(Start, final, ScheduleTenor.Years(1)));
        var flows = BondWorkflowProjection.Project(schedule,
            new("Fixed", -0.05m, 0m, null, null, DayCountConvention.Thirty360, "Level", 100m, 1m,
                [new(final, 40m)]), Start);
        flows.Where(row => row.Principal != 0m).Select(row => row.Principal).Should().Equal(40m, 60m);
        var root = Path.Combine(Path.GetTempPath(), "meridian-principal-monitor", Guid.NewGuid().ToString("N"));
        try
        {
            var source = new Holdings { IsShort = isShort, OriginalFace = 100m };
            var bonds = new Bonds { CurrentFace = 100m, CashFlows = flows };
            var monitor = new BondMonitoringService(new BondMonitoringFileStore(root), source, bonds);
            var plan = await new AccountBondIncomePlanningService(source, bonds, monitor, new FrozenClock()).PlanAsync(Request());
            var direction = isShort ? -1m : 1m;
            plan.Blockers.Should().BeEmpty();
            plan.BaseCurrencyLadder.IsDecisionReady.Should().BeTrue();
            plan.MonitoringInputs.Obligations.Should().HaveCount(2);
            var principal = plan.MonitoringInputs.Obligations.Single(row => row.FlowType == "Principal");
            principal.ObligationId.Should().Be(AccountBondIncomePlanningService.ObligationIdentity(Scope, Security, "Principal", final, final));
            principal.Amount.Should().Be(direction * 100m);
            plan.MonitoringInputs.Obligations.Single(row => row.FlowType == "Coupon").Amount.Should().Be(direction * -5m);
            plan.CurrencyLadders.Single().Contributions.Sum(row => row.Amount).Should().Be(direction * 95m);
            var altered = plan.MonitoringInputs with
            { Obligations = plan.MonitoringInputs.Obligations.Select(row => row == principal ? row with { Amount = direction * 40m } : row).ToArray() };
            var capture = () => monitor.CaptureAsync(altered);
            await capture.Should().ThrowAsync<InvalidOperationException>();
            (await monitor.GetHistoryAsync(Scope)).Should().BeEmpty();

            var captured = await monitor.CaptureAsync(plan.MonitoringInputs);
            captured.Issues.Should().BeEmpty();
            captured.Collections.Single(row => row.FlowType == "Principal").ExpectedAmount.Should().Be(direction * 100m);
            (await monitor.GetHistoryAsync(Scope)).Should().ContainSingle();
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData("USD", "40.005", "60.005", false)]
    [InlineData("USD", "40.005", "60.005", true)]
    [InlineData("JPY", "40.5", "60.5", false)]
    [InlineData("JPY", "40.5", "60.5", true)]
    [InlineData("KWD", "40.0005", "60.0005", false)]
    [InlineData("KWD", "40.0005", "60.0005", true)]
    public async Task CanonicalPrincipalRoundsTheRawAggregateOnceInItsNativeCurrency(string currency,
        string installmentText, string balloonText, bool isShort)
    {
        var installment = decimal.Parse(installmentText, System.Globalization.CultureInfo.InvariantCulture);
        var balloon = decimal.Parse(balloonText, System.Globalization.CultureInfo.InvariantCulture);
        var face = installment + balloon;
        var final = Start.AddYears(1);
        var schedule = ScheduleGenerator.Generate(ScheduleSpec.IssueAnchoredForward(Start, final, ScheduleTenor.Years(1)));
        var flows = BondWorkflowProjection.Project(schedule,
            new("ZeroCoupon", 0m, 0m, null, null, DayCountConvention.Thirty360, "Level", face, 1m,
                [new(final, installment)]), Start).Select(row => row.Principal == installment ? row with { IsForecast = true } : row).ToArray();
        var source = new Holdings { IsShort = isShort, OriginalFace = face, Currency = currency };
        var bonds = new Bonds { CurrentFace = face, Currency = currency, CashFlows = flows };
        var root = Path.Combine(Path.GetTempPath(), "meridian-principal-native", Guid.NewGuid().ToString("N"));
        try
        {
            var monitor = new BondMonitoringService(new BondMonitoringFileStore(root), source, bonds);
            var plan = await new AccountBondIncomePlanningService(source, bonds, monitor, new FrozenClock()).PlanAsync(Request(currency));
            plan.Blockers.Should().BeEmpty();
            var principal = plan.MonitoringInputs.Obligations.Should().ContainSingle().Which;
            principal.Amount.Should().Be((isShort ? -1m : 1m) * face,
                "rounding the installment and balloon separately creates an extra native minor unit");
            principal.IsForecast.Should().BeTrue();
            principal.Inputs.Should().Contain(input => input.Kind == "HoldingsSnapshot" && input.Key == Batch.ToString("D"));
            var altered = plan.MonitoringInputs with { Obligations = [principal with { IsForecast = false }] };
            var capture = () => monitor.CaptureAsync(altered);
            await capture.Should().ThrowAsync<InvalidOperationException>();
            (await monitor.GetHistoryAsync(plan.Scope)).Should().BeEmpty();
            var captured = await monitor.CaptureAsync(plan.MonitoringInputs);
            captured.Inputs.Obligations.Should().BeEquivalentTo(plan.MonitoringInputs.Obligations);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task CouponRightsWithDifferentAccrualStartsDoNotNetDespiteSharingAPaymentDate()
    {
        var final = Start.AddDays(30);
        var bonds = new Bonds
        {
            CashFlows = [new(final, Start, final, 1.235m, 40m, 1960m, 1m, false, []),
                new(final, Start.AddDays(1), final, -1.235m, 0m, 1960m, 1m, false, []),
                new(final, Start, final, 0m, 1960m, 0m, 1m, false, [])]
        };
        var plan = await new AccountBondIncomePlanningService(new Holdings { IsShort = false }, bonds,
            new NoHistory(), new FrozenClock()).PlanAsync(Request());
        plan.Blockers.Should().BeEmpty();
        plan.MonitoringInputs.Obligations.Where(row => row.FlowType == "Coupon").Select(row => row.Amount)
            .Should().BeEquivalentTo(new[] { 1.24m, -1.24m });
        plan.MonitoringInputs.Obligations.Select(row => row.ObligationId).Should().OnlyHaveUniqueItems();
        plan.MonitoringInputs.Obligations.Single(row => row.FlowType == "Principal").Amount.Should().Be(2000m);
    }

    [Fact]
    public async Task ExactLegacyCaptureRetryRetainsSeparatelyRoundedZeroPrincipalButANewCaptureMustUseTheAggregate()
    {
        var final = Start.AddDays(30);
        var source = new Holdings { IsShort = false, OriginalFace = 0.8m, Currency = "JPY" };
        var bonds = new Bonds
        {
            CurrentFace = 0.8m,
            Currency = "JPY",
            CashFlows = [new(final, final, final, 1m, 0.4m, 0.4m, 1m, false, []),
                new(final, final, final, 0m, 0.4m, 0m, 1m, false, [])]
        };
        var root = Path.Combine(Path.GetTempPath(), "meridian-principal-retry", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new BondMonitoringFileStore(root);
            var monitor = new BondMonitoringService(store, source, bonds);
            var plan = await new AccountBondIncomePlanningService(source, bonds, monitor, new FrozenClock()).PlanAsync(Request("JPY"));
            plan.MonitoringInputs.Obligations.Single(row => row.FlowType == "Principal").Amount.Should().Be(1m);
            // The earlier producer rounded each 0.4 principal row to zero and retained only the coupon.
            var earlierInputs = plan.MonitoringInputs with
            { Obligations = plan.MonitoringInputs.Obligations.Where(row => row.FlowType == "Coupon").ToArray() };
            var earlier = BondMonitoringEngine.Build(earlierInputs, BondMonitoringEngine.LegacyEngineVersion);
            earlier.Issues.Should().BeEmpty();
            await store.AppendAsync(earlier);
            var priorCalls = bonds.CalculationCalls;

            var retry = await monitor.CaptureAsync(earlierInputs);

            JsonSerializer.Serialize(retry, BondMonitoringJsonContext.Default.BondMonitoringSnapshotDto)
                .Should().Be(JsonSerializer.Serialize(earlier, BondMonitoringJsonContext.Default.BondMonitoringSnapshotDto));
            bonds.CalculationCalls.Should().Be(priorCalls, "an exact retained retry must not reinterpret its earlier economics");
            var changedSameRun = () => monitor.CaptureAsync(earlierInputs with
            { Obligations = earlierInputs.Obligations.Select(row => row with { Amount = 2m }).ToArray() });
            await changedSameRun.Should().ThrowAsync<InvalidOperationException>();
            var newCapture = () => monitor.CaptureAsync(earlierInputs with { RunId = "new-capture-with-obsolete-principal" });
            await newCapture.Should().ThrowAsync<InvalidOperationException>();
            (await monitor.GetHistoryAsync(plan.Scope)).Should().ContainSingle().Which.SnapshotId.Should().Be(earlier.SnapshotId);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task HoldingFactorDisagreementRemainsVisibleAndBlocksLiquidityConclusion()
    {
        var service = new AccountBondIncomePlanningService(new Holdings(), new Bonds { CurrentFace = 1500m }, new NoHistory(), new FrozenClock());
        var result = await service.PlanAsync(Request());
        result.BaseCurrencyLadder.IsDecisionReady.Should().BeFalse();
        result.Blockers.Should().Contain(row => row.Contains("current face disagree", StringComparison.Ordinal));
    }

    private static AccountBondIncomePlanningRequest Request(string currency = "USD")
    {
        var hash = Sha256Digest.ComputeUtf8("opening");
        var source = new AccountCashProvenanceDto("SyntheticCustodian", "opening", hash, AccountCashEvidenceRole.Observation,
            new("opening", "https://evidence.example.test/opening", hash, "SyntheticCustodian", "opening", "Accepted",
                "reviewer", Now.AddHours(-2), Start, 1, Now.AddHours(-1), "retainer", "AccountCash", Scope.AccountId));
        return new(Scope with { BaseCurrency = currency }, Start, Start.AddDays(365), new(Now),
            [new(Scope.AccountId, Scope.BookId, Start, 3000m, currency, true, source)], []);
    }

    private sealed class FrozenClock : TimeProvider { public override DateTimeOffset GetUtcNow() => Now; }
    private sealed class Holdings : IAccountBondHoldingsSource
    {
        public bool IsShort { get; init; } = true;
        public decimal OriginalFace { get; init; } = 2000m;
        public string Currency { get; init; } = "USD";
        public Task<AccountBondHoldingsSnapshotDto?> GetHoldingsAsync(Guid accountId, DateOnly asOf, DateTimeOffset? knownAt = null, CancellationToken ct = default) =>
            Task.FromResult<AccountBondHoldingsSnapshotDto?>(new(Account, Start, Batch, Sha256Digest.ComputeUtf8("holdings"), "synthetic-verified",
                [new(Guid.Parse("e8dbb375-1d2f-4ffb-83c3-0c83cf00e745"), Batch, Account, Start, "bond", "ISIN",
                    (IsShort ? -1m : 1m) * OriginalFace, (IsShort ? -1m : 1m) * OriginalFace,
                    Currency, "Synthetic bond", "Bond", IsShort, SecurityId: Security,
                    Bond: new(OriginalFace, OriginalFace, 1m, 1m, BondHoldingQuoteBasis.PercentOfCurrentFace, 100m, false, 0m,
                        InvoiceBasis: BondHoldingInvoiceBasis.Nominal))]));
    }
    private sealed class Bonds : IBondWorkflowService
    {
        public BondWorkflowRequest? LastRequest { get; private set; }
        public int CalculationCalls { get; private set; }
        public decimal CurrentFace { get; init; } = 2000m;
        public string Currency { get; init; } = "USD";
        public IReadOnlyList<BondWorkflowCashFlow>? CashFlows { get; init; }
        public Task<BondWorkflowResult?> CalculateAsync(BondWorkflowRequest request, CancellationToken ct = default)
        {
            LastRequest = request;
            CalculationCalls++;
            return Task.FromResult<BondWorkflowResult?>(new(Security, Currency, BondWorkflowModelVersions.Current, request.Context with { SecurityVersion = 1 },
                request.OriginalFace, CurrentFace, 0m, null, null, 0m, null, null, null, null, null, null, null,
                CashFlows ?? [new(Start.AddDays(30), Start, Start.AddDays(30), 10m, 2000m, 0m, 1m, false, [])],
                [new("SecurityTerms", Security.ToString("D"), 1, Sha256Digest.ComputeUtf8("terms")),
                    new("CalculationModel", BondWorkflowModelVersions.Current)], [], []));
        }
        public Task<BondWorkflowResult?> CalculateIncomeScheduleAsync(BondWorkflowRequest request, DateOnly freezeIndexAt,
            CancellationToken ct = default) => CalculateAsync(request, ct);
    }
    private sealed class NoHistory : IBondMonitoringService
    {
        public Task<BondMonitoringSnapshotDto> CaptureAsync(BondMonitoringCaptureRequest request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<BondMonitoringSnapshotDto>> GetHistoryAsync(AccountCashForecastScopeDto scope, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<BondMonitoringSnapshotDto>>([]);
    }
}

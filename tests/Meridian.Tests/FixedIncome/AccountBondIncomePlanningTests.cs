using FluentAssertions;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Integrity;
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

    [Fact]
    public async Task HoldingFactorDisagreementRemainsVisibleAndBlocksLiquidityConclusion()
    {
        var service = new AccountBondIncomePlanningService(new Holdings(), new Bonds { CurrentFace = 1500m }, new NoHistory(), new FrozenClock());
        var result = await service.PlanAsync(Request());
        result.BaseCurrencyLadder.IsDecisionReady.Should().BeFalse();
        result.Blockers.Should().Contain(row => row.Contains("current face disagree", StringComparison.Ordinal));
    }

    private static AccountBondIncomePlanningRequest Request()
    {
        var hash = Sha256Digest.ComputeUtf8("opening");
        var source = new AccountCashProvenanceDto("SyntheticCustodian", "opening", hash, AccountCashEvidenceRole.Observation,
            new("opening", "https://evidence.example.test/opening", hash, "SyntheticCustodian", "opening", "Accepted",
                "reviewer", Now.AddHours(-2), Start, 1, Now.AddHours(-1), "retainer", "AccountCash", Scope.AccountId));
        return new(Scope, Start, Start.AddDays(365), new(Now),
            [new(Scope.AccountId, Scope.BookId, Start, 3000m, "USD", true, source)], []);
    }

    private sealed class FrozenClock : TimeProvider { public override DateTimeOffset GetUtcNow() => Now; }
    private sealed class Holdings : IAccountBondHoldingsSource
    {
        public Task<AccountBondHoldingsSnapshotDto?> GetHoldingsAsync(Guid accountId, DateOnly asOf, DateTimeOffset? knownAt = null, CancellationToken ct = default) =>
            Task.FromResult<AccountBondHoldingsSnapshotDto?>(new(Account, Start, Batch, Sha256Digest.ComputeUtf8("holdings"), "synthetic-verified",
                [new(Guid.Parse("e8dbb375-1d2f-4ffb-83c3-0c83cf00e745"), Batch, Account, Start, "bond", "ISIN", -2000m, -2000m,
                    "USD", "Synthetic bond", "Bond", true, SecurityId: Security,
                    Bond: new(2000m, 2000m, 1m, 1m, BondHoldingQuoteBasis.PercentOfCurrentFace, 100m, false, 0m,
                        InvoiceBasis: BondHoldingInvoiceBasis.Nominal))]));
    }
    private sealed class Bonds : IBondWorkflowService
    {
        public BondWorkflowRequest? LastRequest { get; private set; }
        public decimal CurrentFace { get; init; } = 2000m;
        public Task<BondWorkflowResult?> CalculateAsync(BondWorkflowRequest request, CancellationToken ct = default)
        {
            LastRequest = request;
            return Task.FromResult<BondWorkflowResult?>(new(Security, "USD", "synthetic-verified", request.Context with { SecurityVersion = 1 },
                request.OriginalFace, CurrentFace, 0m, null, null, 0m, null, null, null, null, null, null, null,
                [new(Start.AddDays(30), Start, Start.AddDays(30), 10m, 2000m, 0m, 1m, false, [])],
                [new("SecurityTerms", Security.ToString("D"), 1, Sha256Digest.ComputeUtf8("terms"))], [], []));
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

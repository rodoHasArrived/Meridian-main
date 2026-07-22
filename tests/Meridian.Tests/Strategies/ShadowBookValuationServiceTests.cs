using FluentAssertions;
using Meridian.Contracts.Operations;
using Meridian.Contracts.Workstation;
using Meridian.Strategies.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Meridian.Tests.Strategies;

public sealed class ShadowBookValuationServiceTests
{
    [Fact]
    public async Task RunAsync_IsDeterministic_ForEquivalentInputs()
    {
        var repo = new FileReconciliationBreakQueueRepository(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")), NullLogger<FileReconciliationBreakQueueRepository>.Instance);
        var service = new ShadowBookValuationService(repo);

        var request = BuildRequest(externalNav: 2_000_000m);
        var first = await service.RunAsync(request);
        var second = await service.RunAsync(request with
        {
            OperatorId = "operator-b",
            Positions = request.Positions.Reverse().ToArray(),
            PricesBySymbol = request.PricesBySymbol.Reverse().ToDictionary(static pair => pair.Key, static pair => pair.Value),
            FxRatesByCurrency = request.FxRatesByCurrency.Reverse().ToDictionary(static pair => pair.Key, static pair => pair.Value)
        });

        first.Snapshot.Should().NotBeNull();
        second.Snapshot.Should().NotBeNull();
        first.Snapshot!.VersionHash.Should().Be(second.Snapshot!.VersionHash);
        first.Snapshot!.ShadowNav.Should().Be(second.Snapshot!.ShadowNav);
        first.Outcome.State.Should().Be(OperationTerminalState.Succeeded);
        second.Outcome.State.Should().Be(OperationTerminalState.Succeeded);
        VerifiedOperationOutcomeValidator.Validate(first.Outcome).Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_Breach_EmitsBreak_AndBlocksCloseWhenPolicyEnabled()
    {
        var repo = new FileReconciliationBreakQueueRepository(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")), NullLogger<FileReconciliationBreakQueueRepository>.Instance);
        var service = new ShadowBookValuationService(repo, new Dictionary<string, ShadowBookVarianceThresholds>
        {
            ["Macro"] = new ShadowBookVarianceThresholds(5m, 1m)
        });

        var result = await service.RunAsync(BuildRequest(externalNav: 1_000_000m));

        result.Snapshot.Should().NotBeNull();
        result.Snapshot!.Breached.Should().BeTrue();
        result.EmittedBreak.Should().NotBeNull();
        result.CloseBlocked.Should().BeTrue();
        result.Outcome.State.Should().Be(OperationTerminalState.Succeeded);
        result.Outcome.Postconditions.Should().Contain(postcondition =>
            postcondition.Code == "breach-case-retained" &&
            postcondition.State == OperationPostconditionState.Satisfied);
        result.EmittedBreak!.LedgerBookId.Should().Be(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        result.EmittedBreak.AccountingPeriodId.Should().Be("22222222-2222-2222-2222-222222222222");
        result.EmittedBreak.AsOfDate.Should().Be(new DateOnly(2026, 3, 31));
        result.EmittedBreak.FundAccountId.Should().Be("acct-001");
        result.EmittedBreak.Measures.Should().HaveCount(3);
        result.EmittedBreak.Measures!.Single(measure => measure.Kind == ReconciliationBreakMeasureKindDto.Value)
            .Should().Match<ReconciliationBreakMeasureDto>(measure =>
                measure.Expected == 1_000_000m &&
                measure.Actual == result.Snapshot!.ShadowNav &&
                measure.Variance == result.Snapshot!.VarianceAmount &&
                measure.Unit == "USD");
        result.EmittedBreak.Measures
            .Where(measure => measure.Kind is ReconciliationBreakMeasureKindDto.Quantity or ReconciliationBreakMeasureKindDto.CostBasis)
            .Should().OnlyContain(measure => !string.IsNullOrWhiteSpace(measure.UnavailableReason));
        result.EmittedBreak.BlockedOutputs.Should().BeEquivalentTo("accounting-close", "certified-reporting");
        result.EmittedBreak!.EvidenceLinks.Should().ContainSingle(link => link == $"urn:sha256:{result.Snapshot!.VersionHash}");
        var queue = await repo.GetAllAsync();
        queue.Should().ContainSingle(item => item.BreakId == result.EmittedBreak!.BreakId);
    }

    [Fact]
    public async Task RunAsync_WithinThreshold_DoesNotEmitBreak()
    {
        var repo = new FileReconciliationBreakQueueRepository(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")), NullLogger<FileReconciliationBreakQueueRepository>.Instance);
        var service = new ShadowBookValuationService(repo, new Dictionary<string, ShadowBookVarianceThresholds>
        {
            ["Macro"] = new ShadowBookVarianceThresholds(100_000m, 10_000m)
        });

        var result = await service.RunAsync(BuildRequest(externalNav: 259_500m) with { BlockCloseOnBreach = false });

        result.Snapshot.Should().NotBeNull();
        result.Snapshot!.Breached.Should().BeFalse();
        result.EmittedBreak.Should().BeNull();
        result.CloseBlocked.Should().BeFalse();
        result.Outcome.State.Should().Be(OperationTerminalState.Succeeded);
    }

    [Fact]
    public async Task RunAsync_MissingAccountingScope_ReturnsBlockedOutcome()
    {
        var repo = new FileReconciliationBreakQueueRepository(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")), NullLogger<FileReconciliationBreakQueueRepository>.Instance);
        var service = new ShadowBookValuationService(repo);

        var result = await service.RunAsync(BuildRequest(externalNav: 1_000_000m) with
        {
            AccountingPeriodId = null
        });

        result.Snapshot.Should().BeNull();
        result.EmittedBreak.Should().BeNull();
        result.CloseBlocked.Should().BeTrue();
        result.Outcome.State.Should().Be(OperationTerminalState.Blocked);
        result.Outcome.Issues.Should().ContainSingle(issue =>
            issue.Code == "shadow-accounting-period-scope-missing" && issue.IsBlocking);
        VerifiedOperationOutcomeValidator.Validate(result.Outcome).Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_MissingPositionPrice_ReturnsBlockedOutcome()
    {
        var repo = new FileReconciliationBreakQueueRepository(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")), NullLogger<FileReconciliationBreakQueueRepository>.Instance);
        var service = new ShadowBookValuationService(repo);
        var request = BuildRequest(externalNav: 1_000_000m);

        var result = await service.RunAsync(request with
        {
            PricesBySymbol = new Dictionary<string, decimal> { ["AAPL"] = 180m }
        });

        result.Snapshot.Should().BeNull();
        result.EmittedBreak.Should().BeNull();
        result.Outcome.State.Should().Be(OperationTerminalState.Blocked);
        result.Outcome.Issues.Should().ContainSingle(issue => issue.Code == "shadow-price-missing");
    }

    [Fact]
    public async Task RunAsync_MissingNonBaseFx_ReturnsBlocked_ButBaseFxDefaultsToOne()
    {
        var repo = new FileReconciliationBreakQueueRepository(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")), NullLogger<FileReconciliationBreakQueueRepository>.Instance);
        var service = new ShadowBookValuationService(repo, new Dictionary<string, ShadowBookVarianceThresholds>
        {
            ["Macro"] = new ShadowBookVarianceThresholds(100_000m, 10_000m)
        });
        var request = BuildRequest(externalNav: 259_500m);

        var blocked = await service.RunAsync(request with
        {
            FxRatesByCurrency = new Dictionary<string, decimal> { ["USD"] = 1m }
        });
        var succeeded = await service.RunAsync(request with
        {
            FxRatesByCurrency = new Dictionary<string, decimal> { ["EUR"] = 1.1m }
        });

        blocked.Snapshot.Should().BeNull();
        blocked.Outcome.State.Should().Be(OperationTerminalState.Blocked);
        blocked.Outcome.Issues.Should().ContainSingle(issue => issue.Code == "shadow-fx-rate-missing");
        succeeded.Snapshot.Should().NotBeNull();
        succeeded.Outcome.State.Should().Be(OperationTerminalState.Succeeded);
    }

    [Fact]
    public async Task RunAsync_HashIncludesPolicyAndResolvedThresholds()
    {
        var firstRepo = new FileReconciliationBreakQueueRepository(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")), NullLogger<FileReconciliationBreakQueueRepository>.Instance);
        var secondRepo = new FileReconciliationBreakQueueRepository(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")), NullLogger<FileReconciliationBreakQueueRepository>.Instance);
        var firstService = new ShadowBookValuationService(firstRepo, new Dictionary<string, ShadowBookVarianceThresholds>
        {
            ["Macro"] = new ShadowBookVarianceThresholds(100_000m, 10_000m)
        });
        var secondService = new ShadowBookValuationService(secondRepo, new Dictionary<string, ShadowBookVarianceThresholds>
        {
            ["Macro"] = new ShadowBookVarianceThresholds(200_000m, 10_000m)
        });
        var request = BuildRequest(externalNav: 259_500m);

        var first = await firstService.RunAsync(request);
        var changedPolicy = await firstService.RunAsync(request with { BlockCloseOnBreach = false });
        var changedThreshold = await secondService.RunAsync(request);

        first.Snapshot.Should().NotBeNull();
        changedPolicy.Snapshot.Should().NotBeNull();
        changedThreshold.Snapshot.Should().NotBeNull();
        first.Snapshot!.VersionHash.Should().NotBe(changedPolicy.Snapshot!.VersionHash);
        first.Snapshot.VersionHash.Should().NotBe(changedThreshold.Snapshot!.VersionHash);
    }

    [Fact]
    public async Task RunAsync_QueuePersistenceConflict_ReturnsFailedWithoutPublishingSnapshot()
    {
        var repo = Substitute.For<IReconciliationBreakQueueRepository>();
        repo.CreateIfMissingAsync(Arg.Any<ReconciliationBreakQueueItem>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException<bool>(new InvalidOperationException(
                "Reconciliation break id is already bound to different source or scope input.")));
        var service = new ShadowBookValuationService(repo, new Dictionary<string, ShadowBookVarianceThresholds>
        {
            ["Macro"] = new ShadowBookVarianceThresholds(5m, 1m)
        });

        var result = await service.RunAsync(BuildRequest(externalNav: 1_000_000m));

        result.Snapshot.Should().BeNull();
        result.EmittedBreak.Should().BeNull();
        result.CloseBlocked.Should().BeTrue();
        result.Outcome.State.Should().Be(OperationTerminalState.Failed);
        result.Outcome.Issues.Should().ContainSingle(issue =>
            issue.Code == "shadow-break-persistence-conflict" && !issue.IsBlocking);
        result.Outcome.Recovery.Should().ContainSingle();
        VerifiedOperationOutcomeValidator.Validate(result.Outcome).Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_NumericOverflow_ReturnsFailedOutcome()
    {
        var repo = new FileReconciliationBreakQueueRepository(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")), NullLogger<FileReconciliationBreakQueueRepository>.Instance);
        var service = new ShadowBookValuationService(repo);
        var request = BuildRequest(externalNav: 1_000_000m);

        var result = await service.RunAsync(request with
        {
            Positions = [new ShadowBookPositionInput("AAPL", decimal.MaxValue, "USD")],
            PricesBySymbol = new Dictionary<string, decimal> { ["AAPL"] = 2m },
            FxRatesByCurrency = new Dictionary<string, decimal>()
        });

        result.Snapshot.Should().BeNull();
        result.EmittedBreak.Should().BeNull();
        result.CloseBlocked.Should().BeTrue();
        result.Outcome.State.Should().Be(OperationTerminalState.Failed);
        result.Outcome.Issues.Should().ContainSingle(issue => issue.Code == "shadow-valuation-calculation-failed");
        result.Outcome.Postconditions.Should().ContainSingle(postcondition =>
            postcondition.Code == "shadow-valuation-computed" &&
            postcondition.State == OperationPostconditionState.NotSatisfied);
        VerifiedOperationOutcomeValidator.Validate(result.Outcome).Should().BeEmpty();
    }

    private static ShadowBookValuationRequest BuildRequest(decimal externalNav) => new(
        AccountId: "acct-001",
        StrategyProfile: "Macro",
        PeriodEnd: new DateOnly(2026, 3, 31),
        Positions:
        [
            new ShadowBookPositionInput("AAPL", 1000m, "USD"),
            new ShadowBookPositionInput("SAP", 500m, "EUR")
        ],
        PricesBySymbol: new Dictionary<string, decimal> { ["AAPL"] = 180m, ["SAP"] = 130m },
        FxRatesByCurrency: new Dictionary<string, decimal> { ["USD"] = 1m, ["EUR"] = 1.1m },
        Fees: 2_500m,
        Accruals: 10_000m,
        ExternalReferenceNav: externalNav,
        BlockCloseOnBreach: true,
        OperatorId: "operator-a",
        LedgerBookId: Guid.Parse("11111111-1111-1111-1111-111111111111"),
        BaseCurrency: "USD",
        AccountingPeriodId: Guid.Parse("22222222-2222-2222-2222-222222222222"));
}

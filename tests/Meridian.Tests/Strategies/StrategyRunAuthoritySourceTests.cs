using FluentAssertions;
using Meridian.Strategies.Interfaces;
using Meridian.Strategies.Models;
using Meridian.Strategies.Promotions;
using Meridian.Strategies.Services;
using Meridian.Strategies.Storage;
using Xunit;

namespace Meridian.Tests.Strategies;

/// <summary>
/// The OMS reads strategy-run authority through <see cref="StrategyRunAuthoritySource"/>. Only an
/// approved live promotion that targets the exact run may confer live authority.
/// </summary>
public sealed class StrategyRunAuthoritySourceTests
{
    private static readonly Guid AccountId = Guid.Parse("53bf0251-17f6-4fb7-8dbe-6fb4966e2749");
    private readonly StrategyRunStore _runs = new();
    private readonly RecordedPromotions _promotions = new();

    [Fact]
    public async Task GetAsync_UnknownRun_ReturnsNull()
    {
        var source = new StrategyRunAuthoritySource(_runs, _promotions);

        (await source.GetAsync("missing-run")).Should().BeNull();
    }

    [Fact]
    public async Task GetAsync_ActiveRunWithApprovedLivePromotion_ReportsTheBinding()
    {
        var run = await RecordLiveRunAsync();
        await _promotions.AppendAsync(Promotion(run.RunId, RunType.Live, PromotionDecisionKinds.Approved));
        var source = new StrategyRunAuthoritySource(_runs, _promotions);

        var snapshot = await source.GetAsync(run.RunId);

        snapshot.Should().NotBeNull();
        snapshot!.IsActive.Should().BeTrue();
        snapshot.FundAccountId.Should().Be(AccountId);
        snapshot.PromotionTargetRunId.Should().Be(run.RunId);
        snapshot.PromotionAuditReference.Should().Be("audit://promotion");
    }

    [Theory]
    [InlineData(RunType.Live, PromotionDecisionKinds.Rejected)]
    [InlineData(RunType.Paper, PromotionDecisionKinds.Approved)]
    public async Task GetAsync_RejectedOrNonLivePromotion_ConfersNoLiveAuthority(RunType targetType, string decision)
    {
        var run = await RecordLiveRunAsync();
        await _promotions.AppendAsync(Promotion(run.RunId, targetType, decision));
        var source = new StrategyRunAuthoritySource(_runs, _promotions);

        var snapshot = await source.GetAsync(run.RunId);

        snapshot!.PromotionTargetRunId.Should().BeNull();
    }

    [Fact]
    public async Task GetAsync_EndedRun_IsNotActive()
    {
        var run = await RecordLiveRunAsync(ended: true);
        var source = new StrategyRunAuthoritySource(_runs, _promotions);

        (await source.GetAsync(run.RunId))!.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task GetAsync_ApprovedPromotion_IsReadFromTheStoreOnce_WhileTheRunIsReadEveryTime()
    {
        var run = await RecordLiveRunAsync();
        await _promotions.AppendAsync(Promotion(run.RunId, RunType.Live, PromotionDecisionKinds.Approved));
        var source = new StrategyRunAuthoritySource(_runs, _promotions);

        (await source.GetAsync(run.RunId))!.PromotionTargetRunId.Should().Be(run.RunId);
        await _runs.RecordRunAsync(run with { EndedAt = DateTimeOffset.UtcNow });
        var afterEnd = await source.GetAsync(run.RunId);

        _promotions.Loads.Should().Be(1, "an approval found for a run is remembered; promotion history is append-only");
        afterEnd!.IsActive.Should().BeFalse("whether the run is still active is read on every check");
        afterEnd.PromotionTargetRunId.Should().Be(run.RunId);
    }

    [Fact]
    public async Task GetAsync_RunWithoutApproval_IsLookedUpAgainUntilApproved()
    {
        var run = await RecordLiveRunAsync();
        var source = new StrategyRunAuthoritySource(_runs, _promotions);

        (await source.GetAsync(run.RunId))!.PromotionTargetRunId.Should().BeNull();
        await _promotions.AppendAsync(Promotion(run.RunId, RunType.Live, PromotionDecisionKinds.Approved));

        (await source.GetAsync(run.RunId))!.PromotionTargetRunId.Should().Be(run.RunId,
            "a missing approval is never remembered");
        _promotions.Loads.Should().Be(2);
    }

    private async Task<StrategyRunEntry> RecordLiveRunAsync(bool ended = false)
    {
        var run = StrategyRunEntry.Start("authority-strategy", "Authority Strategy", RunType.Live) with
        {
            FundAccountId = AccountId,
            EndedAt = ended ? DateTimeOffset.UtcNow : null
        };
        await _runs.RecordRunAsync(run);
        return run;
    }

    private static StrategyPromotionRecord Promotion(string targetRunId, RunType targetType, string decision) => new(
        PromotionId: Guid.NewGuid().ToString("N"),
        StrategyId: "authority-strategy",
        StrategyName: "Authority Strategy",
        SourceRunType: RunType.Paper,
        TargetRunType: targetType,
        SourceRunId: "source-run",
        TargetRunId: targetRunId,
        QualifyingSharpe: 1.5,
        QualifyingMaxDrawdownPercent: 5m,
        QualifyingTotalReturn: 0.1m,
        Decision: decision,
        PromotedAt: DateTimeOffset.UtcNow,
        AuditReference: "audit://promotion");

    /// <summary>Holds promotion history as given; the durable store's own validation is covered elsewhere.</summary>
    private sealed class RecordedPromotions : IPromotionRecordStore
    {
        private readonly List<StrategyPromotionRecord> _records = [];

        public int Loads { get; private set; }

        public Task<IReadOnlyList<StrategyPromotionRecord>> LoadAllAsync(CancellationToken ct = default)
        {
            Loads++;
            return Task.FromResult<IReadOnlyList<StrategyPromotionRecord>>(_records.ToArray());
        }

        public Task<PromotionDecisionReservation> ReserveFirstDecisionAsync(
            StrategyPromotionRecord record,
            CancellationToken ct = default) =>
            throw new NotSupportedException("The authority source only reads promotion history.");

        public Task AppendAsync(StrategyPromotionRecord record, CancellationToken ct = default)
        {
            _records.Add(record);
            return Task.CompletedTask;
        }
    }
}

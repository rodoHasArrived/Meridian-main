using FluentAssertions;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// Verifies the effective-state fold over append-only corporate-action events: alias
/// normalization, supersede-chain collapsing, cancellation, orphan tolerance, and
/// calendar-derived lifecycle states.
/// </summary>
[Trait("Category", "Unit")]
public sealed class CorporateActionEffectiveStateProjectorTests
{
    private static readonly Guid SecurityId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTimeOffset AsOf = new(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Project_EmptyList_ReturnsEmpty()
    {
        CorporateActionEffectiveStateProjector.Project([], AsOf).Should().BeEmpty();
    }

    [Fact]
    public void Project_SingleFutureAction_DefaultsNullStateToConfirmed()
    {
        var dividend = MakeAction("Dividend", exDate: new DateOnly(2026, 8, 14), dividendPerShare: 0.24m);

        var states = CorporateActionEffectiveStateProjector.Project([dividend], AsOf);

        var state = states.Should().ContainSingle().Subject;
        state.LifecycleState.Should().Be(CorporateActionLifecycleStates.Confirmed);
        state.IsCancelled.Should().BeFalse();
        state.Timeline.Should().ContainSingle().Which.CorpActId.Should().Be(dividend.CorpActId);
    }

    [Fact]
    public void Project_DerivesExAndPaidFromCalendar()
    {
        var wentEx = MakeAction("Dividend", exDate: new DateOnly(2026, 6, 15), payDate: new DateOnly(2026, 7, 15), dividendPerShare: 0.24m);
        var paid = MakeAction("Dividend", exDate: new DateOnly(2026, 5, 15), payDate: new DateOnly(2026, 6, 1), dividendPerShare: 0.20m);

        var states = CorporateActionEffectiveStateProjector.Project([wentEx, paid], AsOf);

        states.Should().HaveCount(2);
        states.Single(state => state.Effective.CorpActId == wentEx.CorpActId)
            .LifecycleState.Should().Be(CorporateActionLifecycleStates.Ex);
        states.Single(state => state.Effective.CorpActId == paid.CorpActId)
            .LifecycleState.Should().Be(CorporateActionLifecycleStates.Paid);
    }

    [Fact]
    public void Project_AmendmentChain_FoldsToTipWithFullTimeline()
    {
        var original = MakeAction("Dividend", exDate: new DateOnly(2026, 8, 14), dividendPerShare: 0.24m);
        var amendment = MakeAction("Dividend", exDate: new DateOnly(2026, 8, 14), dividendPerShare: 0.26m) with
        {
            SupersedesCorpActId = original.CorpActId
        };

        var states = CorporateActionEffectiveStateProjector.Project([original, amendment], AsOf);

        var state = states.Should().ContainSingle().Subject;
        state.Effective.CorpActId.Should().Be(amendment.CorpActId);
        state.Effective.DividendPerShare.Should().Be(0.26m);
        state.Timeline.Select(static entry => entry.CorpActId)
            .Should().ContainInOrder(original.CorpActId, amendment.CorpActId);
    }

    [Fact]
    public void Project_AmendmentRecordedAfterAsOf_PreservesKnownRevision()
    {
        var original = MakeAction("Split", exDate: new DateOnly(2026, 8, 14), splitRatio: 2m) with
        {
            RecordedAtUtc = AsOf.AddDays(-1)
        };
        var amendment = MakeAction("Split", exDate: new DateOnly(2026, 8, 14), splitRatio: 4m) with
        {
            SupersedesCorpActId = original.CorpActId,
            RecordedAtUtc = AsOf.AddDays(1)
        };

        var state = CorporateActionEffectiveStateProjector.Project([original, amendment], AsOf)
            .Should().ContainSingle().Subject;

        state.Effective.CorpActId.Should().Be(original.CorpActId);
        state.Effective.SplitRatio.Should().Be(2m);
        state.Timeline.Should().ContainSingle();
    }

    [Fact]
    public void Project_CancelledChain_ReportsCancelledAndDropsFromEffectiveActions()
    {
        var original = MakeAction("Dividend", exDate: new DateOnly(2026, 8, 14), dividendPerShare: 0.24m);
        var cancellation = MakeAction("Dividend", exDate: new DateOnly(2026, 8, 14), dividendPerShare: 0.24m) with
        {
            SupersedesCorpActId = original.CorpActId,
            LifecycleState = CorporateActionLifecycleStates.Cancelled
        };

        var states = CorporateActionEffectiveStateProjector.Project([original, cancellation], AsOf);
        states.Should().ContainSingle().Which.IsCancelled.Should().BeTrue();

        CorporateActionEffectiveStateProjector.ProjectEffectiveActions([original, cancellation], AsOf)
            .Should().BeEmpty();
    }

    [Fact]
    public void Project_CancelledState_IsNotOverriddenByCalendarDerivation()
    {
        var original = MakeAction("Dividend", exDate: new DateOnly(2026, 5, 15), dividendPerShare: 0.24m);
        var cancellation = MakeAction("Dividend", exDate: new DateOnly(2026, 5, 15), dividendPerShare: 0.24m) with
        {
            SupersedesCorpActId = original.CorpActId,
            LifecycleState = CorporateActionLifecycleStates.Cancelled
        };

        var state = CorporateActionEffectiveStateProjector.Project([original, cancellation], AsOf)
            .Should().ContainSingle().Subject;

        state.LifecycleState.Should().Be(CorporateActionLifecycleStates.Cancelled);
    }

    [Fact]
    public void Project_OrphanSupersedeReference_StillProjectsTheTip()
    {
        var orphanTip = MakeAction("Dividend", exDate: new DateOnly(2026, 8, 14), dividendPerShare: 0.26m) with
        {
            SupersedesCorpActId = Guid.NewGuid()
        };

        var state = CorporateActionEffectiveStateProjector.Project([orphanTip], AsOf)
            .Should().ContainSingle().Subject;

        state.Effective.CorpActId.Should().Be(orphanTip.CorpActId);
        state.Timeline.Should().ContainSingle();
    }

    [Fact]
    public void Project_NormalizesStoredLegacyAliases()
    {
        var legacySplit = MakeAction("Split", exDate: new DateOnly(2026, 8, 14), splitRatio: 4m);

        var state = CorporateActionEffectiveStateProjector.Project([legacySplit], AsOf)
            .Should().ContainSingle().Subject;

        state.Effective.EventType.Should().Be(CorporateActionEventTypes.StockSplit);
    }

    [Fact]
    public void Project_OrdersByEffectiveExDate()
    {
        var later = MakeAction("Dividend", exDate: new DateOnly(2026, 9, 1), dividendPerShare: 0.3m);
        var earlier = MakeAction("Split", exDate: new DateOnly(2026, 8, 1), splitRatio: 2m);

        var states = CorporateActionEffectiveStateProjector.Project([later, earlier], AsOf);

        states.Select(static state => state.Effective.ExDate)
            .Should().BeInAscendingOrder();
    }

    private static CorporateActionDto MakeAction(
        string eventType,
        DateOnly exDate,
        DateOnly? payDate = null,
        decimal? dividendPerShare = null,
        decimal? splitRatio = null)
        => new(
            CorpActId: Guid.NewGuid(),
            SecurityId: SecurityId,
            EventType: eventType,
            ExDate: exDate,
            PayDate: payDate,
            DividendPerShare: dividendPerShare,
            Currency: dividendPerShare.HasValue ? "USD" : null,
            SplitRatio: splitRatio,
            NewSecurityId: null,
            DistributionRatio: null,
            AcquirerSecurityId: null,
            ExchangeRatio: null,
            SubscriptionPricePerShare: null,
            RightsPerShare: null);
}

/// <summary>
/// Locks the write-side lifecycle vocabulary and its supersede-ordering semantics.
/// </summary>
[Trait("Category", "Unit")]
public sealed class CorporateActionLifecycleStatesTests
{
    [Theory]
    [InlineData(null, true)]
    [InlineData(CorporateActionLifecycleStates.Announced, true)]
    [InlineData(CorporateActionLifecycleStates.Confirmed, true)]
    [InlineData(CorporateActionLifecycleStates.Cancelled, true)]
    [InlineData(CorporateActionLifecycleStates.Ex, false)]
    [InlineData(CorporateActionLifecycleStates.Paid, false)]
    [InlineData("Pending", false)]
    public void IsWriteable_AcceptsOnlyWriteSideStates(string? state, bool expected)
    {
        CorporateActionLifecycleStates.IsWriteable(state).Should().Be(expected);
    }

    [Fact]
    public void Rank_OrdersLifecycleMonotonically_WithCancelledAboveAll()
    {
        CorporateActionLifecycleStates.Rank(CorporateActionLifecycleStates.Announced)
            .Should().BeLessThan(CorporateActionLifecycleStates.Rank(null));
        CorporateActionLifecycleStates.Rank(null)
            .Should().Be(CorporateActionLifecycleStates.Rank(CorporateActionLifecycleStates.Confirmed));
        CorporateActionLifecycleStates.Rank(CorporateActionLifecycleStates.Ex)
            .Should().BeLessThan(CorporateActionLifecycleStates.Rank(CorporateActionLifecycleStates.Paid));
        CorporateActionLifecycleStates.Rank(CorporateActionLifecycleStates.Cancelled)
            .Should().BeGreaterThan(CorporateActionLifecycleStates.Rank(CorporateActionLifecycleStates.Paid));
        CorporateActionLifecycleStates.Rank("Pending").Should().Be(-1);
    }
}

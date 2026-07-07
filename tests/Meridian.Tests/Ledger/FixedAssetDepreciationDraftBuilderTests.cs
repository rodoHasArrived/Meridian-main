using FluentAssertions;
using Meridian.Ledger;
using Xunit;

namespace Meridian.Tests.Ledger;

/// <summary>
/// Tests that the depreciation draft builder batches every asset's charge for a period into a
/// single governed <see cref="AutomatedJournalDraft"/> that flows through
/// <see cref="AutomatedJournalApproval"/>.
/// </summary>
public sealed class FixedAssetDepreciationDraftBuilderTests
{
    private static readonly Guid AssetA = new("33333333-3333-3333-3333-333333333333");
    private static readonly Guid AssetB = new("44444444-4444-4444-4444-444444444444");
    private static readonly DateOnly PeriodEnd = new(2026, 1, 31);
    private static readonly DateTimeOffset AsOf = new(2026, 1, 31, 21, 0, 0, TimeSpan.Zero);

    private static DepreciationInput Input(Guid assetId, decimal amount)
        => new(assetId, LedgerAccounts.FixedAssetCostFor(assetId.ToString("D")), amount);

    [Fact]
    public void BuildDraft_BatchesAllAssetsIntoOneDraft()
    {
        var draft = FixedAssetDepreciationDraftBuilder.BuildDraft(
            PeriodEnd,
            AsOf,
            [Input(AssetA, 100m), Input(AssetB, 200m)],
            bookLabel: "fund-1");

        draft.Should().NotBeNull();
        draft!.Event.Kind.Should().Be(AutomatedJournalEventKind.DepreciationPosted);
        draft.IsBalanced.Should().BeTrue();
        draft.Lines.Should().HaveCount(4); // two lines per asset
        draft.TotalDebits.Should().Be(300m);
        draft.Event.Amount.Should().Be(300m);
        draft.Event.EffectiveDate.Should().Be(PeriodEnd);
        draft.Metadata.Tags.Should().ContainKey("depreciation.assetCount")
            .WhoseValue.Should().Be("2");
    }

    [Fact]
    public void BuildDraft_NoAssets_ReturnsNull()
    {
        var draft = FixedAssetDepreciationDraftBuilder.BuildDraft(PeriodEnd, AsOf, []);

        draft.Should().BeNull();
    }

    [Fact]
    public void BuildDraft_SkipsZeroAndNegativeCharges()
    {
        var draft = FixedAssetDepreciationDraftBuilder.BuildDraft(
            PeriodEnd,
            AsOf,
            [Input(AssetA, 0m), Input(AssetB, 150m)]);

        draft.Should().NotBeNull();
        draft!.Lines.Should().HaveCount(2); // only AssetB projected
        draft.TotalDebits.Should().Be(150m);
    }

    [Fact]
    public void BuildDraft_AllZeroCharges_ReturnsNull()
    {
        var draft = FixedAssetDepreciationDraftBuilder.BuildDraft(
            PeriodEnd,
            AsOf,
            [Input(AssetA, 0m)]);

        draft.Should().BeNull();
    }

    [Fact]
    public void BuildDraft_ProducesSubmittableApproval()
    {
        var draft = FixedAssetDepreciationDraftBuilder.BuildDraft(
            PeriodEnd,
            AsOf,
            [Input(AssetA, 100m), Input(AssetB, 200m)]);

        var approval = AutomatedJournalApproval.Submit(draft!, "controller", AsOf, "January depreciation run");

        approval.Status.Should().Be(AutomatedJournalApprovalStatus.Submitted);
        approval.Draft.Event.Kind.Should().Be(AutomatedJournalEventKind.DepreciationPosted);
    }

    [Fact]
    public void BuildDraft_IdempotencyKeyIsStableForBookAndPeriod()
    {
        var first = FixedAssetDepreciationDraftBuilder.BuildDraft(PeriodEnd, AsOf, [Input(AssetA, 100m)], bookLabel: "fund-1");
        var second = FixedAssetDepreciationDraftBuilder.BuildDraft(PeriodEnd, AsOf, [Input(AssetA, 100m)], bookLabel: "fund-1");

        first!.Event.IdempotencyKey.Should().Be(second!.Event.IdempotencyKey);
        first.Event.IdempotencyKey.Should().Be("depreciation|fund-1|2026-01-31");
    }
}

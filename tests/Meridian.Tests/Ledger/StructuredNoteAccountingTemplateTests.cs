using FluentAssertions;
using Meridian.Contracts.SecurityMaster;
using Meridian.Ledger;
using Xunit;

namespace Meridian.Tests.Ledger;

public sealed class StructuredNoteAccountingTemplateTests
{
    private static readonly Guid SecurityId = Guid.Parse("55200000-0000-0000-0000-000000000001");
    private static readonly DateTimeOffset AsOf = new(2026, 11, 12, 21, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(101, 1010, 10, LedgerAccountType.Revenue)]
    [InlineData(97, 970, -30, LedgerAccountType.Expense)]
    public void Valuation_ConvertsFaceQuotesAndPostsOnlyTheCarryingValueChange(
        decimal quote, decimal marketValue, decimal adjustment, LedgerAccountType resultAccountType)
    {
        var projection = Project(Mark(quote));
        var row = projection.Lines.Should().ContainSingle().Which;
        row.Quantity.Should().Be(1000m, "note quantity is monetary face, not a count of hundred-par units");
        row.CostBasis.Should().Be(980m);
        row.MarketValue.Should().Be(marketValue);
        row.MarkAdjustment.Should().Be(adjustment);
        row.UnrealizedGainOrLoss.Should().Be(marketValue - 980m);

        var draft = DailyPortfolioPricingDraftBuilder.BuildDrafts(projection).Should().ContainSingle().Which;
        draft.IsBalanced.Should().BeTrue();
        draft.TotalDebits.Should().Be(Math.Abs(adjustment));
        draft.Lines.Should().HaveCount(2);
        draft.Lines.Should().ContainSingle(line => line.account.AccountType == resultAccountType);
        draft.Lines.Should().OnlyContain(line => line.account.AccountType != LedgerAccountType.Equity);
        draft.Metadata.Tags!["accounting.template"].Should().Be(StructuredNoteAccountingTemplate.TemplateId);
        draft.Metadata.Tags["accounting.classification"].Should().Be("FairValueThroughProfitOrLoss");
        draft.Metadata.Tags.Should().ContainKey(ValuationMarkEvidenceGuard.EvidenceTag);
        draft.Metadata.SecurityId.Should().Be(SecurityId);
        draft.Lines.Should().OnlyContain(line => line.dimensions!.InstrumentId == SecurityId
            && line.dimensions.EntityId == "entity-1" && line.dimensions.AccountId == "account-1");

        var submitted = AutomatedJournalApproval.Submit(draft, "preparer", AsOf, "Retained note mark");
        var unapprovedPost = () => submitted.ToJournalEntry();
        unapprovedPost.Should().Throw<InvalidOperationException>().WithMessage("*approved*");
        var approved = submitted.Approve("reviewer", AsOf, "Reviewed retained valuation", ["evidence://review/note"]);
        approved.ToJournalEntry().Lines.Should().HaveCount(2);
    }

    [Fact]
    public void NoteTreatment_DoesNotAdmitCouponOrBondAccrualPosting()
    {
        var descriptor = SecurityAssetClassCatalog.GetOrDefault("StructuredNote");
        descriptor.ValuationAccountingTreatment.Should().Be(SecurityValuationAccountingTreatment.FairValueThroughProfitOrLoss);
        descriptor.AccountingDisposition.Should().Be(SecurityAccountingDisposition.OutsideSlice);
        descriptor.QuoteConvention.Should().Be(SecurityQuoteConvention.PercentOfPar);
        SecurityAssetClassCatalog.ResolveAccountingInstrumentClass("StructuredNote", "FixedIncome").Should().BeNull();
        StructuredNoteAccountingTemplate.AppliesTo("Bond").Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("2026-10-01")]
    public void Valuation_RefusesUndatedOrStaleMarks(string? observed)
    {
        var mark = Mark(101m, observed is null ? null : DateOnly.Parse(observed));
        var project = () => Project(mark);
        project.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Valuation_RequiresCanonicalIdentityAndRetainedEvidenceAtApproval()
    {
        var unresolved = () => Project(Mark(101m, securityId: null));
        unresolved.Should().Throw<InvalidOperationException>().WithMessage("*Security Master identity*");

        var draft = DailyPortfolioPricingDraftBuilder.BuildDrafts(Project(Mark(101m))).Single();
        var retainedTags = new Dictionary<string, string>(draft.Metadata.Tags!);
        retainedTags.Remove(ValuationMarkEvidenceGuard.EvidenceTag);
        retainedTags.Remove(ValuationMarkEvidenceGuard.DigestTag);
        var withoutEvidence = draft with { Metadata = draft.Metadata with { Tags = retainedTags } };
        var submit = () => AutomatedJournalApproval.Submit(withoutEvidence, "preparer", AsOf, "No retained mark");
        submit.Should().Throw<InvalidOperationException>().WithMessage("*evidence*");
    }

    [Fact]
    public void Retry_ReusesIdentityAndCorrectedMarksGetNewIdentity()
    {
        string? Key(decimal quote) => DailyPortfolioPricingDraftBuilder.BuildDrafts(Project(Mark(quote)))
            .Single().Metadata.IdempotencyKey;
        Key(101m).Should().Be(Key(101m));
        Key(102m).Should().NotBe(Key(101m));
        DailyPortfolioPricingDraftBuilder.BuildDrafts(Project(Mark(100m))).Should().BeEmpty();
    }

    [Theory]
    [InlineData("JPY", 1010, 10)]
    [InlineData("KWD", 1010.025, 10.025)]
    public void NoteValuation_RespectsCurrencyMinorUnits(string currency, decimal marketValue, decimal adjustment)
    {
        var projection = Project(Mark(101.0025m), currency);
        projection.TotalMarketValue.Should().Be(marketValue);
        projection.NetMarkAdjustment.Should().Be(adjustment);
        projection.IsBalanced.Should().BeTrue();
    }

    private static DailyPortfolioPriceMark Mark(decimal quote)
        => Mark(quote, DateOnly.FromDateTime(AsOf.UtcDateTime));

    private static DailyPortfolioPriceMark Mark(decimal quote, DateOnly? observed, Guid? securityId)
        => new("NOTE", 1000m, 98m, quote, "retained-note-close", "evidence://note/mark",
            FinancialAccountId: "account-1", InstrumentType: "StructuredNote", PriceObservedOn: observed,
            SecurityId: securityId, PriorCarryingValue: 1000m, CarryingValueSource: "ledger:book-1",
            CarryingValueEvidenceReference: "ledger://book-1/note");

    private static DailyPortfolioPriceMark Mark(decimal quote, DateOnly? observed)
        => Mark(quote, observed, SecurityId);

    private static DailyPortfolioPriceMark Mark(decimal quote, Guid? securityId)
        => Mark(quote, DateOnly.FromDateTime(AsOf.UtcDateTime), securityId);

    private static DailyPortfolioPricingProjection Project(DailyPortfolioPriceMark mark, string currency = "USD")
        => DailyPortfolioPricingProjector.Project(new DailyPortfolioPricingInput(
            new DailyPortfolioPricingPolicy("fund-1", "note-close-v1", "Reviewed note close", "retained-close",
                "policy-reviewer", AsOf.AddDays(-30)), "2026-11", AsOf, currency, [mark], "entity-1"));
}

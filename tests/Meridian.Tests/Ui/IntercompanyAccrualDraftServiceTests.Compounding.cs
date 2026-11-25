using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.FinancialOperations.Intercompany;

namespace Meridian.Tests.Ui;

/// <summary>
/// Rule intercompany-accrual-v3 through the draft service: the fixture's loan compounds monthly on its monthly schedule
/// from 1 June 2026, so June is the first period and compounds on nothing: 1,000,000 x 5% x 30/360 = 4,166.67.
/// </summary>
public sealed partial class IntercompanyAccrualDraftServiceTests
{
    [Fact]
    public async Task ACompoundingLoanDraftsUnderV3_RetainingThePostedInterestFactsItRead()
    {
        var proposal = await Service(compounding: true).PreviewAsync("tenant-a", "company-a", "preparer", Request());

        Assert.True(proposal.IsReady);
        Assert.Equal(4166.67m, proposal.Computation.Amount);
        Assert.Equal(IntercompanyAccrualRules.CompoundingRuleVersion, proposal.Evidence!.RuleVersion);
        var compounding = proposal.Evidence.Compounding!;
        Assert.Equal((From, new DateOnly(2026, 5, 15)), (compounding.BalanceFrom, compounding.FirstPrincipalDate));
        Assert.Empty(compounding.PostedWindows);
        Assert.Equal(0m, Assert.Single(proposal.Computation.Slices).CompoundedInterest);
        Assert.Null(IntercompanyAccrualEvidenceGuard.Validate(proposal.Draft!));
        // The window keeps the identity a simple accrual of it would have.
        Assert.Equal(IntercompanyAccrualSourceBuilder.DraftId("tenant-a", Request()), proposal.Draft!.JournalEntryId);
    }

    [Fact]
    public async Task PostedAccruedInterestThatChangesAfterDraftingMakesTheCompoundingDraftStale()
    {
        var evidence = (await Service(compounding: true).PreviewAsync("tenant-a", "company-a", "preparer", Request())).Evidence!;
        var lateInterest = Journal(new(2026, 6, 10), Interest, "a-int-recv", IntercompanyAccrualEvidenceGuard.TransactionId(new(2026, 6, 1), new(2026, 6, 11)), 1388.89m);

        Assert.Null(IntercompanyAccrualSourceBuilder.VerifyCompounding(evidence, []));
        Assert.Contains("accrued interest changed", IntercompanyAccrualSourceBuilder.VerifyCompounding(evidence, [lateInterest]));
    }

    [Fact]
    public async Task TheGuardRefusesV3EvidenceWithoutItsCompoundingFacts()
    {
        var draft = (await Service(compounding: true).PreviewAsync("tenant-a", "company-a", "preparer", Request())).Draft!;
        var e = IntercompanyAccrualEvidenceGuard.Deserialize(draft.IntercompanyAccrualEvidenceJson!)!;
        // Rebuilt through the constructor, so the init-only compounding facts are left out.
        var stripped = IntercompanyAccrualEvidenceGuard.Serialize(new IntercompanyAccrualEvidenceDto(e.RuleVersion, e.DraftId, e.TenantId,
            e.SourceBookId, e.SourceEntityId, e.CounterpartyEntityId, e.AccountingArrangementId, e.PostingArrangementRevisionId, e.AgreementId,
            e.PostingAgreementVersion, e.PostingAgreementPayloadHash, e.TransactionId, e.PostingDate, e.SourceRole, e.MonetaryAccountNodeId,
            e.OperatingAccountNodeId, e.FunctionalCurrency, e.FunctionalAmount, e.Computation, e.Principal, e.Fx));

        Assert.Contains("compounding", IntercompanyAccrualEvidenceGuard.Validate(draft with
        {
            IntercompanyAccrualEvidenceJson = stripped,
            IntercompanyAccrualEvidenceDigest = IntercompanyAccrualEvidenceGuard.Digest(stripped)
        }));
    }
}

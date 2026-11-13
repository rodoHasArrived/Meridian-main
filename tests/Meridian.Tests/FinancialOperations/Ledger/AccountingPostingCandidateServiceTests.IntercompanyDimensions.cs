using FluentAssertions;
using Meridian.Contracts.Ledger;
using Meridian.FinancialOperations.Ledger;

namespace Meridian.Tests.FinancialOperations.Ledger;

public sealed partial class AccountingPostingCandidateServiceTests
{
    [Fact]
    public async Task ApprovedCandidate_PostedAndReplayedImpactPreserveExactIntercompanyReferences()
    {
        var book = Guid.NewGuid();
        var period = Guid.NewGuid();
        var source = Guid.NewGuid();
        var entity = Guid.NewGuid();
        var candidates = await CreateSeededCandidateServiceAsync(ledgerBookId: book, entityId: entity.ToString("D"));
        var ledger = new RecordingLedgerJournalStore(BuildLedgerBook(book, AccountingBasisKindDto.Gaap) with
        { FundStructureNodeId = entity, FundStructureNodeKind = Meridian.Contracts.FundStructure.FundStructureNodeKindDto.Entity }, BuildPeriod(period, book));
        var service = new AccountingPostingCandidatePostService(candidates, ledger);
        var candidate = BuildCandidateRequest(book, period, source, AccountingBasisKindDto.Gaap, "gaap-accrual-v1");
        var dimensions = candidate.Dimensions! with
        {
            EntityId = entity.ToString("D"),
            CounterpartyId = Guid.NewGuid().ToString("D"),
            IntercompanyAgreementId = Guid.NewGuid(),
            IntercompanyAgreementVersion = 3,
            AccountingArrangementId = Guid.NewGuid().ToString("D"),
            AccountingArrangementRevisionId = Guid.NewGuid().ToString("D"),
            ChartAccountNodeId = "retained-account-node",
            AccountBindingRevisionId = Guid.NewGuid().ToString("D"),
            IntercompanyTransactionId = "reviewed-transaction-1"
        };
        var request = new PostPostingRuleJournalCandidateRequestDto(candidate with { Dimensions = dimensions, CounterpartyId = dimensions.CounterpartyId },
            "reviewer@meridian.local", "approval-retained-dimensions",
            EvidenceLinks: [ApprovalEvidence("fund-alpha", book, source)]);

        var posted = await service.PostCandidateAsync(request);
        var replay = await service.PostCandidateAsync(request);

        posted.WasReplay.Should().BeFalse();
        replay.WasReplay.Should().BeTrue();
        ledger.Appended.Should().ContainSingle();
        foreach (var impact in new[] { posted.JournalImpact!, replay.JournalImpact! })
        {
            impact.Lines.Should().NotBeEmpty();
            foreach (var line in impact.Lines)
            {
                line.Dimensions!.IntercompanyAgreementId.Should().Be(dimensions.IntercompanyAgreementId);
                line.Dimensions.IntercompanyAgreementVersion.Should().Be(dimensions.IntercompanyAgreementVersion);
                line.Dimensions!.AccountingArrangementId.Should().Be(dimensions.AccountingArrangementId);
                line.Dimensions.AccountingArrangementRevisionId.Should().Be(dimensions.AccountingArrangementRevisionId);
                line.Dimensions.ChartAccountNodeId.Should().Be(dimensions.ChartAccountNodeId);
                line.Dimensions.AccountBindingRevisionId.Should().Be(dimensions.AccountBindingRevisionId);
                line.Dimensions.IntercompanyTransactionId.Should().Be(dimensions.IntercompanyTransactionId);
                line.Dimensions.FundId.Should().Be(dimensions.FundId);
                line.Dimensions.BookId.Should().Be(dimensions.BookId);
            }
        }
    }
}

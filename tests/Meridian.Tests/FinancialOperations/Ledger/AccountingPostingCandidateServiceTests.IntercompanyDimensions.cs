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
        var candidates = await CreateSeededCandidateServiceAsync(ledgerBookId: book);
        var ledger = new RecordingLedgerJournalStore(BuildLedgerBook(book, AccountingBasisKindDto.Gaap), BuildPeriod(period, book));
        var service = new AccountingPostingCandidatePostService(candidates, ledger);
        var candidate = BuildCandidateRequest(book, period, source, AccountingBasisKindDto.Gaap, "gaap-accrual-v1");
        var dimensions = candidate.Dimensions! with
        {
            AgreementId = Guid.NewGuid().ToString("D"), AgreementRevisionId = Guid.NewGuid().ToString("D"),
            ChartAccountNodeId = "retained-account-node", AccountBindingRevisionId = Guid.NewGuid().ToString("D"),
            IntercompanyTransactionId = "reviewed-transaction-1"
        };
        var request = new PostPostingRuleJournalCandidateRequestDto(candidate with { Dimensions = dimensions },
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
                line.Dimensions!.AgreementId.Should().Be(dimensions.AgreementId);
                line.Dimensions.AgreementRevisionId.Should().Be(dimensions.AgreementRevisionId);
                line.Dimensions.ChartAccountNodeId.Should().Be(dimensions.ChartAccountNodeId);
                line.Dimensions.AccountBindingRevisionId.Should().Be(dimensions.AccountBindingRevisionId);
                line.Dimensions.IntercompanyTransactionId.Should().Be(dimensions.IntercompanyTransactionId);
                line.Dimensions.FundId.Should().Be(dimensions.FundId);
                line.Dimensions.BookId.Should().Be(dimensions.BookId);
            }
        }
    }
}

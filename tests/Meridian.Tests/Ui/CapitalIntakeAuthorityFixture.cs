using Meridian.Contracts.Banking;
using Meridian.Contracts.Ledger;
using Meridian.Ledger;
using Meridian.Ui.Shared.Services;
using NSubstitute;

namespace Meridian.Tests.Ui;

internal static class CapitalIntakeAuthorityFixture
{
    internal const string EntityId = "eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee";
    internal static readonly Guid FirstBankId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1");
    internal static readonly Guid SecondBankId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa2");

    internal static IPrivateCapitalStore Issuance(RunCapitalCallIssuanceDraftIntakeRequest request, bool postedIssuance = false)
    {
        var scope = new PrivateCapitalScopeDto(request.TenantId!, request.CompanyId!, request.EntityId!,
            request.FundProfileId, request.LedgerBookId!.Value, request.Currency);
        var terms = request.Commitments.Select(input => new PrivateCapitalCommitmentDto(input.CommitmentId,
            scope, 1, input.InvestorId, input.CapitalAccountId, input.TotalCommitment, input.CommitmentDate,
            input.CommitmentDate, input.InvestmentPeriodEndDate, true, PrivateCapitalCommitmentApprovalStatus.Approved,
            input.EvidenceLinks, "preparer", DateTimeOffset.UnixEpoch, "reviewer", DateTimeOffset.UnixEpoch)).ToArray();
        var total = terms.Sum(term => term.TotalCommitment);
        var lines = terms.Select(term => new PrivateCapitalCallLineDto($"{request.CallId}:{term.CommitmentId}",
            term.CommitmentId, 1, term.InvestorId, term.CapitalAccountId,
            request.AmountToCall * term.TotalCommitment / total, term.TotalCommitment,
            term.TotalCommitment - request.AmountToCall * term.TotalCommitment / total)).ToArray();
        if (postedIssuance)
            lines = lines.Select(line => line with { IssuedAmount = line.Amount, IssuanceJournalEntryId = Guid.NewGuid() }).ToArray();
        var call = new PrivateCapitalCallDto(request.CallId, scope, request.PeriodId!, request.NoticeDate,
            request.DueDate, request.AmountToCall, request.Purpose, "retained-test-plan", lines, "preparer", DateTimeOffset.UnixEpoch);
        call = call with { PlanHash = PrivateCapitalCallPlanHash.Compute(call) };
        var store = Substitute.For<IPrivateCapitalStore>();
        store.GetCallAsync(scope, request.CallId, Arg.Any<CancellationToken>()).Returns(call);
        store.ListCommitmentsAsync(scope, Arg.Any<CancellationToken>()).Returns(terms);
        store.ListPostingReceiptsAsync(scope, Arg.Any<CancellationToken>()).Returns(Array.Empty<PrivateCapitalPostingReceiptDto>());
        foreach (var term in terms)
            store.GetCommitmentAsync(scope, term.CommitmentId, 1, Arg.Any<CancellationToken>()).Returns(term);
        return store;
    }

    internal static IBankTransactionSource BankSource(decimal firstReceiptAmount, DateOnly receivedDate,
        bool isVoided = false)
    {
        var source = Substitute.For<IBankTransactionSource>();
        source.GetBankTransactionsAsync(Guid.Parse(EntityId), Arg.Any<CancellationToken>()).Returns(new[]
        {
            Bank(FirstBankId, firstReceiptAmount, receivedDate, isVoided),
            Bank(SecondBankId, 400_000m, receivedDate, false)
        });
        return source;
    }

    private static BankTransactionDto Bank(Guid id, decimal amount, DateOnly receivedDate, bool isVoided)
        => new(id, Guid.Parse(EntityId), "CapitalCallFunding", receivedDate, receivedDate, receivedDate,
            amount, "USD", "retained-bank-line", DateTimeOffset.UnixEpoch, isVoided);
}

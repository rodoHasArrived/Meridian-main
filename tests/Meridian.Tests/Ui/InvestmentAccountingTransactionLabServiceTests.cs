using FluentAssertions;
using Meridian.Contracts.Workstation;
using Meridian.Ui.Shared.Services;

namespace Meridian.Tests.Ui;

public sealed class InvestmentAccountingTransactionLabServiceTests
{
    [Fact]
    public void Preview_BuyTradeBuildsBalancedBooksBeforeBrokerJournal()
    {
        var service = new InvestmentAccountingTransactionLabService();

        var preview = service.Preview(new InvestmentAccountingTransactionLabRequestDto(
            InvestmentAccountingTransactionKindDto.Trade,
            "fund-acct-1",
            "msft",
            new DateOnly(2026, 5, 28),
            "usd",
            Amount: 0m,
            Quantity: 10m,
            Price: 25m,
            FeeAmount: 2m,
            Side: InvestmentAccountingTradeSideDto.Buy,
            SourceRunId: "run-1",
            SourceSessionId: "paper-session-1",
            EvidenceIds: ["order-evidence-1", "fee-evidence-1"]));

        preview.PreviewId.Should().Be("txn-lab:fund-acct-1:20260528:Trade:MSFT");
        preview.JournalPreview.IsBalanced.Should().BeTrue();
        preview.JournalPreview.RequiresOperatorApproval.Should().BeTrue();
        preview.JournalPreview.Lines.Should().Contain(line =>
            line.AccountName == "Investments" &&
            line.Symbol == "MSFT" &&
            line.Debit == 250m);
        preview.JournalPreview.Lines.Should().Contain(line =>
            line.AccountName == "Investment Fees" &&
            line.AccountType == "Expense" &&
            line.Debit == 2m);
        preview.LedgerImpact.HasValidationWarnings.Should().BeFalse();
        preview.TrialBalanceImpact.Should().Contain(line =>
            line.AccountName == "Cash" &&
            line.BalanceDelta == -252m);
        preview.ReconciliationExpectation.ExpectedBreakType.Should().Be("trade-to-ledger-match");
        preview.SourceRunId.Should().Be("run-1");
        preview.SourceSessionId.Should().Be("paper-session-1");
    }

    [Fact]
    public void Preview_BrokerReconciliationBuildsReconciliationReadySuspenseJournal()
    {
        var service = new InvestmentAccountingTransactionLabService();

        var preview = service.Preview(new InvestmentAccountingTransactionLabRequestDto(
            InvestmentAccountingTransactionKindDto.BrokerReconciliation,
            "fund-acct-1",
            "AAPL",
            new DateOnly(2026, 5, 28),
            "USD",
            Amount: 17.25m,
            BrokerStatementId: "statement-2026-05",
            ReconciliationCaseId: "case-1",
            EvidenceIds: ["statement-row-1"]));

        preview.JournalPreview.IsBalanced.Should().BeTrue();
        preview.JournalPreview.Lines.Should().Contain(line => line.AccountName == "Reconciliation Suspense" && line.Debit == 17.25m);
        preview.ReconciliationExpectation.ExpectedState.Should().Be("ReadyForReconciliation");
        preview.ReconciliationExpectation.ExpectedBreakType.Should().Be("broker-statement-break");
        preview.ReconciliationExpectation.BrokerStatementId.Should().Be("statement-2026-05");
        preview.ReconciliationExpectation.ReconciliationCaseId.Should().Be("case-1");
        preview.EvidenceIds.Should().ContainSingle("statement-row-1");
    }

    [Fact]
    public void Preview_BooksBeforeBrokerModeRequiresEvidenceSourceAndApprovalsBeforeBrokerStaging()
    {
        var service = new InvestmentAccountingTransactionLabService();

        var preview = service.Preview(new InvestmentAccountingTransactionLabRequestDto(
            InvestmentAccountingTransactionKindDto.Trade,
            "fund-acct-1",
            "msft",
            new DateOnly(2026, 5, 28),
            "usd",
            Amount: 0m,
            Quantity: 5m,
            Price: 100m,
            FeeAmount: 1m,
            Side: InvestmentAccountingTradeSideDto.Buy,
            SourceRunId: "run-1",
            SourceSessionId: "paper-session-1",
            EvidenceIds: ["order-ticket-1", "strategy-approval-1"],
            PreviewMode: InvestmentAccountingPreviewModeDto.BooksBeforeBroker));

        preview.BooksBeforeBroker.Should().NotBeNull();
        preview.BooksBeforeBroker!.IsBooksBeforeBrokerMode.Should().BeTrue();
        preview.BooksBeforeBroker.CanStageBrokerAction.Should().BeTrue();
        preview.BooksBeforeBroker.ExpectedBrokerAction.Should().Be("StageBuyOrder");
        preview.BooksBeforeBroker.RequiredApprovals.Should().Equal("operator-accounting-approval", "broker-routing-approval");
        preview.BooksBeforeBroker.Blockers.Should().BeEmpty();
        preview.BooksBeforeBroker.EvidenceIds.Should().Equal("order-ticket-1", "strategy-approval-1");
        preview.BooksBeforeBroker.BrokerInstructionSummary.Should().Contain("can be staged only after");
        preview.TrialBalanceImpact.Should().Contain(line => line.AccountName == "Cash" && line.BalanceDelta == -501m);
    }

    [Fact]
    public void Preview_BooksBeforeBrokerModeBlocksBrokerStagingWithoutEvidenceAndSource()
    {
        var service = new InvestmentAccountingTransactionLabService();

        var preview = service.Preview(new InvestmentAccountingTransactionLabRequestDto(
            InvestmentAccountingTransactionKindDto.Trade,
            "fund-acct-1",
            "msft",
            new DateOnly(2026, 5, 28),
            "usd",
            Amount: 0m,
            Quantity: 5m,
            Price: 100m,
            Side: InvestmentAccountingTradeSideDto.Sell,
            PreviewMode: InvestmentAccountingPreviewModeDto.BooksBeforeBroker));

        preview.BooksBeforeBroker.Should().NotBeNull();
        preview.BooksBeforeBroker!.CanStageBrokerAction.Should().BeFalse();
        preview.BooksBeforeBroker.ExpectedBrokerAction.Should().Be("StageSellOrder");
        preview.BooksBeforeBroker.Blockers.Should().Contain("evidence-required-before-broker-staging");
        preview.BooksBeforeBroker.Blockers.Should().Contain("source-run-or-session-required");
        preview.LedgerImpact.ValidationFlags.Should().Contain("evidence-required-before-posting");
    }

    [Fact]
    public void Preview_WithoutEvidenceFlagsApprovalWarning()
    {
        var service = new InvestmentAccountingTransactionLabService();

        var preview = service.Preview(new InvestmentAccountingTransactionLabRequestDto(
            InvestmentAccountingTransactionKindDto.Dividend,
            "fund-acct-1",
            "AAPL",
            new DateOnly(2026, 5, 28),
            "USD",
            Amount: 45m));

        preview.LedgerImpact.HasValidationWarnings.Should().BeTrue();
        preview.LedgerImpact.ValidationFlags.Should().ContainSingle("evidence-required-before-posting");
        preview.ReconciliationExpectation.ExpectedState.Should().Be("EvidenceRequired");
        preview.BooksBeforeBroker.Should().BeNull();
    }

    [Fact]
    public void Preview_RejectsNonPositiveAmounts()
    {
        var service = new InvestmentAccountingTransactionLabService();

        Action act = () => service.Preview(new InvestmentAccountingTransactionLabRequestDto(
            InvestmentAccountingTransactionKindDto.Fee,
            "fund-acct-1",
            "AAPL",
            new DateOnly(2026, 5, 28),
            "USD",
            Amount: 0m));

        act.Should().Throw<ArgumentException>()
            .WithMessage("Transaction Lab amount must be positive.");
    }
}

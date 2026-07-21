using Meridian.Contracts.Workstation;

namespace Meridian.Ui.Shared.Services;

public sealed class InvestmentAccountingTransactionLabService
{
    public InvestmentAccountingTransactionLabPreviewDto Preview(InvestmentAccountingTransactionLabRequestDto request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.FundAccountId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Symbol);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Currency);

        var amount = ResolveAmount(request);
        if (amount <= 0m)
        {
            throw new ArgumentException("Transaction Lab amount must be positive.");
        }

        var evidenceIds = NormalizeEvidenceIds(request.EvidenceIds);
        var lines = BuildJournalLines(request, amount);
        var totalDebits = lines.Sum(static line => line.Debit);
        var totalCredits = lines.Sum(static line => line.Credit);
        var isBalanced = totalDebits == totalCredits;
        var previewId = BuildPreviewId(request);
        var expectedAccountingProjection = new ExpectedJournalPreviewDto(
            previewId,
            BuildExpectedEventId(request),
            BuildDescription(request, amount),
            request.EventDate,
            isBalanced,
            RequiresOperatorApproval: true,
            BuildIdempotencyKey(request, amount),
            lines);

        return new InvestmentAccountingTransactionLabPreviewDto(
            previewId,
            request.Kind,
            request.FundAccountId.Trim(),
            request.Symbol.Trim().ToUpperInvariant(),
            request.EventDate,
            request.Currency.Trim().ToUpperInvariant(),
            expectedAccountingProjection,
            new LedgerImpactPreviewDto(
                DraftEntryCount: 1,
                NetDebitEffect: totalDebits,
                NetCreditEffect: totalCredits,
                NetBalanceDelta: totalDebits - totalCredits,
                HasValidationWarnings: !isBalanced || evidenceIds.Count == 0,
                ValidationFlags: BuildValidationFlags(isBalanced, evidenceIds)),
            BuildTrialBalanceImpact(lines),
            BuildReconciliationExpectation(request, evidenceIds),
            evidenceIds,
            NormalizeNullable(request.SourceRunId),
            NormalizeNullable(request.SourceSessionId),
            BuildBooksBeforeBrokerReadiness(request, evidenceIds, isBalanced));
    }

    private static decimal ResolveAmount(InvestmentAccountingTransactionLabRequestDto request)
    {
        if (request.Kind == InvestmentAccountingTransactionKindDto.Trade && request.Quantity > 0m && request.Price > 0m)
        {
            return request.Quantity * request.Price;
        }

        return request.Amount;
    }

    private static IReadOnlyList<ExpectedJournalPreviewLineDto> BuildJournalLines(
        InvestmentAccountingTransactionLabRequestDto request,
        decimal amount)
    {
        var symbol = request.Symbol.Trim().ToUpperInvariant();
        var lines = new List<ExpectedJournalPreviewLineDto>();
        switch (request.Kind)
        {
            case InvestmentAccountingTransactionKindDto.Trade:
                var side = request.Side ?? InvestmentAccountingTradeSideDto.Buy;
                if (side == InvestmentAccountingTradeSideDto.Buy)
                {
                    lines.Add(new ExpectedJournalPreviewLineDto("Investments", "Asset", symbol, amount, 0m));
                    lines.Add(new ExpectedJournalPreviewLineDto("Cash", "Asset", null, 0m, amount));
                }
                else
                {
                    lines.Add(new ExpectedJournalPreviewLineDto("Cash", "Asset", null, amount, 0m));
                    lines.Add(new ExpectedJournalPreviewLineDto("Investments", "Asset", symbol, 0m, amount));
                }

                AddFeeLines(lines, request.FeeAmount);
                break;
            case InvestmentAccountingTransactionKindDto.Dividend:
                lines.Add(new ExpectedJournalPreviewLineDto("Cash", "Asset", null, amount, 0m));
                lines.Add(new ExpectedJournalPreviewLineDto("Dividend Income", "Revenue", symbol, 0m, amount));
                break;
            case InvestmentAccountingTransactionKindDto.Fee:
                lines.Add(new ExpectedJournalPreviewLineDto("Investment Fees", "Expense", symbol, amount, 0m));
                lines.Add(new ExpectedJournalPreviewLineDto("Cash", "Asset", null, 0m, amount));
                break;
            case InvestmentAccountingTransactionKindDto.Accrual:
                lines.Add(new ExpectedJournalPreviewLineDto("Accrued Income Receivable", "Asset", symbol, amount, 0m));
                lines.Add(new ExpectedJournalPreviewLineDto("Investment Income", "Revenue", symbol, 0m, amount));
                break;
            case InvestmentAccountingTransactionKindDto.CorporateAction:
                lines.Add(new ExpectedJournalPreviewLineDto("Investments", "Asset", symbol, amount, 0m));
                lines.Add(new ExpectedJournalPreviewLineDto("Corporate Action Clearing", "Liability", symbol, 0m, amount));
                break;
            case InvestmentAccountingTransactionKindDto.BrokerReconciliation:
                lines.Add(new ExpectedJournalPreviewLineDto("Reconciliation Suspense", "Asset", symbol, amount, 0m));
                lines.Add(new ExpectedJournalPreviewLineDto("Broker Statement Variance", "Liability", symbol, 0m, amount));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(request), "Unsupported transaction kind.");
        }

        return lines;
    }

    private static void AddFeeLines(List<ExpectedJournalPreviewLineDto> lines, decimal feeAmount)
    {
        if (feeAmount <= 0m)
        {
            return;
        }

        lines.Add(new ExpectedJournalPreviewLineDto("Investment Fees", "Expense", null, feeAmount, 0m));
        lines.Add(new ExpectedJournalPreviewLineDto("Cash", "Asset", null, 0m, feeAmount));
    }

    private static IReadOnlyList<InvestmentAccountingTrialBalanceImpactDto> BuildTrialBalanceImpact(
        IReadOnlyList<ExpectedJournalPreviewLineDto> lines) =>
        lines
            .GroupBy(static line => (line.AccountName, line.AccountType, line.Symbol))
            .Select(static group =>
            {
                var debit = group.Sum(static line => line.Debit);
                var credit = group.Sum(static line => line.Credit);
                var delta = debit - credit;
                return new InvestmentAccountingTrialBalanceImpactDto(
                    group.Key.AccountName,
                    group.Key.AccountType,
                    group.Key.Symbol,
                    delta,
                    $"{group.Key.AccountName} changes by {delta} in the projected accounting effect; no journal has been posted.");
            })
            .ToArray();

    private static InvestmentAccountingReconciliationExpectationDto BuildReconciliationExpectation(
        InvestmentAccountingTransactionLabRequestDto request,
        IReadOnlyList<string> evidenceIds)
    {
        var expectedBreakType = request.Kind switch
        {
            InvestmentAccountingTransactionKindDto.BrokerReconciliation => "broker-statement-break",
            InvestmentAccountingTransactionKindDto.CorporateAction => "corporate-action-match",
            InvestmentAccountingTransactionKindDto.Trade => "trade-to-ledger-match",
            InvestmentAccountingTransactionKindDto.Dividend => "cash-income-match",
            InvestmentAccountingTransactionKindDto.Fee => "fee-cash-match",
            InvestmentAccountingTransactionKindDto.Accrual => "accrual-to-income-match",
            _ => "accounting-event-match"
        };

        return new InvestmentAccountingReconciliationExpectationDto(
            evidenceIds.Count == 0 ? "EvidenceRequired" : "ProjectedForReconciliation",
            expectedBreakType,
            "Expected accounting projection remains unposted; reconciliation should compare the projected accounting effect, retained evidence, and broker/custodian statement source before any posting candidate is created.",
            evidenceIds,
            NormalizeNullable(request.BrokerStatementId),
            NormalizeNullable(request.ReconciliationCaseId));
    }

    private static BooksBeforeBrokerReadinessDto? BuildBooksBeforeBrokerReadiness(
        InvestmentAccountingTransactionLabRequestDto request,
        IReadOnlyList<string> evidenceIds,
        bool isBalanced)
    {
        if (request.PreviewMode != InvestmentAccountingPreviewModeDto.BooksBeforeBroker)
        {
            return null;
        }

        var blockers = new List<string>();
        if (!isBalanced)
        {
            blockers.Add("projected-accounting-effect-not-balanced");
        }

        if (evidenceIds.Count == 0)
        {
            blockers.Add("evidence-required-before-broker-staging");
        }

        if (string.IsNullOrWhiteSpace(request.SourceRunId) && string.IsNullOrWhiteSpace(request.SourceSessionId))
        {
            blockers.Add("source-run-or-session-required");
        }

        if (request.Kind == InvestmentAccountingTransactionKindDto.Trade &&
            (request.Quantity <= 0m || request.Price <= 0m || request.Side is null))
        {
            blockers.Add("trade-order-terms-required");
        }

        var requiredApprovals = request.Kind == InvestmentAccountingTransactionKindDto.Trade
            ? new[] { "operator-accounting-approval", "broker-routing-approval" }
            : ["operator-accounting-approval"];

        return new BooksBeforeBrokerReadinessDto(
            IsBooksBeforeBrokerMode: true,
            CanStageBrokerAction: blockers.Count == 0,
            ExpectedBrokerAction: ResolveExpectedBrokerAction(request),
            BrokerInstructionSummary: BuildBrokerInstructionSummary(request, blockers),
            RequiredApprovals: requiredApprovals,
            Blockers: blockers,
            EvidenceIds: evidenceIds);
    }

    private static string ResolveExpectedBrokerAction(InvestmentAccountingTransactionLabRequestDto request)
        => request.Kind switch
        {
            InvestmentAccountingTransactionKindDto.Trade => request.Side switch
            {
                InvestmentAccountingTradeSideDto.Buy => "StageBuyOrder",
                InvestmentAccountingTradeSideDto.Sell => "StageSellOrder",
                _ => "TradeTermsRequired"
            },
            InvestmentAccountingTransactionKindDto.BrokerReconciliation => "NoBrokerOrder-ReconciliationCase",
            _ => "NoBrokerOrder-AccountingOnly"
        };

    private static string BuildBrokerInstructionSummary(
        InvestmentAccountingTransactionLabRequestDto request,
        IReadOnlyList<string> blockers)
    {
        if (blockers.Count > 0)
        {
            return $"Books Before Broker accounting projection is blocked by {string.Join(", ", blockers)}.";
        }

        if (request.Kind == InvestmentAccountingTransactionKindDto.Trade)
        {
            return $"{request.Side} {request.Quantity} {request.Symbol.Trim().ToUpperInvariant()} can be staged only after the expected accounting projection, evidence, and approvals are accepted.";
        }

        return "Projected accounting effect requires operator review; no broker order should be staged for this unposted preview.";
    }

    private static IReadOnlyList<string> BuildValidationFlags(bool isBalanced, IReadOnlyList<string> evidenceIds)
    {
        var flags = new List<string>();
        if (!isBalanced)
        {
            flags.Add("projected-accounting-effect-not-balanced");
        }

        if (evidenceIds.Count == 0)
        {
            flags.Add("evidence-required-before-posting");
        }

        return flags;
    }

    private static IReadOnlyList<string> NormalizeEvidenceIds(IReadOnlyList<string>? evidenceIds) =>
        evidenceIds?
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .Select(static id => id.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? [];

    private static string BuildPreviewId(InvestmentAccountingTransactionLabRequestDto request) =>
        $"txn-lab:{request.FundAccountId.Trim()}:{request.EventDate:yyyyMMdd}:{request.Kind}:{request.Symbol.Trim().ToUpperInvariant()}";

    private static string BuildExpectedEventId(InvestmentAccountingTransactionLabRequestDto request) =>
        $"expected:{request.Kind}:{request.Symbol.Trim().ToUpperInvariant()}:{request.EventDate:yyyyMMdd}";

    private static string BuildIdempotencyKey(InvestmentAccountingTransactionLabRequestDto request, decimal amount) =>
        $"{request.FundAccountId.Trim()}|{request.Kind}|{request.Symbol.Trim().ToUpperInvariant()}|{request.EventDate:yyyyMMdd}|{amount}|{request.FeeAmount}";

    private static string BuildDescription(InvestmentAccountingTransactionLabRequestDto request, decimal amount) =>
        $"Expected {request.Kind} accounting projection for {request.Symbol.Trim().ToUpperInvariant()} {amount} {request.Currency.Trim().ToUpperInvariant()} (unposted)";

    private static string? NormalizeNullable(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

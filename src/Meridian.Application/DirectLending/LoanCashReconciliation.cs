using System.Text.Json;
using Meridian.Contracts.DirectLending;
using Meridian.Contracts.Integrity;

namespace Meridian.Application.DirectLending;

/// <summary>Deterministic cash matching; each retained payment or allocation is consumed once.</summary>
public static partial class LoanCashReconciliation
{
    public static ReconcileLoanRequest Normalize(ReconcileLoanRequest? request)
    {
        request ??= new ReconcileLoanRequest(0m, 0);
        if (request.AmountTolerance < 0m || request.DateToleranceDays < 0)
            throw new DirectLendingCommandException(new DirectLendingCommandError(
                DirectLendingErrorCode.Validation, "Reconciliation tolerances must be non-negative."));
        return request;
    }

    public static string RequestHash(ReconcileLoanRequest request) => Sha256Digest.ComputeUtf8(
        FormattableString.Invariant($"loan-reconciliation/v2:{request.AmountTolerance:G29}:{request.DateToleranceDays}"));

    public static void EnsureReplay(ReconciliationRunDto run, ReconcileLoanRequest request)
    {
        var same = run.RequestHash is { } retained
            ? string.Equals(retained, RequestHash(request), StringComparison.Ordinal)
            : request.AmountTolerance == 0m && request.DateToleranceDays == 0;
        if (!same)
            throw new DirectLendingCommandException(new DirectLendingCommandError(
                DirectLendingErrorCode.ConcurrencyConflict, "The reconciliation command identity already belongs to a different tolerance request."));
    }

    public static IReadOnlyList<ReconciliationResultDto> Match(
        Guid runId, Guid loanId, DateOnly asOf, DateTimeOffset observedAt,
        IReadOnlyList<ProjectedCashFlowDto> projected, IReadOnlyList<CashTransactionDto> transactions,
        IReadOnlyList<PaymentAllocationDto> allocations, ReconcileLoanRequest request,
        IReadOnlyList<LoanEventLineageDto>? history = null, CurrencyCode? loanCurrency = null)
    {
        request = Normalize(request);
        var tolerance = JsonSerializer.Serialize(new { amountTolerance = request.AmountTolerance, dateToleranceDays = request.DateToleranceDays });
        var flows = projected.Where(flow => flow.LoanId == loanId && flow.DueDate <= asOf)
            .OrderBy(static flow => flow.DueDate).ThenBy(static flow => flow.FlowSequenceNumber)
            .ThenBy(static flow => flow.ProjectedCashFlowId).ToArray();
        var candidates = new List<Payment>();
        foreach (var cash in transactions.Where(cash => cash.LoanId == loanId && DirectLendingServiceSupport.IsIncomingReceipt(cash) && !cash.IsVoided && cash.SettlementDate <= asOf)
                     .OrderBy(static cash => cash.SettlementDate).ThenBy(static cash => cash.RecordedAt).ThenBy(static cash => cash.CashTransactionId))
        {
            var slices = allocations.Where(allocation => allocation.LoanId == loanId && allocation.CashTransactionId == cash.CashTransactionId)
                .OrderBy(static allocation => allocation.AllocationSequenceNumber).ThenBy(static allocation => allocation.AllocationId).ToArray();
            if (slices.Length == 0)
            {
                candidates.Add(new Payment(cash, cash.Amount, PaymentTarget(cash.TransactionType), null));
                continue;
            }

            // Invalid allocations cannot provide matching evidence. Retain the whole payment as an
            // unmatched observation instead of inflating available cash by its allocation total.
            if (slices.Any(static slice => slice.AllocatedAmount <= 0m) || slices.Sum(static slice => slice.AllocatedAmount) > cash.Amount)
            {
                candidates.Add(new Payment(cash, cash.Amount, "InvalidAllocation", null));
                continue;
            }
            candidates.AddRange(slices.Select(slice => new Payment(cash, slice.AllocatedAmount, AllocationTarget(slice.TargetType), slice.AllocationId)
            { IsUnappliedCash = string.Equals(slice.TargetType, "UnappliedCash", StringComparison.OrdinalIgnoreCase), SourceEventId = slice.SourceEventId }));
            var residual = cash.Amount - slices.Sum(static slice => slice.AllocatedAmount);
            if (residual != 0m)
                candidates.Add(new Payment(cash, residual, "Unallocated", null));
        }

        var heldCashFailure = ReassignHeldCash(candidates, loanId, asOf, observedAt, history ?? [], loanCurrency);
        var matches = Enumerable.Repeat(-1, flows.Length).ToArray();
        var owners = Enumerable.Repeat(-1, candidates.Count).ToList();
        // Lock exact matches first; then find a deterministic maximum matching of remaining
        // tolerant candidates, avoiding the ordinary greedy matcher losing an available pair.
        var exactOwners = new bool[candidates.Count];
        MatchPhase(exact: true);
        // A complete retained application total takes priority over a looser individual amount.
        MatchHeldApplications(exactAmount: true);
        exactOwners = owners.Select(static owner => owner >= 0).ToArray();
        MatchPhase(exact: false);
        MatchHeldApplications(exactAmount: false);
        var results = new List<ReconciliationResultDto>();
        for (var i = 0; i < flows.Length; i++)
        {
            var flow = flows[i];
            var payment = matches[i] < 0 ? null : candidates[matches[i]];
            results.Add(new ReconciliationResultDto(Guid.NewGuid(), runId, loanId, flow.ProjectedCashFlowId,
                payment?.CashTransactionId, payment is null ? "MissingCash" : "Matched", flow.Amount,
                payment?.Amount, payment is null ? flow.Amount : payment.Amount - flow.Amount, flow.DueDate,
                payment?.MatchDate, payment is null ? "ExpectedFlowVsCash" : payment.FundingSources.Count > 0 ? "RetainedUnappliedCashApplication" : "SingleUseAmountDateCurrency",
                tolerance, payment is null ? ["Due projected flow did not match eligible cash."] : payment.EvidenceNotes, observedAt)
            {
                MeasurementKind = "cash-flow",
                Currency = flow.Currency.ToString(),
                AsOfDate = asOf,
                PaymentAllocationId = payment?.AllocationId
            });
        }
        for (var i = 0; i < candidates.Count; i++)
        {
            if (owners[i] >= 0)
                continue;
            var payment = candidates[i];
            results.Add(new ReconciliationResultDto(Guid.NewGuid(), runId, loanId, null, payment.CashTransactionId,
                payment.TargetType is "RetainedExcess" ? "Matched" : "UnexpectedCash",
                payment.TargetType is "RetainedExcess" ? payment.Amount : null, payment.Amount,
                payment.TargetType is "RetainedExcess" ? 0m : payment.Amount, null, payment.MatchDate,
                payment.TargetType is "RetainedExcess" ? "RetainedExcessDisposition" : "UnconsumedCash", tolerance,
                payment.FundingSources.Count > 0 ? payment.EvidenceNotes
                    : payment.TargetType is "RetainedExcess" ? ["Cash has an explicit retained prepaid-interest, unapplied-cash or principal-prepayment disposition."]
                    : ["Eligible cash or allocation has no due projected counterpart."], observedAt)
            {
                MeasurementKind = payment.TargetType is "RetainedExcess" ? "cash-disposition" : "cash-flow",
                Currency = payment.Cash.Currency.ToString(),
                AsOfDate = asOf,
                PaymentAllocationId = payment.AllocationId
            });
        }
        if (heldCashFailure is not null)
            results.Add(new(Guid.NewGuid(), runId, loanId, null, null, "CashCoverageUnavailable", null, null, null,
                asOf, null, "RetainedUnappliedCashApplication", tolerance, [heldCashFailure], observedAt)
            { MeasurementKind = "cash-disposition", Currency = loanCurrency?.ToString(), AsOfDate = asOf });
        return results;

        void MatchHeldApplications(bool exactAmount)
        {
            for (var flowIndex = 0; flowIndex < flows.Length; flowIndex++)
            {
                if (matches[flowIndex] >= 0)
                    continue;
                var flow = flows[flowIndex];
                var eligible = Enumerable.Range(0, candidates.Count)
                    .Where(index => owners[index] < 0 && EligibleScope(flow, candidates[index])).ToArray();
                if (eligible.Length < 2)
                    continue;
                // Retained applications name a balance component, not an individual projected
                // obligation. Aggregate only a unique remaining scope; never choose a subset or
                // distribute these funds arbitrarily between competing due obligations.
                if (Enumerable.Range(0, flows.Length).Any(other => other != flowIndex && matches[other] < 0
                    && eligible.Any(index => EligibleScope(flows[other], candidates[index]))))
                    continue;
                var amount = eligible.Sum(index => candidates[index].Amount);
                if (Math.Abs(amount - flow.Amount) > (exactAmount ? 0m : request.AmountTolerance))
                    continue;
                var funding = eligible.SelectMany(index => candidates[index].FundingSources).ToArray();
                var allocationIds = funding.Select(static source => source.Payment.AllocationId).Distinct().ToArray();
                matches[flowIndex] = candidates.Count;
                candidates.Add(new(funding[0].Payment.Cash, amount, flow.FlowType,
                    allocationIds.Length == 1 ? allocationIds[0] : null)
                {
                    ApplicationDate = eligible.Max(index => candidates[index].MatchDate),
                    FundingSources = funding
                });
                owners.Add(flowIndex);
                foreach (var index in eligible)
                    owners[index] = flowIndex;
            }

            bool EligibleScope(ProjectedCashFlowDto flow, Payment candidate) => candidate.FundingSources.Count > 0
                && candidate.TargetType != "RetainedExcess" && candidate.Cash.Currency == flow.Currency
                && string.Equals(candidate.TargetType, flow.FlowType, StringComparison.OrdinalIgnoreCase)
                && Math.Abs(candidate.MatchDate.DayNumber - flow.DueDate.DayNumber) <= request.DateToleranceDays;
        }

        void MatchPhase(bool exact)
        {
            for (var flowIndex = 0; flowIndex < flows.Length; flowIndex++)
                if (matches[flowIndex] < 0)
                    TryMatch(flowIndex, new bool[candidates.Count], exact);
        }

        bool TryMatch(int flowIndex, bool[] visited, bool exact)
        {
            var flow = flows[flowIndex];
            var eligible = Enumerable.Range(0, candidates.Count).Where(index =>
            {
                var candidate = candidates[index];
                return !visited[index] && (exact || !exactOwners[index])
                    && candidate.Cash.Currency == flow.Currency
                    && (candidate.TargetType is null || string.Equals(candidate.TargetType, flow.FlowType, StringComparison.OrdinalIgnoreCase))
                    && Math.Abs(candidate.Amount - flow.Amount) <= (exact ? 0m : request.AmountTolerance)
                    && Math.Abs(candidate.MatchDate.DayNumber - flow.DueDate.DayNumber) <= (exact ? 0 : request.DateToleranceDays);
            }).OrderBy(index => Math.Abs(candidates[index].Amount - flow.Amount))
                .ThenBy(index => Math.Abs(candidates[index].MatchDate.DayNumber - flow.DueDate.DayNumber)).ThenBy(static index => index);
            foreach (var candidateIndex in eligible)
            {
                visited[candidateIndex] = true;
                if (owners[candidateIndex] < 0 || TryMatch(owners[candidateIndex], visited, exact))
                {
                    owners[candidateIndex] = flowIndex;
                    matches[flowIndex] = candidateIndex;
                    return true;
                }
            }
            return false;
        }
    }

    public static IReadOnlyList<ReconciliationExceptionDto> Exceptions(IEnumerable<ReconciliationResultDto> results, DateTimeOffset observedAt) =>
        results.Where(static result => result.MatchStatus != "Matched").Select(result => new ReconciliationExceptionDto(
            Guid.NewGuid(), result.ReconciliationResultId, result.MatchStatus,
            result.MatchStatus == "UnexpectedCash" ? "Medium" : "High", "Open", null, null, observedAt, null)).ToArray();

    private static string AllocationTarget(string target) => target.ToUpperInvariant() switch
    {
        "PRINCIPAL" or "PRINCIPALOUTSTANDING" => "Principal",
        "INTEREST" or "INTERESTACCRUED" => "Interest",
        "COMMITMENTFEE" or "COMMITMENTFEEACCRUED" => "CommitmentFee",
        "FEES" or "FEEBALANCE" => "Fees",
        "PENALTY" or "PENALTYACCRUED" => "Penalty",
        "PREPAIDINTEREST" or "UNAPPLIEDCASH" or "PRINCIPALPREPAYMENT" => "RetainedExcess",
        _ => "UnsupportedAllocation"
    };

    private static string? PaymentTarget(string transactionType) => transactionType.ToUpperInvariant() switch
    {
        "PRINCIPAL" or "PRINCIPALPAYMENT" => "Principal",
        "INTEREST" or "INTERESTPAYMENT" => "Interest",
        "MIXEDPAYMENT" => "Unallocated",
        _ => null
    };

    private sealed record Payment(CashTransactionDto Cash, decimal Amount, string? TargetType, Guid? AllocationId)
    {
        public bool IsUnappliedCash { get; init; }
        public Guid? SourceEventId { get; init; }
        public DateOnly? ApplicationDate { get; init; }
        public IReadOnlyList<Funding> FundingSources { get; init; } = [];
        public DateOnly MatchDate => ApplicationDate ?? Cash.SettlementDate;
        public Guid? CashTransactionId => FundingSources.Count == 0 ? Cash.CashTransactionId
            : FundingSources.Select(static source => source.Payment.Cash.CashTransactionId).Distinct().Take(2).Count() == 1 ? Cash.CashTransactionId : null;
        public IReadOnlyList<string> EvidenceNotes => FundingSources.Count == 0 ? ["Matched retained cash once."]
            : ["Retained unapplied-cash movements; funding attributed in retained event order, without another cash receipt.",
               .. FundingSources.Select(source => FormattableString.Invariant($"Movement {source.MovementEventId:D} on {source.MovementDate:yyyy-MM-dd}; cash {source.Payment.Cash.CashTransactionId:D}; allocation {source.Payment.AllocationId:D}; amount {source.Amount:G29}."))];
    }

    private sealed record Funding(Payment Payment, decimal Amount, Guid MovementEventId, DateOnly MovementDate);
}

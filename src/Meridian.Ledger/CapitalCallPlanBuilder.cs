using Meridian.Contracts.Ledger;
using static Meridian.Contracts.Ledger.LedgerCurrencyRounding;

namespace Meridian.Ledger;

/// <summary>Basis used to apportion a fund-level capital call across investor commitments.</summary>
public enum CapitalCallAllocationBasis
{
    /// <summary>Pro-rata over each commitment's remaining uncalled amount (default).</summary>
    ProRataByUncalled = 0,

    /// <summary>Pro-rata over each commitment's total commitment.</summary>
    ProRataByTotalCommitment = 1,
}

/// <summary>Fund-level capital-call request the wizard turns into per-LP call lines.</summary>
public sealed record CapitalCallPlanRequest
{
    public CapitalCallPlanRequest(
        string callId,
        string fundProfileId,
        decimal amountToCall,
        DateOnly noticeDate,
        DateOnly dueDate,
        IReadOnlyList<CommitmentRollForward> rollForwards,
        CapitalCallAllocationBasis allocationBasis = CapitalCallAllocationBasis.ProRataByUncalled,
        string? purpose = null)
    {
        if (string.IsNullOrWhiteSpace(callId))
            throw new ArgumentException("Call identifier must not be null or whitespace.", nameof(callId));
        if (string.IsNullOrWhiteSpace(fundProfileId))
            throw new ArgumentException("Fund profile identifier must not be null or whitespace.", nameof(fundProfileId));
        if (amountToCall <= 0m)
            throw new ArgumentOutOfRangeException(nameof(amountToCall), amountToCall, "Amount to call must be positive.");
        if (dueDate < noticeDate)
            throw new ArgumentException("Due date cannot precede the notice date.", nameof(dueDate));
        ArgumentNullException.ThrowIfNull(rollForwards);
        if (rollForwards.Count == 0)
            throw new ArgumentException("At least one commitment roll-forward is required.", nameof(rollForwards));

        var normalizedFundProfileId = fundProfileId.Trim();
        var seenCommitments = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? currency = null;
        foreach (var rollForward in rollForwards)
        {
            ArgumentNullException.ThrowIfNull(rollForward);
            if (!string.Equals(rollForward.Commitment.FundProfileId, normalizedFundProfileId, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    $"Commitment '{rollForward.CommitmentId}' belongs to fund '{rollForward.Commitment.FundProfileId}', not '{normalizedFundProfileId}'.",
                    nameof(rollForwards));
            }

            if (!seenCommitments.Add(rollForward.CommitmentId))
                throw new ArgumentException($"Commitment '{rollForward.CommitmentId}' is duplicated.", nameof(rollForwards));

            currency ??= rollForward.Currency;
            if (!string.Equals(currency, rollForward.Currency, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    "All commitments in one capital call must share a single currency; cross-currency calls need an FX policy first.",
                    nameof(rollForwards));
            }
        }

        CallId = callId.Trim();
        FundProfileId = normalizedFundProfileId;
        AmountToCall = amountToCall;
        NoticeDate = noticeDate;
        DueDate = dueDate;
        RollForwards = rollForwards;
        AllocationBasis = allocationBasis;
        Purpose = string.IsNullOrWhiteSpace(purpose) ? null : purpose.Trim();
        Currency = currency!;
    }

    public string CallId { get; }

    public string FundProfileId { get; }

    public decimal AmountToCall { get; }

    public DateOnly NoticeDate { get; }

    public DateOnly DueDate { get; }

    public IReadOnlyList<CommitmentRollForward> RollForwards { get; }

    public CapitalCallAllocationBasis AllocationBasis { get; }

    public string? Purpose { get; }

    public string Currency { get; }
}

/// <summary>One investor's share of a planned fund-level capital call.</summary>
public sealed record CapitalCallPlanLine(
    string InstallmentId,
    InvestorCommitment Commitment,
    decimal CallAmount,
    decimal SharePercent,
    decimal UncalledBeforeCall,
    decimal UncalledAfterCall)
{
    /// <summary>Materializes the drawdown installment this plan line notices.</summary>
    public DrawdownInstallment BuildInstallment(DateOnly noticeDate, DateOnly dueDate, int sequence)
        => new(
            InstallmentId,
            Commitment.CommitmentId,
            sequence,
            noticeDate,
            dueDate,
            callPercent: null,
            callAmount: CallAmount,
            DrawdownInstallmentStatus.Noticed);
}

/// <summary>Complete per-LP allocation of a fund-level capital call.</summary>
public sealed record CapitalCallPlan(
    CapitalCallPlanRequest Request,
    IReadOnlyList<CapitalCallPlanLine> Lines,
    decimal AllocatedAmount,
    IReadOnlyList<AccountingConfigurationValidationIssueDto> ValidationIssues)
{
    public bool IsExecutable =>
        AllocatedAmount == Request.AmountToCall
        && Lines.Count > 0
        && !ValidationIssues.Any(static issue =>
            issue.Severity == AccountingConfigurationValidationSeverityDto.Critical);
}

/// <summary>
/// Turns a fund-level capital-call amount into per-LP call lines over the commitment register:
/// pro-rata over the chosen basis, capped at each commitment's uncalled amount, with rounding
/// residue redistributed deterministically so the lines sum exactly to the requested amount.
/// </summary>
public static class CapitalCallPlanBuilder
{
    public static CapitalCallPlan Build(CapitalCallPlanRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var issues = new List<AccountingConfigurationValidationIssueDto>();

        // Fail closed on any roll-forward that already carries a broken invariant (e.g. an over-call
        // or misrouted expiry surfaced a Critical issue upstream): planning a call from it would let
        // notices and postings flow from an invalid commitment state.
        var invalid = request.RollForwards.Where(static rollForward => !rollForward.InvariantHolds).ToArray();
        foreach (var breach in invalid)
        {
            issues.Add(new AccountingConfigurationValidationIssueDto(
                "private-capital.capital-call-invariant-breach",
                AccountingConfigurationValidationSeverityDto.Critical,
                $"Commitment '{breach.CommitmentId}' roll-forward is invalid (invariant does not hold); it cannot back a capital call.",
                breach.CommitmentId,
                "Reconcile the commitment roll-forward and clear its critical issues before calling capital."));
        }

        var callable = request.RollForwards
            .Where(static rollForward => rollForward.InvariantHolds && rollForward.Commitment.IsCallable && rollForward.Uncalled > 0m)
            .OrderBy(static rollForward => rollForward.CommitmentId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var skipped in request.RollForwards.Except(callable).Except(invalid))
        {
            issues.Add(new AccountingConfigurationValidationIssueDto(
                "private-capital.capital-call-commitment-skipped",
                AccountingConfigurationValidationSeverityDto.Warning,
                $"Commitment '{skipped.CommitmentId}' was skipped: status {skipped.Commitment.Status} with uncalled {skipped.Uncalled}.",
                skipped.CommitmentId,
                "Confirm the commitment is callable or exclude it from the call."));
        }

        var totalCapacity = callable.Sum(static rollForward => rollForward.Uncalled);
        if (totalCapacity + LedgerToleranceConstants.Balance < request.AmountToCall)
        {
            issues.Add(new AccountingConfigurationValidationIssueDto(
                "private-capital.capital-call-exceeds-uncalled",
                AccountingConfigurationValidationSeverityDto.Critical,
                $"Requested call {request.AmountToCall} exceeds total uncalled capacity {totalCapacity}.",
                request.CallId,
                "Reduce the call amount or record additional commitments before issuing notices."));
            return new CapitalCallPlan(request, [], 0m, issues);
        }

        var allocations = Allocate(request, callable);
        var lines = new List<CapitalCallPlanLine>(allocations.Count);
        foreach (var (rollForward, amount) in allocations)
        {
            if (amount == 0m)
                continue;

            lines.Add(new CapitalCallPlanLine(
                FormattableString.Invariant($"{request.CallId}:{rollForward.CommitmentId}"),
                rollForward.Commitment,
                amount,
                request.AmountToCall == 0m ? 0m : amount / request.AmountToCall,
                rollForward.Uncalled,
                rollForward.Uncalled - amount));
        }

        var allocated = lines.Sum(static line => line.CallAmount);
        return new CapitalCallPlan(request, lines, allocated, issues);
    }

    private static IReadOnlyList<(CommitmentRollForward RollForward, decimal Amount)> Allocate(
        CapitalCallPlanRequest request,
        IReadOnlyList<CommitmentRollForward> callable)
    {
        var amounts = callable.ToDictionary(
            static rollForward => rollForward.CommitmentId,
            static _ => 0m,
            StringComparer.OrdinalIgnoreCase);
        var remaining = request.AmountToCall;

        // Waterline allocation: apportion the remainder pro-rata over the basis weights of
        // commitments that still have capacity, capping each at its uncalled amount. Capped
        // commitments drop out and the residual re-apportions over the rest, so the loop
        // terminates once every dollar is placed or capacity is exhausted (which the caller
        // has already ruled out).
        while (remaining > 0m)
        {
            var open = callable
                .Where(rollForward => rollForward.Uncalled - amounts[rollForward.CommitmentId] > 0m)
                .ToArray();
            if (open.Length == 0)
                break;

            var weightTotal = open.Sum(rollForward => BasisWeight(request.AllocationBasis, rollForward));
            if (weightTotal <= 0m)
                break;

            var placedThisRound = 0m;
            for (var index = 0; index < open.Length; index++)
            {
                var rollForward = open[index];
                var capacity = rollForward.Uncalled - amounts[rollForward.CommitmentId];
                var share = index == open.Length - 1
                    ? remaining - placedThisRound
                    : RoundCurrency(remaining * BasisWeight(request.AllocationBasis, rollForward) / weightTotal);
                var placed = Math.Min(Math.Max(share, 0m), capacity);
                amounts[rollForward.CommitmentId] += placed;
                placedThisRound += placed;
            }

            if (placedThisRound == 0m)
                break;

            remaining -= placedThisRound;
        }

        return callable.Select(rollForward => (rollForward, amounts[rollForward.CommitmentId])).ToArray();
    }

    private static decimal BasisWeight(CapitalCallAllocationBasis basis, CommitmentRollForward rollForward)
        => basis == CapitalCallAllocationBasis.ProRataByTotalCommitment
            ? rollForward.TotalCommitment
            : rollForward.Uncalled;
}

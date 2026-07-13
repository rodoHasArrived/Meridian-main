using Meridian.Contracts.SecurityMaster;
using Meridian.Ledger;

namespace Meridian.Application.SecurityMaster.CashFlow;

/// <summary>
/// Wires a display cash flow projection to the ledger. Converts the per-period interest amounts of a
/// <see cref="StructuredCashFlowProjectionDto"/> into balanced coupon-accrual journal lines via
/// <see cref="FixedIncomeAmortizationProjector"/> — the accrual/amortization projector that had no
/// production caller before this seam existed. Enforces the freshness and scenario gates so stale or
/// rate-shocked what-if projections cannot reach the general ledger.
/// </summary>
public interface IStructuredCashFlowLedgerBridge
{
    /// <summary>
    /// Projects balanced coupon-accrual postings from <paramref name="projection"/>.
    /// </summary>
    /// <param name="projection">The cash flow projection to post from.</param>
    /// <param name="symbol">Instrument symbol used to scope the per-security ledger accounts.</param>
    /// <param name="financialAccountId">Optional financial account scoping the ledger accounts.</param>
    /// <param name="through">
    /// Optional inclusive upper bound on coupon period dates; entries dated after it are skipped.
    /// </param>
    StructuredCashFlowLedgerPostingResult BuildCouponAccrualPostings(
        StructuredCashFlowProjectionDto projection,
        string symbol,
        string? financialAccountId = null,
        DateOnly? through = null);
}

/// <inheritdoc />
public sealed class StructuredCashFlowLedgerBridge : IStructuredCashFlowLedgerBridge
{
    /// <inheritdoc />
    public StructuredCashFlowLedgerPostingResult BuildCouponAccrualPostings(
        StructuredCashFlowProjectionDto projection,
        string symbol,
        string? financialAccountId = null,
        DateOnly? through = null)
    {
        ArgumentNullException.ThrowIfNull(projection);
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);

        var normalizedSymbol = symbol.Trim().ToUpperInvariant();
        var normalizedFinancialAccountId = string.IsNullOrWhiteSpace(financialAccountId)
            ? null
            : financialAccountId.Trim();

        // Freshness and scenario gates are shared with the posting bridge so neither path can route a
        // stale source or a rate-shocked what-if projection to the general ledger.
        if (StructuredCashFlowLedgerGate.EvaluateBlockReason(projection) is { } blockReason)
        {
            return StructuredCashFlowLedgerPostingResult.Blocked(projection.SecurityId, blockReason);
        }

        var carryingValueAccount = LedgerAccounts.Securities(normalizedSymbol, normalizedFinancialAccountId);
        var postings = new List<StructuredCashFlowLedgerPosting>();
        foreach (var entry in projection.Schedule)
        {
            if (entry.InterestAmount <= 0m)
            {
                continue;
            }

            if (through is DateOnly bound && DateOnly.FromDateTime(entry.PeriodDate.UtcDateTime) > bound)
            {
                continue;
            }

            var input = new FixedIncomeAmortizationInput(
                normalizedSymbol,
                carryingValueAccount,
                CouponAccrual: entry.InterestAmount,
                DiscountAccretion: 0m,
                PremiumAmortization: 0m,
                FinancialAccountId: normalizedFinancialAccountId,
                Description: $"Coupon accrual for {normalizedSymbol} period {entry.PeriodDate:yyyy-MM-dd}");

            var projected = FixedIncomeAmortizationProjector.Project(input);
            postings.Add(new StructuredCashFlowLedgerPosting(
                entry.PeriodDate,
                projected.Description,
                projected.TotalDebits,
                projected.TotalCredits,
                projected.IsBalanced,
                projected.Lines.Select(static line => new StructuredCashFlowLedgerLine(
                    line.account.Name,
                    line.account.AccountType.ToString(),
                    line.account.Symbol,
                    line.account.FinancialAccountId,
                    line.debit,
                    line.credit)).ToArray()));
        }

        if (postings.Count == 0)
        {
            return StructuredCashFlowLedgerPostingResult.Blocked(
                projection.SecurityId,
                "Projection has no accruable coupon periods in the requested window.");
        }

        return new StructuredCashFlowLedgerPostingResult(projection.SecurityId, true, null, postings);
    }
}

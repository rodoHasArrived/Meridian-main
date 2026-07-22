using System.Globalization;
using FSharpLedger = Meridian.FSharp.Ledger;

namespace Meridian.Ledger;

/// <summary>
/// Result of drafting a performance-fee accrual: the balanced draft (null when no fee is due) plus
/// the crystallization outputs a caller persists to carry the high-water mark forward.
/// </summary>
public sealed record PerformanceFeeAccrualDraftResult(
    AutomatedJournalDraft? Draft,
    decimal GrossProfit,
    decimal FeeBase,
    decimal PerformanceFee,
    decimal CrystallizedFee,
    decimal NewHighWaterMark)
{
    /// <summary>True when a fee accrued and a posting draft was produced.</summary>
    public bool HasFee => Draft is not null && PerformanceFee > 0m;
}

/// <summary>
/// Builds governed <see cref="AutomatedJournalDraft"/>s for fund fee, expense, and distribution
/// economics so the fund-economics kernels post real ledger entries instead of living only in tests.
/// The fee/expense amounts come from the F# <c>FundEconomics</c> kernels (via
/// <see cref="FSharpLedger.LedgerInterop"/>); distribution splits come from
/// <see cref="EuropeanDistributionWaterfall"/>. Every draft is balanced (debits equal credits) and
/// flows through the standard <see cref="AutomatedJournalApproval"/> submit → approve → post
/// lifecycle before it reaches the governed append boundary.
/// </summary>
public static class FundEconomicsJournalFactory
{
    /// <summary>
    /// Drafts a day-weighted management-fee accrual: debit management fee expense, credit management
    /// fee payable. The amount is computed by the F# management-fee kernel.
    /// </summary>
    public static AutomatedJournalDraft BuildManagementFeeAccrualDraft(
        string fundId,
        decimal netAssets,
        decimal annualRate,
        int accrualDays,
        int yearBasisDays,
        DateOnly effectiveDate,
        DateTimeOffset occurredAtUtc,
        string periodId,
        IReadOnlyList<JournalEvidenceReference>? evidenceReferences = null)
    {
        var normalizedFundId = RequireId(fundId, nameof(fundId));
        var normalizedPeriodId = RequireId(periodId, nameof(periodId));
        var fee = FSharpLedger.LedgerInterop.ManagementFeeAccrual(netAssets, annualRate, accrualDays, yearBasisDays);
        if (fee <= 0m)
            throw new InvalidOperationException("Management fee accrual produced a non-positive amount; nothing to post.");

        var idempotencyKey = FormattableString.Invariant(
            $"management-fee:{normalizedFundId}:{normalizedPeriodId}:{effectiveDate:yyyyMMdd}");
        var description = FormattableString.Invariant(
            $"Management fee accrual for {normalizedFundId} period {normalizedPeriodId} ({FormatAmount(fee)} on {accrualDays}/{yearBasisDays} days)");

        return Build(
            normalizedFundId,
            AutomatedJournalEventKind.ManagementFeeAccrued,
            "ManagementFee",
            FormattableString.Invariant($"fund-event:{normalizedFundId}:management-fee:{normalizedPeriodId}"),
            normalizedPeriodId,
            fee,
            effectiveDate,
            occurredAtUtc,
            idempotencyKey,
            description,
            lines:
            [
                (LedgerAccounts.ManagementFeeExpenseFor(normalizedFundId), fee, 0m),
                (LedgerAccounts.ManagementFeePayableFor(normalizedFundId), 0m, fee),
            ],
            evidenceReferences);
    }

    /// <summary>
    /// Drafts a performance-fee accrual against the high-water mark, net of a hurdle: debit
    /// performance fee expense, credit performance fee payable. Returns the crystallization outputs
    /// (including the advanced high-water mark) even when no fee is due, so the caller can persist
    /// the mark. The draft is null when the fee is zero.
    /// </summary>
    public static PerformanceFeeAccrualDraftResult BuildPerformanceFeeAccrualDraft(
        string fundId,
        decimal endingNavBeforeFee,
        decimal priorHighWaterMark,
        decimal hurdleAmount,
        decimal performanceFeeRate,
        bool crystallize,
        DateOnly effectiveDate,
        DateTimeOffset occurredAtUtc,
        string periodId,
        IReadOnlyList<JournalEvidenceReference>? evidenceReferences = null)
    {
        var normalizedFundId = RequireId(fundId, nameof(fundId));
        var normalizedPeriodId = RequireId(periodId, nameof(periodId));
        var accrual = FSharpLedger.LedgerInterop.PerformanceFeeAccrual(
            endingNavBeforeFee, priorHighWaterMark, hurdleAmount, performanceFeeRate, crystallize);

        AutomatedJournalDraft? draft = null;
        if (accrual.PerformanceFee > 0m)
        {
            var idempotencyKey = FormattableString.Invariant(
                $"performance-fee:{normalizedFundId}:{normalizedPeriodId}:{effectiveDate:yyyyMMdd}");
            var description = FormattableString.Invariant(
                $"Performance fee accrual for {normalizedFundId} period {normalizedPeriodId} ({FormatAmount(accrual.PerformanceFee)} on {FormatAmount(accrual.FeeBase)} above high-water mark)");

            draft = Build(
                normalizedFundId,
                AutomatedJournalEventKind.PerformanceFeeAccrued,
                "PerformanceFee",
                FormattableString.Invariant($"fund-event:{normalizedFundId}:performance-fee:{normalizedPeriodId}"),
                normalizedPeriodId,
                accrual.PerformanceFee,
                effectiveDate,
                occurredAtUtc,
                idempotencyKey,
                description,
                lines:
                [
                    (LedgerAccounts.PerformanceFeeExpenseFor(normalizedFundId), accrual.PerformanceFee, 0m),
                    (LedgerAccounts.PerformanceFeePayableFor(normalizedFundId), 0m, accrual.PerformanceFee),
                ],
                evidenceReferences);
        }

        return new PerformanceFeeAccrualDraftResult(
            draft,
            accrual.GrossProfit,
            accrual.FeeBase,
            accrual.PerformanceFee,
            accrual.CrystallizedFee,
            accrual.NewHighWaterMark);
    }

    /// <summary>
    /// Drafts a straight-line fund-expense accrual: debit fund operating expense, credit accrued
    /// expenses payable. The amount is computed by the F# expense-accrual kernel.
    /// </summary>
    public static AutomatedJournalDraft BuildExpenseAccrualDraft(
        string fundId,
        string expenseId,
        decimal annualAmount,
        int accrualDays,
        int yearBasisDays,
        DateOnly effectiveDate,
        DateTimeOffset occurredAtUtc,
        string periodId,
        IReadOnlyList<JournalEvidenceReference>? evidenceReferences = null)
    {
        var normalizedFundId = RequireId(fundId, nameof(fundId));
        var normalizedExpenseId = RequireId(expenseId, nameof(expenseId));
        var normalizedPeriodId = RequireId(periodId, nameof(periodId));
        var accrued = FSharpLedger.LedgerInterop.ExpenseAccrual(annualAmount, accrualDays, yearBasisDays);
        if (accrued <= 0m)
            throw new InvalidOperationException("Expense accrual produced a non-positive amount; nothing to post.");

        var idempotencyKey = FormattableString.Invariant(
            $"expense-accrual:{normalizedFundId}:{normalizedExpenseId}:{normalizedPeriodId}:{effectiveDate:yyyyMMdd}");
        var description = FormattableString.Invariant(
            $"Expense accrual {normalizedExpenseId} for {normalizedFundId} period {normalizedPeriodId} ({FormatAmount(accrued)} on {accrualDays}/{yearBasisDays} days)");

        return Build(
            normalizedFundId,
            AutomatedJournalEventKind.CorporateActionExpense,
            "ExpenseAccrual",
            FormattableString.Invariant($"fund-event:{normalizedFundId}:expense-accrual:{normalizedExpenseId}:{normalizedPeriodId}"),
            normalizedPeriodId,
            accrued,
            effectiveDate,
            occurredAtUtc,
            idempotencyKey,
            description,
            lines:
            [
                (LedgerAccounts.FundOperatingExpenseFor(normalizedFundId), accrued, 0m),
                (LedgerAccounts.AccruedExpensesPayableFor(normalizedFundId), 0m, accrued),
            ],
            evidenceReferences,
            expenseId: normalizedExpenseId);
    }

    /// <summary>
    /// Drafts a distribution declaration from a European-waterfall result: debit the LP capital
    /// account (return of capital plus preferred and carried-interest catch-up) and the GP carried
    /// interest allocation, credit the fund distribution payable. Balances by construction because
    /// the payable equals the waterfall's total distributed amount.
    /// </summary>
    public static AutomatedJournalDraft BuildDistributionWaterfallDraft(
        string fundId,
        string investorId,
        EuropeanWaterfallResult waterfall,
        DateOnly effectiveDate,
        DateTimeOffset occurredAtUtc,
        string periodId,
        string distributionId,
        IReadOnlyList<JournalEvidenceReference>? evidenceReferences = null)
    {
        ArgumentNullException.ThrowIfNull(waterfall);
        var normalizedFundId = RequireId(fundId, nameof(fundId));
        var normalizedInvestorId = RequireId(investorId, nameof(investorId));
        var normalizedPeriodId = RequireId(periodId, nameof(periodId));
        var normalizedDistributionId = RequireId(distributionId, nameof(distributionId));
        if (waterfall.Distributed <= 0m)
            throw new InvalidOperationException("Distribution waterfall produced a non-positive amount; nothing to post.");

        var lines = new List<(LedgerAccount account, decimal debit, decimal credit)>();
        if (waterfall.LpTotal > 0m)
            lines.Add((LedgerAccounts.InvestorCapitalFor(normalizedInvestorId), waterfall.LpTotal, 0m));
        if (waterfall.GpTotal > 0m)
            lines.Add((LedgerAccounts.CarriedInterestAllocationFor(normalizedInvestorId), waterfall.GpTotal, 0m));
        lines.Add((LedgerAccounts.FundDistributionPayableFor(normalizedInvestorId), 0m, waterfall.Distributed));

        var idempotencyKey = FormattableString.Invariant(
            $"distribution:{normalizedFundId}:{normalizedInvestorId}:{normalizedDistributionId}");
        var description = FormattableString.Invariant(
            $"Distribution {normalizedDistributionId} for {normalizedInvestorId} in {normalizedFundId} (LP {FormatAmount(waterfall.LpTotal)}, GP {FormatAmount(waterfall.GpTotal)})");

        return Build(
            normalizedFundId,
            AutomatedJournalEventKind.CorporateActionExpense,
            "Distribution",
            FormattableString.Invariant($"fund-event:{normalizedFundId}:distribution:{normalizedDistributionId}"),
            normalizedPeriodId,
            waterfall.Distributed,
            effectiveDate,
            occurredAtUtc,
            idempotencyKey,
            description,
            lines,
            evidenceReferences,
            investorId: normalizedInvestorId,
            distributionId: normalizedDistributionId);
    }

    private static AutomatedJournalDraft Build(
        string fundId,
        AutomatedJournalEventKind kind,
        string fundEventType,
        string fundEventId,
        string periodId,
        decimal amount,
        DateOnly effectiveDate,
        DateTimeOffset occurredAtUtc,
        string idempotencyKey,
        string description,
        IReadOnlyList<(LedgerAccount account, decimal debit, decimal credit)> lines,
        IReadOnlyList<JournalEvidenceReference>? evidenceReferences,
        string? investorId = null,
        string? expenseId = null,
        string? distributionId = null)
    {
        var totalDebits = lines.Sum(static line => line.debit);
        var totalCredits = lines.Sum(static line => line.credit);
        if (lines.Count == 0 || totalDebits != totalCredits)
            throw new InvalidOperationException($"Fund-economics {fundEventType} draft produced unbalanced journal lines.");

        var dimensions = new LedgerLineDimensionSet(FundId: fundId, InvestorId: investorId);
        var draftLines = lines
            .Select(line => (line.account, line.debit, line.credit, (LedgerLineDimensionSet?)dimensions))
            .ToArray();

        var journalEvent = new AutomatedJournalEvent(
            kind,
            fundId,
            amount,
            occurredAtUtc,
            Description: description,
            EffectiveDate: effectiveDate,
            IdempotencyKey: idempotencyKey,
            EvidenceReferences: evidenceReferences);

        var tags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["fundId"] = fundId,
            ["periodId"] = periodId,
        };
        AddTag(tags, "expenseId", expenseId);
        AddTag(tags, "distributionId", distributionId);

        var metadata = new JournalEntryMetadata(
            ActivityType: FormattableString.Invariant($"fund-economics.{fundEventType.ToLowerInvariant()}"),
            EffectiveDate: effectiveDate,
            IdempotencyKey: idempotencyKey,
            FundEventId: fundEventId,
            FundEventType: fundEventType,
            InvestorId: investorId,
            Tags: tags,
            EvidenceReferences: evidenceReferences);

        return new AutomatedJournalDraft(journalEvent, description, draftLines, metadata);
    }

    private static void AddTag(IDictionary<string, string> tags, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            tags[key] = value.Trim();
    }

    private static string FormatAmount(decimal amount)
        => amount.ToString("0.00", CultureInfo.InvariantCulture);

    private static string RequireId(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Identifier must not be null or whitespace.", parameterName);

        return value.Trim();
    }
}

using Meridian.Contracts.Ledger;
using static Meridian.Contracts.Ledger.LedgerCurrencyRounding;

namespace Meridian.Ledger;

/// <summary>Lifecycle state of one investor's commitment to a closed-end vehicle.</summary>
public enum CommitmentStatus
{
    /// <summary>Within the investment period and callable.</summary>
    Active = 0,

    /// <summary>Investment period ended; residual uncalled retained and still callable for follow-ons/expenses.</summary>
    InvestmentPeriodClosed = 1,

    /// <summary>Uncalled commitment fully drawn.</summary>
    FullyCalled = 2,

    /// <summary>One or more capital-call installments are in default.</summary>
    Defaulted = 3,

    /// <summary>Residual uncalled commitment released by a governed expiry action.</summary>
    Expired = 4,

    /// <summary>Commitment closed (transferred out or wound down).</summary>
    Closed = 5,
}

/// <summary>Lifecycle state of one planned drawdown installment against a commitment.</summary>
public enum DrawdownInstallmentStatus
{
    /// <summary>Planned only; no notice issued.</summary>
    Scheduled = 0,

    /// <summary>Drawdown notice issued to the LP.</summary>
    Noticed = 1,

    /// <summary>Capital-call journal posted (receivable raised).</summary>
    Called = 2,

    /// <summary>Cash received in full.</summary>
    Funded = 3,

    /// <summary>Cash received for part of the called amount.</summary>
    PartiallyFunded = 4,

    /// <summary>Due date plus grace passed with the call unfunded.</summary>
    Defaulted = 5,

    /// <summary>Default cured (funded late, including any default interest).</summary>
    Cured = 6,

    /// <summary>Installment waived by the manager.</summary>
    Waived = 7,

    /// <summary>Installment cancelled before notice.</summary>
    Cancelled = 8,
}

/// <summary>Whether a distribution restores uncalled commitment under the LPA's recycling clause.</summary>
public enum DistributionRecallability
{
    /// <summary>Permanent distribution of capital or profit; never restores uncalled commitment.</summary>
    NonRecallable = 0,

    /// <summary>Return of capital that restores uncalled commitment, subject to the recall cap.</summary>
    RecallableReturnOfCapital = 1,
}

/// <summary>Day-count convention used to accrue default interest on an unfunded capital call.</summary>
public enum DefaultInterestConvention
{
    /// <summary>Actual elapsed days over a fixed 365-day year.</summary>
    Actual365Fixed = 0,

    /// <summary>Actual elapsed days over a 360-day year.</summary>
    Actual360 = 1,

    /// <summary>US 30/360 day count over a 360-day year.</summary>
    Thirty360 = 2,
}

/// <summary>
/// One investor's total commitment to a closed-end fund, with the policy terms the
/// roll-forward and default-interest calculators need.
/// </summary>
public sealed record InvestorCommitment
{
    public InvestorCommitment(
        string commitmentId,
        string fundProfileId,
        Guid? ledgerBookId,
        string capitalAccountId,
        string investorId,
        string currency,
        decimal totalCommitment,
        DateOnly commitmentDate,
        DateOnly? investmentPeriodEndDate,
        CommitmentStatus status,
        decimal recallCapPercent = 1m,
        decimal defaultInterestRateAnnual = 0m,
        DefaultInterestConvention defaultInterestConvention = DefaultInterestConvention.Actual365Fixed,
        int defaultGraceDays = 0)
    {
        if (string.IsNullOrWhiteSpace(commitmentId))
            throw new ArgumentException("Commitment identifier must not be null or whitespace.", nameof(commitmentId));
        if (string.IsNullOrWhiteSpace(fundProfileId))
            throw new ArgumentException("Fund profile identifier must not be null or whitespace.", nameof(fundProfileId));
        if (string.IsNullOrWhiteSpace(capitalAccountId))
            throw new ArgumentException("Capital account identifier must not be null or whitespace.", nameof(capitalAccountId));
        if (string.IsNullOrWhiteSpace(investorId))
            throw new ArgumentException("Investor identifier must not be null or whitespace.", nameof(investorId));
        if (totalCommitment <= 0m)
            throw new ArgumentOutOfRangeException(nameof(totalCommitment), totalCommitment, "Total commitment must be positive.");
        if (investmentPeriodEndDate.HasValue && investmentPeriodEndDate.Value < commitmentDate)
            throw new ArgumentException("Investment period end date cannot precede the commitment date.", nameof(investmentPeriodEndDate));
        if (recallCapPercent < 0m || recallCapPercent > 1m)
            throw new ArgumentOutOfRangeException(nameof(recallCapPercent), recallCapPercent, "Recall cap percent must be between 0 and 1.");
        if (defaultInterestRateAnnual < 0m)
            throw new ArgumentOutOfRangeException(nameof(defaultInterestRateAnnual), defaultInterestRateAnnual, "Default interest rate cannot be negative.");
        if (defaultGraceDays < 0)
            throw new ArgumentOutOfRangeException(nameof(defaultGraceDays), defaultGraceDays, "Default grace days cannot be negative.");

        CommitmentId = commitmentId.Trim();
        FundProfileId = fundProfileId.Trim();
        LedgerBookId = ledgerBookId;
        CapitalAccountId = capitalAccountId.Trim();
        InvestorId = investorId.Trim();
        Currency = NormalizeCurrency(currency, nameof(currency));
        TotalCommitment = totalCommitment;
        CommitmentDate = commitmentDate;
        InvestmentPeriodEndDate = investmentPeriodEndDate;
        Status = status;
        RecallCapPercent = recallCapPercent;
        DefaultInterestRateAnnual = defaultInterestRateAnnual;
        DefaultInterestConvention = defaultInterestConvention;
        DefaultGraceDays = defaultGraceDays;
    }

    public string CommitmentId { get; }

    public string FundProfileId { get; }

    public Guid? LedgerBookId { get; }

    public string CapitalAccountId { get; }

    public string InvestorId { get; }

    public string Currency { get; }

    public decimal TotalCommitment { get; }

    public DateOnly CommitmentDate { get; }

    public DateOnly? InvestmentPeriodEndDate { get; }

    public CommitmentStatus Status { get; }

    /// <summary>Fraction of the total commitment that recallable distributions may restore.</summary>
    public decimal RecallCapPercent { get; }

    public decimal DefaultInterestRateAnnual { get; }

    public DefaultInterestConvention DefaultInterestConvention { get; }

    /// <summary>Days after an installment's due date before default interest starts accruing.</summary>
    public int DefaultGraceDays { get; }

    /// <summary>Maximum cumulative amount recallable distributions may restore to uncalled.</summary>
    public decimal RecallCapAmount => RoundCurrency(RecallCapPercent * TotalCommitment);

    public bool IsCallable => Status is CommitmentStatus.Active or CommitmentStatus.InvestmentPeriodClosed;

    internal static string NormalizeCurrency(string currency, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currency, parameterName);
        var normalized = currency.Trim().ToUpperInvariant();
        if (normalized.Length != 3 || !normalized.All(static c => c is >= 'A' and <= 'Z'))
            throw new ArgumentException("Currency codes must be three alphabetic ISO-style characters.", parameterName);

        return normalized;
    }
}

/// <summary>One planned installment in a commitment's drawdown schedule.</summary>
public sealed record DrawdownInstallment
{
    public DrawdownInstallment(
        string installmentId,
        string commitmentId,
        int sequence,
        DateOnly noticeDate,
        DateOnly dueDate,
        decimal? callPercent,
        decimal? callAmount,
        DrawdownInstallmentStatus status,
        Guid? journalEntryId = null,
        string? paymentIntentId = null,
        IReadOnlyList<string>? evidenceLinks = null)
    {
        if (string.IsNullOrWhiteSpace(installmentId))
            throw new ArgumentException("Installment identifier must not be null or whitespace.", nameof(installmentId));
        if (string.IsNullOrWhiteSpace(commitmentId))
            throw new ArgumentException("Commitment identifier must not be null or whitespace.", nameof(commitmentId));
        if (sequence < 1)
            throw new ArgumentOutOfRangeException(nameof(sequence), sequence, "Installment sequence must be positive.");
        if (dueDate < noticeDate)
            throw new ArgumentException("Installment due date cannot precede its notice date.", nameof(dueDate));
        if (callPercent is null && callAmount is null)
            throw new ArgumentException("An installment requires a call percent or a call amount.", nameof(callAmount));
        if (callPercent is <= 0m or > 1m)
            throw new ArgumentOutOfRangeException(nameof(callPercent), callPercent, "Call percent must be greater than 0 and at most 1.");
        if (callAmount is <= 0m)
            throw new ArgumentOutOfRangeException(nameof(callAmount), callAmount, "Call amount must be positive.");

        InstallmentId = installmentId.Trim();
        CommitmentId = commitmentId.Trim();
        Sequence = sequence;
        NoticeDate = noticeDate;
        DueDate = dueDate;
        CallPercent = callPercent;
        CallAmount = callAmount;
        Status = status;
        JournalEntryId = journalEntryId;
        PaymentIntentId = string.IsNullOrWhiteSpace(paymentIntentId) ? null : paymentIntentId.Trim();
        EvidenceLinks = evidenceLinks ?? [];
    }

    public string InstallmentId { get; }

    public string CommitmentId { get; }

    public int Sequence { get; }

    public DateOnly NoticeDate { get; }

    public DateOnly DueDate { get; }

    /// <summary>Fraction of the total commitment to call; used when <see cref="CallAmount"/> is null.</summary>
    public decimal? CallPercent { get; }

    /// <summary>Absolute amount to call; takes precedence over <see cref="CallPercent"/>.</summary>
    public decimal? CallAmount { get; }

    public DrawdownInstallmentStatus Status { get; init; }

    /// <summary>Set once the capital-call journal posts.</summary>
    public Guid? JournalEntryId { get; init; }

    public string? PaymentIntentId { get; init; }

    public IReadOnlyList<string> EvidenceLinks { get; init; }

    public decimal ResolveCallAmount(decimal totalCommitment)
        => CallAmount ?? RoundCurrency((CallPercent ?? 0m) * totalCommitment);
}

/// <summary>Planned drawdown schedule for one commitment.</summary>
public sealed record DrawdownSchedule
{
    public DrawdownSchedule(
        string scheduleId,
        string commitmentId,
        DateOnly effectiveFrom,
        IReadOnlyList<DrawdownInstallment> installments)
    {
        if (string.IsNullOrWhiteSpace(scheduleId))
            throw new ArgumentException("Schedule identifier must not be null or whitespace.", nameof(scheduleId));
        if (string.IsNullOrWhiteSpace(commitmentId))
            throw new ArgumentException("Commitment identifier must not be null or whitespace.", nameof(commitmentId));
        ArgumentNullException.ThrowIfNull(installments);
        if (installments.Count == 0)
            throw new ArgumentException("A drawdown schedule requires at least one installment.", nameof(installments));

        var normalizedCommitmentId = commitmentId.Trim();
        var seenSequences = new HashSet<int>();
        foreach (var installment in installments)
        {
            ArgumentNullException.ThrowIfNull(installment);
            if (!string.Equals(installment.CommitmentId, normalizedCommitmentId, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    $"Installment '{installment.InstallmentId}' belongs to commitment '{installment.CommitmentId}', not '{normalizedCommitmentId}'.",
                    nameof(installments));
            }

            if (!seenSequences.Add(installment.Sequence))
                throw new ArgumentException($"Installment sequence {installment.Sequence} is duplicated.", nameof(installments));
        }

        ScheduleId = scheduleId.Trim();
        CommitmentId = normalizedCommitmentId;
        EffectiveFrom = effectiveFrom;
        Installments = installments.OrderBy(static installment => installment.Sequence).ToArray();
    }

    public string ScheduleId { get; }

    public string CommitmentId { get; }

    public DateOnly EffectiveFrom { get; }

    public IReadOnlyList<DrawdownInstallment> Installments { get; }
}

/// <summary>
/// Neutral posted-activity event folded by the commitment roll-forward. Adapted from the
/// private-capital fund-event projection; recallability rides journal metadata and is supplied
/// by the caller because the projection does not carry it structurally.
/// </summary>
public sealed record CommitmentActivityEvent
{
    public CommitmentActivityEvent(
        string fundEventId,
        ManualJournalEntryTypeDto entryType,
        DateOnly effectiveDate,
        decimal grossAmount,
        DistributionRecallability recallability = DistributionRecallability.NonRecallable)
    {
        if (string.IsNullOrWhiteSpace(fundEventId))
            throw new ArgumentException("Fund event identifier must not be null or whitespace.", nameof(fundEventId));
        if (grossAmount < 0m)
            throw new ArgumentOutOfRangeException(nameof(grossAmount), grossAmount, "Gross amount cannot be negative.");

        FundEventId = fundEventId.Trim();
        EntryType = entryType;
        EffectiveDate = effectiveDate;
        GrossAmount = grossAmount;
        Recallability = recallability;
    }

    public string FundEventId { get; }

    public ManualJournalEntryTypeDto EntryType { get; }

    public DateOnly EffectiveDate { get; }

    public decimal GrossAmount { get; }

    public DistributionRecallability Recallability { get; }
}

/// <summary>
/// Governed release of residual uncalled commitment (operator-driven, evidence-backed).
/// </summary>
public sealed record CommitmentExpiryEvent
{
    public CommitmentExpiryEvent(
        string expiryEventId,
        string commitmentId,
        DateOnly effectiveDate,
        decimal amount,
        string recordedBy,
        IReadOnlyList<string>? evidenceLinks = null)
    {
        if (string.IsNullOrWhiteSpace(expiryEventId))
            throw new ArgumentException("Expiry event identifier must not be null or whitespace.", nameof(expiryEventId));
        if (string.IsNullOrWhiteSpace(commitmentId))
            throw new ArgumentException("Commitment identifier must not be null or whitespace.", nameof(commitmentId));
        if (amount <= 0m)
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "Expiry amount must be positive.");
        if (string.IsNullOrWhiteSpace(recordedBy))
            throw new ArgumentException("Expiry recorder must not be null or whitespace.", nameof(recordedBy));

        ExpiryEventId = expiryEventId.Trim();
        CommitmentId = commitmentId.Trim();
        EffectiveDate = effectiveDate;
        Amount = amount;
        RecordedBy = recordedBy.Trim();
        EvidenceLinks = evidenceLinks ?? [];
    }

    public string ExpiryEventId { get; }

    public string CommitmentId { get; }

    public DateOnly EffectiveDate { get; }

    public decimal Amount { get; }

    public string RecordedBy { get; }

    public IReadOnlyList<string> EvidenceLinks { get; }
}

/// <summary>One step in a commitment roll-forward, after applying a single dated event.</summary>
public sealed record CommitmentRollForwardStep(
    string FundEventId,
    ManualJournalEntryTypeDto EntryType,
    DateOnly EffectiveDate,
    decimal CallDelta,
    decimal RecallableDelta,
    decimal ExpiredDelta,
    decimal RunningUncalled,
    decimal RunningNetCalled);

/// <summary>
/// Uncalled-commitment roll-forward for one investor commitment. Carries the invariant
/// net-called + uncalled + expired = total on every step.
/// </summary>
public sealed record CommitmentRollForward(
    InvestorCommitment Commitment,
    decimal CumulativeCalled,
    decimal CumulativeRecallableRestored,
    decimal CumulativeExpired,
    IReadOnlyList<CommitmentRollForwardStep> Steps,
    IReadOnlyList<AccountingConfigurationValidationIssueDto> ValidationIssues)
{
    public string CommitmentId => Commitment.CommitmentId;

    public string CapitalAccountId => Commitment.CapitalAccountId;

    public string InvestorId => Commitment.InvestorId;

    public string Currency => Commitment.Currency;

    public decimal TotalCommitment => Commitment.TotalCommitment;

    /// <summary>Capital drawn and still outstanding (gross calls minus recallable restorations).</summary>
    public decimal NetCalled => CumulativeCalled - CumulativeRecallableRestored;

    /// <summary>Commitment still callable.</summary>
    public decimal Uncalled =>
        TotalCommitment - CumulativeCalled + CumulativeRecallableRestored - CumulativeExpired;

    public decimal CalledPercent => TotalCommitment == 0m ? 0m : NetCalled / TotalCommitment;

    public decimal RemainingRecallableCapacity =>
        Math.Max(0m, Commitment.RecallCapAmount - CumulativeRecallableRestored);

    /// <summary>Runtime tripwire for arithmetic drift and over-calls.</summary>
    public bool InvariantHolds =>
        Math.Abs(NetCalled + Uncalled + CumulativeExpired - TotalCommitment) <= LedgerToleranceConstants.Balance
        && Uncalled >= -LedgerToleranceConstants.Balance
        && !ValidationIssues.Any(static issue =>
            issue.Severity == AccountingConfigurationValidationSeverityDto.Critical);

    /// <summary>Recallable distributions applied during the fold, with their cap splits.</summary>
    public IReadOnlyList<RecallableDistributionEvent> RecallableDistributions { get; init; } = [];
}

/// <summary>Cash received against one drawdown installment (funding evidence for default detection).</summary>
public sealed record CapitalCallFundingReceipt
{
    public CapitalCallFundingReceipt(string installmentId, DateOnly receivedDate, decimal amount)
    {
        if (string.IsNullOrWhiteSpace(installmentId))
            throw new ArgumentException("Installment identifier must not be null or whitespace.", nameof(installmentId));
        if (amount <= 0m)
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "Funding receipt amount must be positive.");

        InstallmentId = installmentId.Trim();
        ReceivedDate = receivedDate;
        Amount = amount;
    }

    public string InstallmentId { get; }

    public DateOnly ReceivedDate { get; }

    public decimal Amount { get; }
}

/// <summary>Recallable return-of-capital split into restored and permanent portions.</summary>
public sealed record RecallableDistributionEvent(
    string FundEventId,
    string CommitmentId,
    DateOnly EffectiveDate,
    decimal GrossReturnOfCapital,
    decimal RestoredToUncalled,
    decimal PermanentPortion);

/// <summary>An unfunded capital-call installment past its due date, with interest accruals.</summary>
public sealed record CapitalCallDefault(
    string DefaultId,
    InvestorCommitment Commitment,
    DrawdownInstallment Installment,
    decimal DefaultedAmount,
    DateOnly DueDate,
    DateOnly? CuredDate,
    DrawdownInstallmentStatus Status,
    IReadOnlyList<DefaultInterestAccrual> Accruals)
{
    public decimal AccruedInterest => Accruals.Sum(static accrual => accrual.AccruedInterest);
}

/// <summary>One default-interest accrual span computed for a defaulted installment.</summary>
public sealed record DefaultInterestAccrual(
    string AccrualId,
    string DefaultId,
    DateOnly AccrualFrom,
    DateOnly AccrualTo,
    decimal Principal,
    decimal AnnualRate,
    DefaultInterestConvention Convention,
    decimal AccruedInterest);

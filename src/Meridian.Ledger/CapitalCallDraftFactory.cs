using System.Globalization;

namespace Meridian.Ledger;

/// <summary>
/// Builds governed <see cref="AutomatedJournalDraft"/>s for commitment-driven capital-call
/// postings so they satisfy every critical gate the private-capital fund-event projector
/// enforces (effective date, idempotency key, fund event identity, capital-account impact via
/// an equity leg, and evidence). Drafts flow through the standard
/// <see cref="AutomatedJournalApproval"/> submit → approve → post lifecycle.
/// </summary>
public static class CapitalCallDraftFactory
{
    /// <summary>
    /// Drafts the call posting: debit the investor's capital-call receivable, credit investor
    /// capital. The credit leg is equity, which is what the projector reads as the
    /// capital-account impact.
    /// </summary>
    public static AutomatedJournalDraft BuildCapitalCallDraft(
        InvestorCommitment commitment,
        string installmentId,
        decimal amount,
        DateOnly effectiveDate,
        DateTimeOffset occurredAtUtc,
        IReadOnlyList<JournalEvidenceReference>? evidenceReferences = null,
        string? paymentIntentId = null)
    {
        ArgumentNullException.ThrowIfNull(commitment);
        var normalizedInstallmentId = RequireId(installmentId, nameof(installmentId));
        ValidateAmount(amount);

        var idempotencyKey = FormattableString.Invariant(
            $"capital-call:{commitment.CommitmentId}:{normalizedInstallmentId}");
        var description = FormattableString.Invariant(
            $"Capital call {normalizedInstallmentId} for {commitment.InvestorId} ({commitment.Currency} {amount.ToString("0.00", CultureInfo.InvariantCulture)})");

        return Build(
            commitment,
            AutomatedJournalEventKind.CapitalCallIssued,
            "CapitalCall",
            FormattableString.Invariant($"fund-event:{commitment.FundProfileId}:capital-call:{normalizedInstallmentId}"),
            normalizedInstallmentId,
            amount,
            effectiveDate,
            occurredAtUtc,
            idempotencyKey,
            description,
            lines:
            [
                (LedgerAccounts.CapitalCallReceivableFor(commitment.InvestorId), amount, 0m, BuildDimensions(commitment)),
                (LedgerAccounts.InvestorCapitalFor(commitment.InvestorId), 0m, amount, BuildDimensions(commitment)),
            ],
            evidenceReferences,
            paymentIntentId);
    }

    /// <summary>
    /// Drafts the settlement posting on cash receipt: debit cash, credit the outstanding
    /// capital-call receivable. Uses the same fund event as the originating call so the
    /// projector groups call and funding into one lifecycle.
    /// </summary>
    public static AutomatedJournalDraft BuildCapitalCallFundingDraft(
        InvestorCommitment commitment,
        string installmentId,
        decimal amount,
        DateOnly effectiveDate,
        DateTimeOffset occurredAtUtc,
        string? cashFinancialAccountId = null,
        IReadOnlyList<JournalEvidenceReference>? evidenceReferences = null,
        string? paymentIntentId = null)
    {
        ArgumentNullException.ThrowIfNull(commitment);
        var normalizedInstallmentId = RequireId(installmentId, nameof(installmentId));
        ValidateAmount(amount);

        var cash = string.IsNullOrWhiteSpace(cashFinancialAccountId)
            ? LedgerAccounts.CashAccount(commitment.FundProfileId)
            : LedgerAccounts.CashAccount(cashFinancialAccountId);
        var idempotencyKey = FormattableString.Invariant(
            $"capital-call-funding:{commitment.CommitmentId}:{normalizedInstallmentId}");
        var description = FormattableString.Invariant(
            $"Capital call funding {normalizedInstallmentId} from {commitment.InvestorId} ({commitment.Currency} {amount.ToString("0.00", CultureInfo.InvariantCulture)})");

        return Build(
            commitment,
            AutomatedJournalEventKind.CapitalCallFunded,
            "CapitalCall",
            FormattableString.Invariant($"fund-event:{commitment.FundProfileId}:capital-call:{normalizedInstallmentId}"),
            normalizedInstallmentId,
            amount,
            effectiveDate,
            occurredAtUtc,
            idempotencyKey,
            description,
            lines:
            [
                (cash, amount, 0m, BuildDimensions(commitment)),
                (LedgerAccounts.CapitalCallReceivableFor(commitment.InvestorId), 0m, amount, BuildDimensions(commitment)),
            ],
            evidenceReferences,
            paymentIntentId);
    }

    /// <summary>
    /// Drafts a default-interest accrual for an unfunded call: debit the investor's default
    /// interest receivable, credit fund interest income. The credit leg is income, not equity,
    /// so it never distorts the capital-account equity roll-forward.
    /// </summary>
    public static AutomatedJournalDraft BuildDefaultInterestDraft(
        CapitalCallDefault capitalCallDefault,
        DefaultInterestAccrual accrual,
        DateTimeOffset occurredAtUtc,
        IReadOnlyList<JournalEvidenceReference>? evidenceReferences = null)
    {
        ArgumentNullException.ThrowIfNull(capitalCallDefault);
        ArgumentNullException.ThrowIfNull(accrual);
        ValidateAmount(accrual.AccruedInterest);

        var commitment = capitalCallDefault.Commitment;
        var idempotencyKey = FormattableString.Invariant(
            $"default-interest:{capitalCallDefault.DefaultId}:{accrual.AccrualTo:yyyyMMdd}");
        var description = FormattableString.Invariant(
            $"Default interest on capital call {capitalCallDefault.Installment.InstallmentId} for {commitment.InvestorId} through {accrual.AccrualTo:yyyy-MM-dd}");

        return Build(
            commitment,
            AutomatedJournalEventKind.CapitalCallDefaultInterestAccrued,
            "DefaultInterest",
            FormattableString.Invariant($"fund-event:{commitment.FundProfileId}:default-interest:{capitalCallDefault.DefaultId}:{accrual.AccrualTo:yyyyMMdd}"),
            capitalCallDefault.Installment.InstallmentId,
            accrual.AccruedInterest,
            accrual.AccrualTo,
            occurredAtUtc,
            idempotencyKey,
            description,
            lines:
            [
                (LedgerAccounts.CapitalCallDefaultInterestReceivableFor(commitment.InvestorId), accrual.AccruedInterest, 0m, BuildDimensions(commitment)),
                (LedgerAccounts.CapitalCallDefaultInterestIncomeFor(commitment.FundProfileId), 0m, accrual.AccruedInterest, BuildDimensions(commitment)),
            ],
            evidenceReferences,
            paymentIntentId: null);
    }

    private static AutomatedJournalDraft Build(
        InvestorCommitment commitment,
        AutomatedJournalEventKind kind,
        string fundEventType,
        string fundEventId,
        string installmentId,
        decimal amount,
        DateOnly effectiveDate,
        DateTimeOffset occurredAtUtc,
        string idempotencyKey,
        string description,
        (LedgerAccount account, decimal debit, decimal credit, LedgerLineDimensionSet? dimensions)[] lines,
        IReadOnlyList<JournalEvidenceReference>? evidenceReferences,
        string? paymentIntentId)
    {
        var journalEvent = new AutomatedJournalEvent(
            kind,
            commitment.FundProfileId,
            amount,
            occurredAtUtc,
            Description: description,
            EffectiveDate: effectiveDate,
            IdempotencyKey: idempotencyKey,
            EvidenceReferences: evidenceReferences);
        var metadata = new JournalEntryMetadata(
            ActivityType: FormattableString.Invariant($"private-capital.{fundEventType.ToLowerInvariant()}"),
            EffectiveDate: effectiveDate,
            IdempotencyKey: idempotencyKey,
            FundEventId: fundEventId,
            FundEventType: fundEventType,
            CapitalAccountId: commitment.CapitalAccountId,
            InvestorId: commitment.InvestorId,
            PaymentIntentId: paymentIntentId,
            Tags: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["commitmentId"] = commitment.CommitmentId,
                ["drawdownInstallmentId"] = installmentId,
                ["commitmentCurrency"] = commitment.Currency,
            },
            EvidenceReferences: evidenceReferences);

        return new AutomatedJournalDraft(journalEvent, description, lines, metadata);
    }

    private static LedgerLineDimensionSet BuildDimensions(InvestorCommitment commitment)
        => new(
            FundId: commitment.FundProfileId,
            InvestorId: commitment.InvestorId,
            CapitalAccountId: commitment.CapitalAccountId);

    private static string RequireId(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Identifier must not be null or whitespace.", parameterName);

        return value.Trim();
    }

    private static void ValidateAmount(decimal amount)
    {
        if (amount <= 0m)
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "Draft amount must be positive.");
    }
}

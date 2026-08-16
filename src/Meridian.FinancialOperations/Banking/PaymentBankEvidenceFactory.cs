using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Meridian.Contracts.Banking;
using Meridian.Ledger;

namespace Meridian.FinancialOperations.Banking;

/// <summary>
/// Centralizes payment currency and bank-evidence normalization so in-memory and durable
/// implementations enforce identical approval, binding, and replay semantics.
/// </summary>
internal static class PaymentBankEvidenceFactory
{
    private const int MaximumEvidenceIdLength = 200;

    public static string NormalizeRequiredCurrency(string? value, string subject)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new BankingException($"{subject} currency is required as a recognized three-letter code.");
        }

        if (!CurrencyCodeCatalog.TryNormalizeCurrent(value, out var normalized))
        {
            throw new BankingException($"{subject} currency must be a current supported three-letter currency code.");
        }

        return normalized;
    }

    private static string NormalizeRetainedCurrency(string? value, string subject)
    {
        if (!CurrencyCodeCatalog.TryNormalizeRecognized(value, out var normalized))
        {
            throw new BankingException(
                $"{subject} currency must be a recognized current or historical three-letter currency code.");
        }

        return normalized;
    }

    public static BankTransactionDto Create(
        PendingPaymentDto pending,
        RecordPaymentBankEvidenceRequest request)
    {
        ArgumentNullException.ThrowIfNull(pending);
        ArgumentNullException.ThrowIfNull(request);

        if (pending.Status != PaymentApprovalStatus.Approved)
        {
            throw new BankingException(
                $"Payment '{pending.PendingPaymentId}' must be approved before bank confirmation, return, or reversal evidence is recorded.");
        }

        if (string.IsNullOrWhiteSpace(pending.Currency))
        {
            throw new BankingException(
                $"Payment '{pending.PendingPaymentId}' has no retained currency. Remediate this legacy intent before recording bank evidence.");
        }

        var paymentCurrency = NormalizeRetainedCurrency(pending.Currency, "Payment intent");
        var evidenceCurrency = request.Currency is null
            ? paymentCurrency
            : NormalizeRetainedCurrency(request.Currency, "Bank evidence");
        if (!string.Equals(evidenceCurrency, paymentCurrency, StringComparison.Ordinal))
        {
            throw new BankingException(
                $"Bank evidence currency '{evidenceCurrency}' must match payment currency '{paymentCurrency}'.");
        }

        var amount = request.Amount ?? pending.Amount;
        if (amount <= 0m)
        {
            throw new BankingException("Bank evidence amount must be positive.");
        }

        if (amount != pending.Amount)
        {
            throw new BankingException(
                $"Bank evidence amount '{amount.ToString(CultureInfo.InvariantCulture)}' must match payment amount '{pending.Amount.ToString(CultureInfo.InvariantCulture)}'.");
        }

        var evidenceId = NormalizeEvidenceId(request.EvidenceId);
        var evidenceType = NormalizeEvidenceType(request.EvidenceType);
        var transactionDate = request.TransactionDate ?? pending.EffectiveDate;
        var settlementDate = request.SettlementDate ?? transactionDate;
        if (settlementDate < transactionDate)
        {
            throw new BankingException("Bank evidence settlement date cannot be before transaction date.");
        }

        var externalRef = FirstNonBlank(
            request.ExternalRef,
            pending.ExternalRef,
            pending.PendingPaymentId.ToString("D"));
        var recordedBy = FirstNonBlank(request.RecordedBy);
        var isVoided = evidenceType is "BankReturn" or "BankReversal" or "BankFailure";
        var canonicalInputHash = ComputeCanonicalInputHash(
            pending.PendingPaymentId,
            pending.EntityId,
            evidenceId,
            evidenceType,
            pending.EffectiveDate,
            transactionDate,
            settlementDate,
            amount,
            paymentCurrency,
            externalRef,
            recordedBy,
            isVoided);

        return new BankTransactionDto(
            BankTransactionId: Guid.NewGuid(),
            EntityId: pending.EntityId,
            TransactionType: evidenceType,
            EffectiveDate: pending.EffectiveDate,
            TransactionDate: transactionDate,
            SettlementDate: settlementDate,
            Amount: amount,
            Currency: paymentCurrency,
            ExternalRef: externalRef,
            RecordedAt: DateTimeOffset.UtcNow,
            IsVoided: isVoided,
            RecordedBy: recordedBy,
            PendingPaymentId: pending.PendingPaymentId,
            EvidenceId: evidenceId,
            CanonicalInputHash: canonicalInputHash);
    }

    private static string NormalizeEvidenceId(string? evidenceId)
    {
        if (string.IsNullOrWhiteSpace(evidenceId))
        {
            throw new BankingException("EvidenceId is required as a stable idempotency key for payment bank evidence.");
        }

        var normalized = evidenceId.Trim();
        if (normalized.Length > MaximumEvidenceIdLength)
        {
            throw new BankingException($"EvidenceId cannot exceed {MaximumEvidenceIdLength} characters.");
        }

        return normalized;
    }

    private static string NormalizeEvidenceType(string? evidenceType)
        => (evidenceType ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "" or "confirmation" or "confirmed" or "bankconfirmation" or "bank-confirmation" => "BankConfirmation",
            "return" or "returned" or "bankreturn" or "bank-return" => "BankReturn",
            "reversal" or "reversed" or "bankreversal" or "bank-reversal" => "BankReversal",
            "failure" or "failed" or "reject" or "rejected" or "bankfailure" or "bank-failure" => "BankFailure",
            _ => throw new BankingException(
                "Bank evidence type must be BankConfirmation, BankReturn, BankReversal, or BankFailure.")
        };

    private static string ComputeCanonicalInputHash(
        Guid pendingPaymentId,
        Guid entityId,
        string evidenceId,
        string evidenceType,
        DateOnly effectiveDate,
        DateOnly transactionDate,
        DateOnly settlementDate,
        decimal amount,
        string currency,
        string? externalRef,
        string? recordedBy,
        bool isVoided)
    {
        var canonical = new StringBuilder();
        Append(canonical, pendingPaymentId.ToString("D"));
        Append(canonical, entityId.ToString("D"));
        Append(canonical, evidenceId);
        Append(canonical, evidenceType);
        Append(canonical, effectiveDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        Append(canonical, transactionDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        Append(canonical, settlementDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        Append(canonical, amount.ToString("G29", CultureInfo.InvariantCulture));
        Append(canonical, currency);
        Append(canonical, externalRef);
        Append(canonical, recordedBy);
        Append(canonical, isVoided ? "1" : "0");

        // Deliberately NOT routed through Sha256Digest (which lowercases): this hash is the
        // idempotency identity persisted on bank_transactions for payment evidence, so changing
        // its casing would let previously recorded evidence re-record as new (#2691).
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())));
    }

    private static void Append(StringBuilder builder, string? value)
    {
        if (value is null)
        {
            builder.Append("-1:");
            return;
        }

        builder.Append(value.Length.ToString(CultureInfo.InvariantCulture));
        builder.Append(':');
        builder.Append(value);
    }

    private static string? FirstNonBlank(params string?[] values)
        => values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value))?.Trim();
}

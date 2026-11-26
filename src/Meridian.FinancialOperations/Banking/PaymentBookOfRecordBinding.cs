using Meridian.Contracts.Banking;
using Meridian.Contracts.Workstation;

namespace Meridian.FinancialOperations.Banking;

/// <summary>
/// Binds a payment intent to the ledger book it is released from, and refuses approval while that
/// book is not the book of record. A shadow book runs in parallel with an incumbent that still
/// releases the real payments, so approving here as well would pay twice (W11-SHADOW-001).
/// </summary>
internal static class PaymentBookOfRecordBinding
{
    /// <summary>Returns the server-bound scope for a new intent, or none for an unbound intent.</summary>
    public static (string? TenantId, string? CompanyId, Guid? LedgerBookId) Resolve(InitiatePaymentRequest request)
    {
        if (request.LedgerBookId is not { } ledgerBookId)
            return (null, null, null);
        if (ledgerBookId == Guid.Empty)
            throw new BankingException("A payment ledger book id must not be empty.");

        var tenantId = request.BoundTenantId?.Trim();
        var companyId = request.BoundCompanyId?.Trim();
        if (string.IsNullOrWhiteSpace(tenantId) || string.IsNullOrWhiteSpace(companyId))
        {
            throw new BankingException(
                "A payment bound to a ledger book requires the authenticated tenant and company that own the book.");
        }

        return (tenantId, companyId, ledgerBookId);
    }

    /// <summary>
    /// Refuses approval of a bound intent unless its ledger book is the book of record. An unbound
    /// legacy intent carries no book to check and is left to the existing approval controls.
    /// </summary>
    public static async Task EnsureBookOfRecordAsync(
        IBookOfRecordGuard? guard,
        PendingPaymentDto payment,
        CancellationToken ct)
    {
        if (payment.LedgerBookId is not { } ledgerBookId)
            return;
        if (string.IsNullOrWhiteSpace(payment.TenantId) || string.IsNullOrWhiteSpace(payment.CompanyId))
        {
            throw new BankingConflictException(
                $"Payment '{payment.PendingPaymentId}' names a ledger book without its owning tenant and company.");
        }

        if (guard is null)
        {
            throw new BankingConflictException(
                $"Payment '{payment.PendingPaymentId}' is released from a ledger book, but book-of-record status is unavailable on this host.");
        }

        var status = await guard.GetStatusAsync(payment.TenantId, payment.CompanyId, ledgerBookId, ct).ConfigureAwait(false);
        if (!status.IsBookOfRecord)
            throw new BankingConflictException(status.Reason);
    }
}

using System.Security.Cryptography;
using System.Text;
using Meridian.Contracts.Ledger;

namespace Meridian.Ui.Shared.Services;

internal enum CloseReopenReceiptRetention
{
    Created,
    ExistingExact,
    Missing,
    Conflict
}

public sealed partial class ManualJournalEntryWorkbenchService
{
    private readonly SemaphoreSlim _closeReopenReceiptGate = new(1, 1);

    internal async Task<CloseReopenReceiptRetention> RetainCloseReopenReceiptAsync(
        string fundProfileId,
        Guid ledgerBookId,
        Guid ledgerPeriodId,
        long ledgerPeriodVersion,
        string actor,
        string correlationId,
        string commandHash,
        IReadOnlyList<string> evidenceLinks,
        string? tenantId,
        string? companyId,
        bool allowCreate,
        CancellationToken ct)
    {
        var actionPrefix = $"GovernedLedgerPeriodReopen:{ledgerPeriodId:D}:";
        var action = $"{actionPrefix}from-version:{ledgerPeriodVersion}";
        await _closeReopenReceiptGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var retained = await _auditStore
                .ListAsync(fundProfileId, ledgerBookId, ct, tenantId, companyId)
                .ConfigureAwait(false);
            var periodReceipts = retained
                .Where(item => item.Action.StartsWith(actionPrefix, StringComparison.Ordinal))
                .OrderByDescending(static item => item.RecordedAtUtc)
                .ToArray();
            var relevantReceipts = allowCreate
                ? periodReceipts.Where(item => string.Equals(item.Action, action, StringComparison.Ordinal)).ToArray()
                : periodReceipts.Take(1).ToArray();
            if (relevantReceipts.Any(item =>
                    string.Equals(item.CorrelationId, correlationId, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(item.AfterHash, commandHash, StringComparison.OrdinalIgnoreCase)))
            {
                return CloseReopenReceiptRetention.ExistingExact;
            }

            if (relevantReceipts.Length > 0)
            {
                return CloseReopenReceiptRetention.Conflict;
            }

            if (!allowCreate)
            {
                return CloseReopenReceiptRetention.Missing;
            }

            var receiptIdBytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{action}|{commandHash}"));
            await _auditStore.AppendAsync(
                    new AccountingActionAuditEventDto(
                        new Guid(receiptIdBytes.AsSpan(0, 16)),
                        DateTimeOffset.UtcNow,
                        actor.Trim(),
                        action,
                        fundProfileId,
                        ledgerBookId,
                        correlationId.Trim(),
                        "ledger-period:hard-closed;intent:governed-reopen",
                        commandHash,
                        [],
                        evidenceLinks,
                        CompanyId: companyId,
                        TenantId: tenantId),
                    ct)
                .ConfigureAwait(false);
            return CloseReopenReceiptRetention.Created;
        }
        finally
        {
            _closeReopenReceiptGate.Release();
        }
    }
}

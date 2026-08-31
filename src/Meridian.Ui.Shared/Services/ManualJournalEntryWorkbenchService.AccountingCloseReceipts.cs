using Meridian.Contracts.Ledger;
using Meridian.Contracts.Integrity;

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

            var receiptIdBytes = Sha256Digest.ComputeBytesUtf8($"{action}|{commandHash}");
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

    /// <summary>
    /// Narrow release for a close-locked closing batch after the exact governed ledger-period
    /// reopen intent is already durable. This is intentionally internal: ordinary lifecycle
    /// callers continue to be unable to reverse or unlock <c>CloseLocked</c> journals.
    /// </summary>
    internal async Task<JournalEntryLifecycleActionResultDto> ReverseCloseLockedClosingEntryForGovernedReopenAsync(
        JournalEntryLifecycleActionRequestDto request,
        Guid ledgerPeriodId,
        long ledgerPeriodVersion,
        string reopenCommandHash,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Action != JournalEntryLifecycleActionDto.Reverse)
            throw new InvalidOperationException("The governed close-reopen release only supports reversal drafting.");
        EnsureHumanOrigin(request.ActionOrigin, "reverse a close-locked closing entry during governed period reopen");
        var fundProfileId = NormalizeFundProfileId(request.FundProfileId);
        var ledgerBookId = request.LedgerBookId is { } bookId && bookId != Guid.Empty
            ? bookId
            : throw new InvalidOperationException("Governed close-reopen reversal requires the exact ledger book.");
        var correlationId = RequireText(request.CorrelationId, nameof(request.CorrelationId));
        var receipt = await RetainCloseReopenReceiptAsync(
                fundProfileId,
                ledgerBookId,
                ledgerPeriodId,
                ledgerPeriodVersion,
                RequireText(request.Actor, nameof(request.Actor)),
                correlationId,
                RequireText(reopenCommandHash, nameof(reopenCommandHash)),
                request.EvidenceLinks,
                request.TenantId,
                request.CompanyId,
                allowCreate: false,
                ct)
            .ConfigureAwait(false);
        if (receipt != CloseReopenReceiptRetention.ExistingExact)
        {
            throw new InvalidOperationException(
                "A close-locked closing entry can only be reversed after an exact governed reopen intent is retained for the same period, actor, correlation, reason, approval, and evidence.");
        }

        var draft = await _draftStore
            .GetAsync(fundProfileId, request.JournalEntryId, ct, request.TenantId, request.CompanyId)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Manual journal entry '{request.JournalEntryId:D}' was not found.");
        EnsureRequestedLedgerBookMatchesDraft(request.LedgerBookId, draft);
        var idempotent = await TryBuildIdempotentLifecycleResultAsync(fundProfileId, draft, request, ct)
            .ConfigureAwait(false);
        if (idempotent is not null)
            return idempotent;
        if (draft.Version != request.Version)
            throw new InvalidOperationException("Manual journal entry draft version is stale.");
        if (draft.Status != ManualJournalEntryStatusDto.CloseLocked ||
            draft.EntryType != ManualJournalEntryTypeDto.ClosingEntry ||
            !string.Equals(draft.PeriodId, ledgerPeriodId.ToString("D"), StringComparison.OrdinalIgnoreCase) ||
            draft.ClosedLockedAtUtc is null ||
            string.IsNullOrWhiteSpace(draft.CloseLockedBy))
        {
            throw new InvalidOperationException(
                "The governed close-reopen release applies only to the exact retained close-locked closing batch for the reopened ledger period.");
        }

        var validated = await NormalizeAndValidateAsync(draft, allowIncomplete: false, ct, periodIsLocked: false)
            .ConfigureAwait(false);
        return await CreateCorrectionDraftAsync(
                validated,
                request,
                reverseSides: true,
                "manual-je.governed-close-reopen-reversal-draft",
                DateTimeOffset.UtcNow,
                ct)
            .ConfigureAwait(false);
    }
}

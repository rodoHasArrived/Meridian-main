using Meridian.Contracts.Accounting.Lots;
using Meridian.FinancialOperations.Ledger;
using Meridian.Ledger;
using Meridian.Storage.Ledger;

namespace Meridian.Ui.Shared.Services;

/// <summary>
/// Request to queue one reviewed short carrying charge (stock borrow fee or payment in lieu) as a governed
/// workbench draft. The charge is calculated server-side from the retained instruction; callers never
/// supply the journal amount or lines.
/// </summary>
public sealed record RunShortCarryDraftIntakeRequest(
    string FundProfileId,
    Guid LedgerBookId,
    string Actor,
    ShortCarryInstruction Instruction,
    string? PeriodId = null,
    string? EntityId = null,
    string? TenantId = null,
    string? CompanyId = null);

public sealed partial class AutomatedJournalIntakeRunner
{
    /// <summary>
    /// Proves the instruction against the authoritative open short lot in the requested book, projects the
    /// borrow-fee or payment-in-lieu journal from it, and queues that draft for review, approval and posting.
    /// Nothing posts here; the draft carries the lot, period and evidence identities as tags.
    /// </summary>
    public async Task<AutomatedJournalIntakeRunResult> RunShortCarryIntakeAsync(
        RunShortCarryDraftIntakeRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Instruction);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.FundProfileId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Actor);
        if (request.LedgerBookId == Guid.Empty)
            throw new ArgumentException("A short carrying charge requires the ledger book that holds the short lot.", nameof(request));
        if (_ledgerJournalStore is null)
        {
            throw new InvalidOperationException(
                "Short carrying-charge intake requires the ledger journal store, which is not configured.");
        }

        var book = await _ledgerJournalStore.GetLedgerBookAsync(request.LedgerBookId, ct).ConfigureAwait(false);
        if (book is null || !string.Equals(book.FundProfileId, request.FundProfileId.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The ledger book is unavailable or does not belong to the requested fund profile.");
        }

        var lot = (await _ledgerJournalStore
                .GetTaxLotsByIdsAsync(request.LedgerBookId, [request.Instruction.TaxLotRecordId], ct)
                .ConfigureAwait(false))
            .SingleOrDefault()
            ?? throw new InvalidOperationException("The short lot named by the charge is unavailable in this ledger book.");

        AutomatedJournalDraft draft;
        try
        {
            ShortCarryPostingGuard.RequireLot(request.Instruction, lot.ToOpenLot());
            draft = ShortCarryDraftBuilder.BuildDraft(
                request.Instruction,
                lot.Account,
                lot.SecurityId,
                _timeProvider.GetUtcNow(),
                request.Actor);
        }
        catch (ArgumentException ex)
        {
            // Refusals from the lot proof or the projection describe the instruction, not a malformed call.
            throw new InvalidOperationException(ex.Message, ex);
        }

        var intake = await _intake.IntakeDraftsAsync(
                new AutomatedJournalPreparedDraftIntakeRequest(
                    request.FundProfileId,
                    book.BaseCurrency,
                    [draft],
                    request.Actor,
                    request.LedgerBookId,
                    request.PeriodId,
                    request.EntityId,
                    request.TenantId,
                    request.CompanyId),
                ct)
            .ConfigureAwait(false);
        return new AutomatedJournalIntakeRunResult([], intake);
    }
}

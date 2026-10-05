using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.AssetOperations;
using Meridian.Storage.AssetOperations;
using Meridian.Storage.Ledger;
using Meridian.Storage.SecurityMaster;

namespace Meridian.FinancialOperations.Ledger;

public sealed record CanonicalLotAmortizationPreview(
    OpenLotAmortizationInstructionDto Instruction,
    OpenLotAmortizationProjectionDto Projection);

/// <summary>
/// Read-only preparation for the existing asset-accounting candidate and independent approval
/// workflow. Posting rechecks every version and the complete lot/reference snapshot atomically.
/// Optional workstation hosts may construct the service without persistence; preview refuses
/// missing authorities before reading any source records.
/// </summary>
public sealed class CanonicalLotAmortizationService(
    ILedgerJournalStore? lots,
    ISecurityMasterStore? securities,
    IInstrumentPositionProjectionStore? positions)
{
    /// <summary>Prepares an inverse from retained posting economics and reviewed reference authority.</summary>
    public Task<CanonicalLotAmortizationPreview> PreviewReversalAsync(
        Guid ledgerBookId, Guid mutationBatchId, CancellationToken ct = default)
        => PreviewReversalAsync(ledgerBookId, mutationBatchId, null, ct);

    public async Task<CanonicalLotAmortizationPreview> PreviewReversalAsync(
        Guid ledgerBookId, Guid mutationBatchId, RetainedEvidenceIdentityDto? securityEvidence, CancellationToken ct = default)
    {
        var lotStore = lots ?? throw new InvalidOperationException("Canonical lot reversal requires an authoritative ledger journal store.");
        var securityStore = securities ?? throw new InvalidOperationException("Canonical lot reversal requires an authoritative Security Master store.");
        var positionStore = positions ?? throw new InvalidOperationException("Canonical lot reversal requires an authoritative book-position store.");
        var original = await lotStore.GetAtomicTaxLotPostingAsync(mutationBatchId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The original amortization posting is unavailable.");
        if (original.MutationKind != AtomicTaxLotMutationKind.Amortization || original.Mutations.Count != 1
            || original.Mutations[0].LotBefore is null
            || original.Mutations[0].LotAfter.LedgerBookId != ledgerBookId
            || original.Mutations[0].LotAfter.BasisAdjustment?.MutationBatchId != mutationBatchId
            || original.Mutations[0].LotAfter.BasisAdjustment?.Amortization is not { Reversal: null } inputs)
            throw new InvalidOperationException("Reversal requires one retained original amortization mutation in the requested book.");
        var mutation = original.Mutations[0];
        var current = (await lotStore.GetTaxLotsByIdsAsync(ledgerBookId, [mutation.TaxLotRecordId], ct).ConfigureAwait(false)).SingleOrDefault()
            ?? throw new InvalidOperationException("The amortization lot is unavailable.");
        if (current.LastMutationBatchId != mutationBatchId
            || !OpenLotAmortization.SameLot(current.ToOpenLot(), mutation.LotAfter.ToOpenLot()))
            throw new InvalidOperationException("Amortization reversal requires the latest unchanged lot mutation.");
        var position = await positionStore.GetBookPositionAsync(current.BookPositionId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The authoritative book position is unavailable.");
        if (position.SecurityId != current.SecurityId || position.BookContext.LedgerBookId != ledgerBookId)
            throw new InvalidOperationException("Canonical lot and book-position accounting scope differ.");
        var security = await securityStore.GetProjectionAsync(current.SecurityId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Versioned Security Master evidence is unavailable.");
        var instruction = inputs with
        {
            ExpectedLot = current.ToOpenLot(),
            Security = security,
            SecurityEvidence = securityEvidence ?? inputs.SecurityEvidence,
            ExpectedBookPositionVersion = position.Version,
            Reversal = new(mutationBatchId, original.Journal.Entry.JournalEntryId, mutation.LotBefore!.ToOpenLot())
        };
        return new(instruction, OpenLotAmortization.Project(instruction));
    }

    public async Task<CanonicalLotAmortizationPreview> PreviewAsync(
        Guid ledgerBookId, Guid taxLotRecordId, DateOnly asOfDate,
        RetainedEvidenceIdentityDto securityEvidence, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var lotStore = lots ?? throw new InvalidOperationException(
            "Canonical lot amortization cannot be previewed because no authoritative ledger journal store is configured.");
        var securityStore = securities ?? throw new InvalidOperationException(
            "Canonical lot amortization cannot be previewed because no authoritative Security Master store is configured.");
        var positionStore = positions ?? throw new InvalidOperationException(
            "Canonical lot amortization cannot be previewed because no authoritative book-position store is configured.");
        ArgumentNullException.ThrowIfNull(securityEvidence);

        var retained = (await lotStore.GetTaxLotsByIdsAsync(ledgerBookId, [taxLotRecordId], ct).ConfigureAwait(false)).SingleOrDefault()
            ?? throw new InvalidOperationException("The canonical lot was not found in the requested book.");
        var security = await securityStore.GetProjectionAsync(retained.SecurityId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Versioned Security Master evidence is unavailable.");
        var position = await positionStore.GetBookPositionAsync(retained.BookPositionId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The authoritative book position is unavailable.");
        if (position.SecurityId != retained.SecurityId || position.BookContext.LedgerBookId != ledgerBookId)
            throw new InvalidOperationException("Canonical lot and book-position accounting scope differ.");
        var instruction = new OpenLotAmortizationInstructionDto(retained.ToOpenLot(), security, securityEvidence, position.Version, asOfDate);
        return new(instruction, OpenLotAmortization.Project(instruction));
    }
}

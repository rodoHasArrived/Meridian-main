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

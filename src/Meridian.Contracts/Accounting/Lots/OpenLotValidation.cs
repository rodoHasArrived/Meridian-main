using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.FixedIncome;

namespace Meridian.Contracts.Accounting.Lots;

public static class OpenLotValidation
{
    public static void Validate(OpenLotDto lot)
    {
        ArgumentNullException.ThrowIfNull(lot);
        if (lot.TaxLotRecordId == Guid.Empty || lot.SecurityId == Guid.Empty || lot.BookPositionId == Guid.Empty
            || lot.LedgerBookId == Guid.Empty || string.IsNullOrWhiteSpace(lot.LotId))
            throw new ArgumentException("Canonical open lots require durable, Security Master, position, book, and lot identity.");
        if (lot.OriginalQuantity <= 0 || lot.OpenQuantity < 0 || lot.OpenQuantity > lot.OriginalQuantity || lot.Version < 0
            || lot.OpenTransactionCostBasis < 0 || lot.OpenFunctionalCostBasis < 0)
            throw new ArgumentException("Open-lot quantity, basis, or version is invalid.");
        var acquisition = lot.Acquisition;
        ArgumentNullException.ThrowIfNull(acquisition);
        if (!Enum.IsDefined(acquisition.QuantityBasis) || !Currency(acquisition.AcquisitionCurrency)
            || !Currency(acquisition.FunctionalCurrency) || acquisition.AcquisitionFxRateToFunctional <= 0
            || acquisition.TransactionCostBasis < 0 || acquisition.FunctionalCostBasis < 0
            || acquisition.HoldingPeriodStartDate > lot.AcquiredDate)
            throw new ArgumentException("Open lots require explicit valid acquisition currency, FX, basis, and holding-period facts.");
        if (acquisition.AcquisitionCurrency == acquisition.FunctionalCurrency && acquisition.AcquisitionFxRateToFunctional != 1m)
            throw new ArgumentException("Same-currency acquisition FX must equal one.");
        if (Math.Abs(acquisition.FunctionalCostBasis - acquisition.TransactionCostBasis * acquisition.AcquisitionFxRateToFunctional) > 0.01m)
            throw new ArgumentException("Retained acquisition basis does not reconcile to acquisition FX.");
        if (acquisition.Evidence is null || acquisition.Evidence.Count == 0
            || acquisition.Evidence.Any(e => !RetainedEvidenceIdentityValidator.IsComplete(e)))
            throw new ArgumentException("Acquisition requires complete retained evidence; missing facts cannot receive synthetic defaults.");
        if (!acquisition.Evidence.Any(e => e.SubjectType == "OpenLotAcquisition"
            && e.SubjectId == lot.TaxLotRecordId.ToString("D") && e.EffectiveDate == lot.AcquiredDate))
            throw new ArgumentException("Acquisition evidence must bind this exact durable lot and acquisition date.");
        var face = acquisition.FaceValueTerms;
        if ((acquisition.QuantityBasis == LotQuantityBasis.Face) != (face is not null))
            throw new ArgumentException("Face lots require face acquisition terms; unit lots must not carry them.");
        if (face is not null && (face.ParBasis <= 0 || face.BookedFactor <= 0 || face.BookedFactor > 1
            || !Enum.IsDefined(face.AmortizationMethod)
            || (face.AmortizationMethod == BondAmortizationMethod.ConstantYield && face.EffectiveYield is null)))
            throw new ArgumentException("Face acquisition terms must retain par basis, factor, and the selected amortization inputs.");
    }

    private static bool Currency(string value) => value is { Length: 3 } && value.All(c => c is >= 'A' and <= 'Z');
}

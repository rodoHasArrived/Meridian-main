using Meridian.Contracts.Accounting.Lots;
using Meridian.Ledger;

namespace Meridian.Storage.Ledger;

/// <summary>Production cutover guard over the locked lots of record, in durable storage units.</summary>
public static class CanonicalOpenLotDisposalGuard
{
    public static OpenLotReliefResultDto Validate(
        IReadOnlyList<LedgerTaxLotRecord> openLots,
        IReadOnlyList<LedgerTaxLotDisposalSelection> selections,
        LedgerTaxLotReliefMethod method,
        string functionalCurrency)
    {
        try
        {
            var canonical = openLots.Select(static lot => lot.ToOpenLot()).ToArray();
            if (canonical.Length == 0 || selections.Count == 0)
                throw new LedgerValidationException("Canonical disposal requires open lots and retained selections.");
            if (canonical.Any(lot => lot.Acquisition.FunctionalCurrency != functionalCurrency))
                throw new LedgerValidationException("Canonical acquisition functional currency differs from the disposal journal.");

            // Atomic journal storage supports discrete relief. Average cost also needs a governed
            // redistribution of the surviving lot bases and cannot be enabled by changing a selector.
            var canonicalMethod = method switch
            {
                LedgerTaxLotReliefMethod.Fifo => OpenLotReliefMethod.Fifo,
                LedgerTaxLotReliefMethod.Lifo => OpenLotReliefMethod.Lifo,
                LedgerTaxLotReliefMethod.Hifo => OpenLotReliefMethod.Hifo,
                LedgerTaxLotReliefMethod.SpecificId => OpenLotReliefMethod.SpecificId,
                _ => throw new LedgerValidationException("Atomic canonical disposal requires a supported discrete relief policy.")
            };
            var scale = canonical[0].Acquisition.QuantityBasis == LotQuantityBasis.Face
                ? LedgerTaxLotFaceValueTerms.LedgerLotParBasis : 1m;
            var result = new OpenLotReliefService().Select(canonical,
                selections.Sum(static selection => selection.Quantity) * scale, canonicalMethod,
                canonicalMethod == OpenLotReliefMethod.SpecificId
                    ? selections.Select(static selection => selection.TaxLotRecordId).ToArray() : null);
            if (result.Selections.Count != selections.Count || selections.Where((expected, index) =>
                result.Selections[index].TaxLotRecordId != expected.TaxLotRecordId ||
                result.Selections[index].ExpectedVersion != expected.ExpectedVersion ||
                result.Selections[index].Quantity != expected.Quantity * scale ||
                result.Selections[index].FunctionalCostBasis != expected.ExpectedCostBasis).Any())
                throw new LedgerValidationException($"Atomic disposal selections do not match the authoritative {method} canonical quantity and functional-basis relief plan.");
            return result;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or LedgerValidationException)
        {
            throw new LedgerValidationException($"Canonical open-lot disposal is blocked: {exception.Message} Resolve the open-lot backfill exception for this durable lot with reviewed acquisition evidence.");
        }
    }
}

using System.Text.Json.Serialization;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.FixedIncome;

namespace Meridian.Contracts.Accounting.Lots;

[JsonConverter(typeof(JsonStringEnumConverter<LotQuantityBasis>))]
public enum LotQuantityBasis { Units, Face }

[JsonConverter(typeof(JsonStringEnumConverter<OpenLotReliefMethod>))]
public enum OpenLotReliefMethod { Fifo, Lifo, Hifo, SpecificId, AverageCost }

public sealed record FaceValueAcquisitionTermsDto(decimal ParBasis, decimal BookedFactor,
    BondAmortizationMethod AmortizationMethod, decimal? EffectiveYield);

/// <summary>Immutable acquisition facts; FX is functional-currency units per acquisition-currency unit.</summary>
public sealed record OpenLotAcquisitionDto(
    LotQuantityBasis QuantityBasis,
    string AcquisitionCurrency,
    string FunctionalCurrency,
    decimal AcquisitionFxRateToFunctional,
    decimal TransactionCostBasis,
    decimal FunctionalCostBasis,
    DateOnly HoldingPeriodStartDate,
    FaceValueAcquisitionTermsDto? FaceValueTerms,
    IReadOnlyList<RetainedEvidenceIdentityDto> Evidence);

/// <summary>Security-identified decimal lot view over the durable ledger lot, never a second store.</summary>
public sealed record OpenLotDto(
    Guid TaxLotRecordId,
    Guid SecurityId,
    Guid BookPositionId,
    Guid LedgerBookId,
    string LotId,
    DateOnly AcquiredDate,
    decimal OriginalQuantity,
    decimal OpenQuantity,
    decimal OpenTransactionCostBasis,
    decimal OpenFunctionalCostBasis,
    long Version,
    OpenLotAcquisitionDto Acquisition);

public sealed record OpenLotReliefSelectionDto(Guid TaxLotRecordId, string LotId, long ExpectedVersion,
    decimal Quantity, decimal TransactionCostBasis, decimal FunctionalCostBasis);

public sealed record OpenLotReliefResultDto(IReadOnlyList<OpenLotReliefSelectionDto> Selections,
    decimal Quantity, decimal TransactionCostBasis, decimal FunctionalCostBasis);

public interface IOpenLotReliefService
{
    OpenLotReliefResultDto Select(IReadOnlyList<OpenLotDto> lots, decimal quantity,
        OpenLotReliefMethod method, IReadOnlyList<Guid>? specificLotIds = null);
}

using System.Text.Json.Serialization;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.FixedIncome;

namespace Meridian.Contracts.Accounting.Lots;

[JsonConverter(typeof(JsonStringEnumConverter<LotQuantityBasis>))]
public enum LotQuantityBasis { Units, Face }

[JsonConverter(typeof(JsonStringEnumConverter<OpenLotReliefMethod>))]
public enum OpenLotReliefMethod { Fifo, Lifo, Hifo, SpecificId, AverageCost }

/// <summary>EffectiveYield is the retained annual yield as a decimal: 0.05 means 5%.</summary>
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
    IReadOnlyList<RetainedEvidenceIdentityDto> Evidence)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public OpenLotCorporateActionLineageDto? CorporateActionLineage { get; init; }
}

/// <summary>
/// A governed restatement of a lot's open basis after acquisition, retained on the lot of record
/// and in its append-only mutation. Bases are stated for <see cref="OpenQuantity"/> in durable lot
/// units; later relief scales them by the remaining open quantity. The acquisition facts never
/// change, so the original basis stays provable while the open basis follows governed policy.
/// </summary>
public sealed record OpenLotBasisAdjustmentDto(
    Guid MutationBatchId,
    string Reason,
    decimal OpenQuantity,
    decimal TransactionCostBasis,
    decimal FunctionalCostBasis,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    OpenLotAmortizationInstructionDto? Amortization = null);

public static class OpenLotBasisAdjustmentReasons
{
    /// <summary>Average-cost relief restated the surviving pool at the pooled per-unit basis.</summary>
    public const string AverageCostRedistribution = "AverageCostRedistribution";
    public const string Amortization = "Amortization";
    public const string CorporateAction = "CorporateAction";

    /// <summary>Discrete relief retains the exact unrelieved current basis on its surviving lot.</summary>
    public const string DisposalRelief = "DisposalRelief";
}

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

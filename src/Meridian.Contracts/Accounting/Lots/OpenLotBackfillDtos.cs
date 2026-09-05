using Meridian.Contracts.Workstation;

namespace Meridian.Contracts.Accounting.Lots;

/// <summary>Reviewed source facts. Legacy quantities and costs retain the durable row's convention.</summary>
public sealed record OpenLotBackfillFactsDto(
    Guid LedgerBookId,
    Guid TaxLotRecordId,
    Guid SecurityId,
    long SecurityMasterVersion,
    Guid BookPositionId,
    long BookPositionVersion,
    DateOnly AcquiredDate,
    decimal LegacyOriginalQuantity,
    decimal LegacyUnitCost,
    string LegacyCurrency,
    LotQuantityBasis QuantityBasis,
    string AcquisitionCurrency,
    string FunctionalCurrency,
    decimal AcquisitionFxRateToFunctional,
    decimal TransactionCostBasis,
    decimal FunctionalCostBasis,
    DateOnly HoldingPeriodStartDate,
    decimal? OriginalFace,
    FaceValueAcquisitionTermsDto? FaceValueTerms);

/// <summary>Retains exact UTF-8 JSON source bytes before a separate human review.</summary>
public sealed record RetainOpenLotBackfillEvidenceRequest(
    Guid EvidenceRecordId, Guid LedgerBookId, Guid TaxLotRecordId,
    string SourceSystem, string SourceReference, string SourceUri,
    byte[] Content, string ContentHashSha256, string Actor);

public sealed record ReviewOpenLotBackfillEvidenceRequest(
    Guid LedgerBookId, Guid EvidenceRecordId, long ExpectedVersion,
    bool Accepted, string Actor, string Rationale,
    OperationsActionOriginDto ActionOrigin = OperationsActionOriginDto.AutomationAssistant);

public sealed record OpenLotBackfillEvidenceDto(
    Guid EvidenceRecordId, Guid LedgerBookId, Guid TaxLotRecordId,
    OpenLotBackfillFactsDto Facts, string SourceSystem, string SourceReference,
    string SourceUri, string ContentHashSha256, string RetainedBy,
    DateTimeOffset RetainedAtUtc, long Version, string ReviewStatus,
    string? ReviewedBy, DateTimeOffset? ReviewedAtUtc, string? ReviewRationale);

/// <summary>The mutation accepts an approved retained source reference, never replacement facts.</summary>
public sealed record ApplyOpenLotBackfillRequest(
    Guid LedgerBookId, Guid TaxLotRecordId, long ExpectedLotVersion,
    long ExpectedExceptionVersion, Guid EvidenceRecordId, long ExpectedEvidenceVersion,
    string IdempotencyKey, string Actor,
    OperationsActionOriginDto ActionOrigin = OperationsActionOriginDto.AutomationAssistant);

public sealed record OpenLotBackfillExceptionDto(
    Guid LedgerBookId, Guid TaxLotRecordId, string LotId, long LotVersion,
    IReadOnlyList<string> Issues, long Version, DateTimeOffset FirstObservedAtUtc,
    DateTimeOffset LastObservedAtUtc, Guid? ResolutionReceiptId);

public sealed record OpenLotBackfillReceiptDto(
    Guid ReceiptId, Guid LedgerBookId, Guid TaxLotRecordId,
    Guid EvidenceRecordId, string ContentHashSha256, string IdempotencyKey,
    string Actor, DateTimeOffset AppliedAtUtc, long PreviousLotVersion,
    long ResultingLotVersion, OpenLotDto Lot);

/// <summary>Maintenance over the authoritative lot store; unresolved exceptions have no dismissal action.</summary>
public interface IOpenLotBackfillStore
{
    Task<IReadOnlyList<OpenLotBackfillExceptionDto>> SurveyAsync(Guid ledgerBookId, CancellationToken ct = default);
    Task<IReadOnlyList<OpenLotBackfillExceptionDto>> ListExceptionsAsync(Guid ledgerBookId, CancellationToken ct = default);
    Task<OpenLotBackfillEvidenceDto> RetainEvidenceAsync(RetainOpenLotBackfillEvidenceRequest request, CancellationToken ct = default);
    Task<OpenLotBackfillEvidenceDto?> GetEvidenceAsync(Guid ledgerBookId, Guid evidenceRecordId, CancellationToken ct = default);
    Task<OpenLotBackfillEvidenceDto> ReviewEvidenceAsync(ReviewOpenLotBackfillEvidenceRequest request, CancellationToken ct = default);
    Task<OpenLotBackfillReceiptDto> ApplyAsync(ApplyOpenLotBackfillRequest request, CancellationToken ct = default);
}

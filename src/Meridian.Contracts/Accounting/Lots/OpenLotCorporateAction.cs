using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Contracts.Accounting.Lots;

/// <summary>Retained reviewed successor identity, allocation and reference authority. Quantity is units or face, never per-100 ledger units.</summary>
public sealed record OpenLotCorporateActionSuccessorDto(
    Guid TaxLotRecordId,
    string LotId,
    string AssetAccountId,
    SecurityProjectionRecord Security,
    RetainedEvidenceIdentityDto SecurityEvidence,
    Guid BookPositionId,
    long ExpectedBookPositionVersion,
    decimal Quantity,
    decimal BasisAllocationPercent,
    CorporateActionSuccessorRoleDto Role,
    IReadOnlyList<string> ReportingTags,
    RetainedEvidenceIdentityDto AcquisitionEvidence,
    string? PostingAccountPath = null);

/// <summary>A complete, independently approved full-open-lot transformation; no acquisition facts or approval are inferred.</summary>
public sealed record OpenLotCorporateActionInstructionDto(
    Guid CorporateActionId,
    CorporateActionAccountingTypeDto ActionType,
    DateOnly EffectiveDate,
    OpenLotDto ExpectedLot,
    SecurityProjectionRecord Security,
    RetainedEvidenceIdentityDto SecurityEvidence,
    long ExpectedBookPositionVersion,
    IReadOnlyList<OpenLotCorporateActionSuccessorDto> Successors,
    IReadOnlyList<CorporateActionLotMutationDto> Mutations,
    string SourceAssetAccountId);

/// <summary>Immutable origin on a successor acquisition, retained even after later relief.</summary>
public sealed record OpenLotCorporateActionLineageDto(
    Guid CorporateActionId,
    CorporateActionAccountingTypeDto ActionType,
    DateOnly EffectiveDate,
    Guid PredecessorTaxLotRecordId,
    long PredecessorVersion,
    decimal BasisAllocationPercent,
    CorporateActionSuccessorRoleDto Role,
    IReadOnlyList<string> ReportingTags);

public sealed record OpenLotCorporateActionSuccessorProjectionDto(
    OpenLotCorporateActionSuccessorDto Successor,
    decimal AcquisitionTransactionCostBasis,
    decimal AcquisitionFunctionalCostBasis,
    decimal OpenTransactionCostBasis,
    decimal OpenFunctionalCostBasis);

/// <summary>Pure conservation and evidence guard shared by governed drafting, storage and retained Reporting proof.</summary>
public static class OpenLotCorporateAction
{
    public const string ModelVersion = "canonical-lot-corporate-action-v1";

    public static IReadOnlyList<OpenLotCorporateActionSuccessorProjectionDto> Project(OpenLotCorporateActionInstructionDto instruction)
    {
        ArgumentNullException.ThrowIfNull(instruction);
        var lot = instruction.ExpectedLot;
        OpenLotValidation.Validate(lot);
        if (instruction.CorporateActionId == Guid.Empty || string.IsNullOrWhiteSpace(instruction.SourceAssetAccountId) || lot.Version <= 0 || lot.OpenQuantity <= 0m
            || lot.OpenFunctionalCostBasis <= 0m || instruction.ExpectedBookPositionVersion <= 0
            || instruction.EffectiveDate < lot.AcquiredDate)
            throw new ArgumentException("Corporate action requires a versioned positive open lot, action identity and effective date.");
        if (lot.Acquisition.CorporateActionLineage is { } origin
            && (instruction.EffectiveDate < origin.EffectiveDate || instruction.CorporateActionId == origin.CorporateActionId))
            throw new ArgumentException("A successor cannot undergo a corporate action before its retained origin or apply that same action again.");
        if (instruction.ActionType is not (CorporateActionAccountingTypeDto.StockSplit
            or CorporateActionAccountingTypeDto.ReverseStockSplit or CorporateActionAccountingTypeDto.MergerStock
            or CorporateActionAccountingTypeDto.AdvanceRefunding))
            throw new ArgumentException("Only basis-preserving stock splits, stock mergers and advance refunding are supported; cash and other treatments require a separate reviewed workflow.");
        ValidateSecurity(instruction.Security, instruction.SecurityEvidence, lot.SecurityId, lot.Acquisition.AcquisitionCurrency, instruction.EffectiveDate);
        var successors = instruction.Successors;
        if (successors is null || successors.Count == 0 || successors.Any(s => s is null || s.Security is null || s.SecurityEvidence is null || s.AcquisitionEvidence is null || s.TaxLotRecordId == Guid.Empty
            || s.TaxLotRecordId == lot.TaxLotRecordId || string.IsNullOrWhiteSpace(s.LotId)
            || string.IsNullOrWhiteSpace(s.AssetAccountId) || s.BookPositionId == Guid.Empty || s.ExpectedBookPositionVersion <= 0 || s.Quantity <= 0m
            || s.BasisAllocationPercent <= 0m || s.BasisAllocationPercent > 100m || !Enum.IsDefined(s.Role)
            || s.ReportingTags is null)
            || successors.Select(s => s.TaxLotRecordId).Distinct().Count() != successors.Count
            || successors.Select(s => s.LotId).Distinct(StringComparer.Ordinal).Count() != successors.Count
            || successors.Sum(s => s.BasisAllocationPercent) != 100m)
            throw new ArgumentException("Corporate action requires distinct new successor lots and a complete positive 100 percent basis allocation.");
        foreach (var successor in successors)
        {
            ValidateSecurity(successor.Security, successor.SecurityEvidence, successor.Security.SecurityId, lot.Acquisition.AcquisitionCurrency, instruction.EffectiveDate);
            if (!RetainedEvidenceIdentityValidator.IsComplete(successor.AcquisitionEvidence)
                || successor.AcquisitionEvidence.SubjectType != "OpenLotAcquisition"
                || successor.AcquisitionEvidence.SubjectId != successor.TaxLotRecordId.ToString("D")
                || successor.AcquisitionEvidence.EffectiveDate != lot.AcquiredDate)
                throw new ArgumentException("Each successor requires reviewed retained acquisition evidence bound to its new lot identity and inherited acquisition date.");
        }
        if (successors.Any(s => instruction.ActionType is CorporateActionAccountingTypeDto.StockSplit or CorporateActionAccountingTypeDto.ReverseStockSplit
                ? s.Role != CorporateActionSuccessorRoleDto.Successor
                : instruction.ActionType == CorporateActionAccountingTypeDto.MergerStock
                    && s.Role is not (CorporateActionSuccessorRoleDto.Successor or CorporateActionSuccessorRoleDto.Acquirer)))
            throw new ArgumentException("Successor role does not match the supported corporate-action treatment.");
        var split = instruction.ActionType is CorporateActionAccountingTypeDto.StockSplit or CorporateActionAccountingTypeDto.ReverseStockSplit;
        if (split && (lot.Acquisition.QuantityBasis != LotQuantityBasis.Units || successors.Count != 1
            || (instruction.ActionType == CorporateActionAccountingTypeDto.StockSplit
                ? successors[0].Quantity <= lot.OpenQuantity : successors[0].Quantity >= lot.OpenQuantity)
            || decimal.Truncate(successors[0].Quantity) != successors[0].Quantity))
            throw new ArgumentException("A supported split requires one whole-unit successor and an explicit forward or reverse ratio; cash-in-lieu is unsupported.");
        if (instruction.ActionType == CorporateActionAccountingTypeDto.MergerStock
            && (lot.Acquisition.QuantityBasis != LotQuantityBasis.Units || successors.Count != 1))
            throw new ArgumentException("A supported stock merger requires one unit successor and carried holding period.");
        if (instruction.ActionType == CorporateActionAccountingTypeDto.AdvanceRefunding)
        {
            if (lot.Acquisition.QuantityBasis != LotQuantityBasis.Face || successors.Count != 2
                || successors.Count(s => s.Role == CorporateActionSuccessorRoleDto.Refunded) != 1
                || successors.Count(s => s.Role == CorporateActionSuccessorRoleDto.Unrefunded) != 1
                || successors.Select(s => s.Security.SecurityId).Distinct().Count() != 2
                || successors.Sum(s => s.Quantity) != lot.OpenQuantity
                || successors.Any(s => s.Quantity != lot.OpenQuantity * s.BasisAllocationPercent / 100m))
                throw new ArgumentException("Advance refunding requires refunded and unrefunded face successors conserving face and proportional basis.");
        }
        foreach (var successor in successors)
        {
            var requiredTags = instruction.ActionType == CorporateActionAccountingTypeDto.AdvanceRefunding
                && successor.Role == CorporateActionSuccessorRoleDto.Refunded ? new[] { "ScheduleD" } : [];
            if (!successor.ReportingTags.SequenceEqual(requiredTags, StringComparer.Ordinal))
                throw new ArgumentException("Only the refunded advance-refunding successor may carry ScheduleD tracking.");
        }
        if (instruction.Mutations is null || instruction.Mutations.Any(mutation => mutation is null))
            throw new ArgumentException("Corporate action requires a non-null authoritative mutation plan.");
        var blockers = CorporateActionLotMutationPlanValidator.Validate(instruction.Mutations);
        if (blockers.Count != 0 || instruction.Mutations.Count != successors.Count)
            throw new ArgumentException("Corporate action requires a complete authoritative source-to-successor mutation plan.");
        var result = new List<OpenLotCorporateActionSuccessorProjectionDto>();
        var acquisitionTransaction = lot.Acquisition.TransactionCostBasis * lot.OpenQuantity / lot.OriginalQuantity;
        var acquisitionFunctional = lot.Acquisition.FunctionalCostBasis * lot.OpenQuantity / lot.OriginalQuantity;
        decimal allocatedAcquisitionTransaction = 0m, allocatedAcquisitionFunctional = 0m, allocatedTransaction = 0m, allocatedFunctional = 0m, allocatedQuantity = 0m;
        for (var index = 0; index < successors.Count; index++)
        {
            var successor = successors[index];
            var last = index == successors.Count - 1;
            var fraction = successor.BasisAllocationPercent / 100m;
            var sourceQuantity = last ? lot.OpenQuantity - allocatedQuantity : Round(lot.OpenQuantity * fraction);
            var acquisitionTransactionPart = last ? acquisitionTransaction - allocatedAcquisitionTransaction : Round(acquisitionTransaction * fraction);
            var acquisitionFunctionalPart = last ? acquisitionFunctional - allocatedAcquisitionFunctional : Round(acquisitionFunctional * fraction);
            var transaction = last ? lot.OpenTransactionCostBasis - allocatedTransaction : Round(lot.OpenTransactionCostBasis * fraction);
            var functional = last ? lot.OpenFunctionalCostBasis - allocatedFunctional : Round(lot.OpenFunctionalCostBasis * fraction);
            var mutation = instruction.Mutations.SingleOrDefault(m => m.TargetLotId == successor.TaxLotRecordId);
            if (mutation is null || mutation.Kind is not (CorporateActionLotMutationKindDto.CarryOver or CorporateActionLotMutationKindDto.Allocate)
                || mutation.SecurityId != lot.SecurityId || mutation.SourceLotId != lot.TaxLotRecordId
                || mutation.ExpectedSourceLotVersion != lot.Version
                || mutation.SourceBefore != new CorporateActionLotStateSnapshotDto(lot.OpenQuantity, lot.OpenFunctionalCostBasis, lot.OpenTransactionCostBasis)
                || mutation.SourceAfter != new CorporateActionLotStateSnapshotDto(0m, 0m, 0m)
                || mutation.SourceQuantity != sourceQuantity || mutation.SourceCarryingAmount != functional || mutation.SourceBasisAmount != transaction
                || mutation.TargetSecurityId != successor.Security.SecurityId || mutation.TargetOperation != CorporateActionLotTargetOperationDto.Create
                || mutation.Quantity != successor.Quantity || mutation.CarryingAmount != functional || mutation.BasisAmount != transaction
                || mutation.TargetAfter != new CorporateActionLotStateSnapshotDto(successor.Quantity, functional, transaction)
                || (mutation.AllocationPercent ?? (successors.Count == 1 ? 1m : -1m)) != fraction
                || mutation.HoldingPeriodTreatment != CorporateActionHoldingPeriodTreatmentDto.CarryOver
                || mutation.LinkedCaseId is not null || !mutation.ReportingTags.SequenceEqual(successor.ReportingTags, StringComparer.Ordinal))
                throw new ArgumentException("The reviewed corporate-action plan must close the exact predecessor and create each exact successor while carrying basis and holding period.");
            if (functional <= 0m || transaction < 0m || !Exact(transaction) || !Exact(functional) || !Exact(successor.Quantity)
                || !Exact(acquisitionTransactionPart) || !Exact(acquisitionFunctionalPart)
                || Math.Abs(functional - transaction * lot.Acquisition.AcquisitionFxRateToFunctional) > 0.000000000001m)
                throw new ArgumentException("Successor quantity and bases must be representable at twelve decimals and conserve retained acquisition FX.");
            result.Add(new(successor, acquisitionTransactionPart, acquisitionFunctionalPart, transaction, functional));
            allocatedQuantity += sourceQuantity;
            allocatedAcquisitionTransaction += acquisitionTransactionPart;
            allocatedAcquisitionFunctional += acquisitionFunctionalPart;
            allocatedTransaction += transaction;
            allocatedFunctional += functional;
        }
        return result;
    }

    public static IReadOnlyList<RetainedEvidenceIdentityDto> Evidence(OpenLotCorporateActionInstructionDto instruction)
        => instruction.ExpectedLot.Acquisition.Evidence.Concat([instruction.SecurityEvidence])
            .Concat(instruction.Successors.SelectMany(s => new[] { s.SecurityEvidence, s.AcquisitionEvidence })).Distinct().ToArray();

    public static string Fingerprint(OpenLotCorporateActionInstructionDto instruction)
    {
        ArgumentNullException.ThrowIfNull(instruction);
        using var stream = new MemoryStream();
        using (var writer = new System.Text.Json.Utf8JsonWriter(stream))
            OpenLotAmortization.WriteOrderedJson(writer, System.Text.Json.JsonSerializer.SerializeToElement(instruction));
        return Meridian.Contracts.Integrity.Sha256Digest.Compute(stream.ToArray());
    }

    private static bool Exact(decimal value) => Round(value) == value;
    private static decimal Round(decimal value) => decimal.Round(value, 12, MidpointRounding.ToEven);

    private static void ValidateSecurity(SecurityProjectionRecord security, RetainedEvidenceIdentityDto evidence,
        Guid securityId, string currency, DateOnly effectiveDate)
    {
        ArgumentNullException.ThrowIfNull(security);
        ArgumentNullException.ThrowIfNull(evidence);
        if (security.SecurityId == Guid.Empty || security.SecurityId != securityId || security.Version <= 0
            || security.Status != SecurityStatusDto.Active || security.Currency != currency
            || DateOnly.FromDateTime(security.EffectiveFrom.UtcDateTime) > effectiveDate
            || (security.EffectiveTo is { } end && DateOnly.FromDateTime(end.UtcDateTime) < effectiveDate)
            || !RetainedEvidenceIdentityValidator.IsComplete(evidence) || evidence.SubjectType != "SecurityMasterProjection"
            || evidence.SubjectId != securityId.ToString("D") || evidence.EvidenceVersion != security.Version
            || evidence.EffectiveDate > effectiveDate
            || !Meridian.Contracts.Integrity.Sha256Digest.FixedEquals(evidence.ContentHashSha256, OpenLotAmortization.SecurityHash(security)))
            throw new ArgumentException("Corporate action requires active effective hash-bound Security Master evidence for every predecessor and successor.");
    }
}

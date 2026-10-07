using System.Text.Json.Serialization;
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

/// <summary>One reviewed predecessor and its distinct successors within a corporate-action batch.</summary>
public sealed record OpenLotCorporateActionPredecessorDto(
    OpenLotDto ExpectedLot,
    IReadOnlyList<OpenLotCorporateActionSuccessorDto> Successors,
    string SourceAssetAccountId);

/// <summary>A complete reviewed transformation of the affected lots in one source position.</summary>
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
    string SourceAssetAccountId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<OpenLotCorporateActionPredecessorDto>? AdditionalPredecessors = null);

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
    decimal OpenFunctionalCostBasis)
{
    public Guid PredecessorTaxLotRecordId { get; init; }
}

/// <summary>Pure conservation and evidence guard shared by governed drafting, storage and retained Reporting proof.</summary>
public static class OpenLotCorporateAction
{
    public const string ModelVersion = "canonical-lot-corporate-action-v1";

    /// <summary>Retains reviewed order without changing the serialized shape of single-lot instructions.</summary>
    public static IReadOnlyList<OpenLotCorporateActionPredecessorDto> Groups(OpenLotCorporateActionInstructionDto instruction)
    {
        ArgumentNullException.ThrowIfNull(instruction);
        var groups = new List<OpenLotCorporateActionPredecessorDto>
        {
            new(instruction.ExpectedLot, instruction.Successors, instruction.SourceAssetAccountId)
        };
        if (instruction.AdditionalPredecessors is { } additional)
            groups.AddRange(additional);
        if (groups.Any(group => group is null || group.ExpectedLot is null || group.Successors is null))
            throw new ArgumentException("Corporate action requires complete predecessor and successor groups.", nameof(instruction));
        return groups;
    }

    public static IReadOnlyList<OpenLotCorporateActionSuccessorProjectionDto> Project(OpenLotCorporateActionInstructionDto instruction)
    {
        ArgumentNullException.ThrowIfNull(instruction);
        try
        {
            return ProjectBatch(instruction);
        }
        catch (OverflowException exception)
        {
            throw new ArgumentException("Corporate-action quantities, bases, allocations or FX exceed the supported decimal range.", nameof(instruction), exception);
        }
    }

    private static IReadOnlyList<OpenLotCorporateActionSuccessorProjectionDto> ProjectBatch(OpenLotCorporateActionInstructionDto instruction)
    {
        var groups = Groups(instruction);
        var first = groups[0].ExpectedLot;
        foreach (var group in groups)
            OpenLotValidation.Validate(group.ExpectedLot);
        if (groups.Select(group => group.ExpectedLot.TaxLotRecordId).Distinct().Count() != groups.Count
            || groups.Any(group => group.ExpectedLot.LedgerBookId != first.LedgerBookId
                || group.ExpectedLot.SecurityId != first.SecurityId || group.ExpectedLot.BookPositionId != first.BookPositionId
                || group.ExpectedLot.Acquisition.QuantityBasis != first.Acquisition.QuantityBasis
                || group.ExpectedLot.Acquisition.AcquisitionCurrency != first.Acquisition.AcquisitionCurrency
                || group.ExpectedLot.Acquisition.FunctionalCurrency != first.Acquisition.FunctionalCurrency))
            throw new ArgumentException("All distinct predecessor lots must belong to the same book, security, source position and currency scope.");
        var successors = groups.SelectMany(group => group.Successors).ToArray();
        if (successors.Any(successor => successor is null)
            || successors.Select(successor => successor.TaxLotRecordId).Distinct().Count() != successors.Length
            || successors.Select(successor => successor.LotId).Distinct(StringComparer.Ordinal).Count() != successors.Length
            || successors.Any(successor => groups.Any(group => group.ExpectedLot.TaxLotRecordId == successor.TaxLotRecordId)))
            throw new ArgumentException("Successor lot identities must be globally distinct from every predecessor and successor in the action.");
        if (instruction.Mutations is null || instruction.Mutations.Any(mutation => mutation is null)
            || instruction.Mutations.Count != successors.Length
            || CorporateActionLotMutationPlanValidator.Validate(instruction.Mutations).Count != 0)
            throw new ArgumentException("Corporate action requires a complete authoritative source-to-successor mutation plan.");
        var result = groups.SelectMany(group => ProjectGroup(instruction, group)).ToArray();
        // The event amount and every consumer's debit/credit totals must fit before review.
        if (groups.Sum(group => group.ExpectedLot.OpenFunctionalCostBasis) != result.Sum(target => target.OpenFunctionalCostBasis)
            || groups.Sum(group => group.ExpectedLot.OpenTransactionCostBasis) != result.Sum(target => target.OpenTransactionCostBasis))
            throw new ArgumentException("The corporate-action batch must conserve both journal currency totals.");
        return result;
    }

    private static IReadOnlyList<OpenLotCorporateActionSuccessorProjectionDto> ProjectGroup(
        OpenLotCorporateActionInstructionDto instruction, OpenLotCorporateActionPredecessorDto group)
    {
        var lot = group.ExpectedLot;
        OpenLotValidation.Validate(lot);
        if (instruction.CorporateActionId == Guid.Empty || string.IsNullOrWhiteSpace(group.SourceAssetAccountId)
            || lot.Version <= 0 || lot.Version == long.MaxValue || lot.OpenQuantity <= 0m
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
        ValidatePrecision(lot);
        var successors = group.Successors;
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
        var mutations = instruction.Mutations.Where(mutation => mutation.SourceLotId == lot.TaxLotRecordId).ToArray();
        if (mutations.Length != successors.Count)
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
            var transaction = last ? lot.OpenTransactionCostBasis - allocatedTransaction : RoundJournal(lot.OpenTransactionCostBasis * fraction);
            var functional = last ? lot.OpenFunctionalCostBasis - allocatedFunctional : RoundJournal(lot.OpenFunctionalCostBasis * fraction);
            var matches = mutations.Where(mutation => mutation.TargetLotId == successor.TaxLotRecordId).ToArray();
            var mutation = matches.Length == 1 ? matches[0] : null;
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
            if (functional <= 0m || transaction <= 0m || !ExactLotNumeric(successor.Quantity)
                || !Exact(acquisitionTransactionPart) || !Exact(acquisitionFunctionalPart)
                || Math.Abs(acquisitionFunctionalPart - acquisitionTransactionPart * lot.Acquisition.AcquisitionFxRateToFunctional) > 0.000000000001m
                || Math.Abs(functional - transaction * lot.Acquisition.AcquisitionFxRateToFunctional) > 0.000000000001m)
                throw new ArgumentException("Successor quantity and bases must be representable at twelve decimals and conserve retained acquisition FX.");
            var durableQuantity = successor.Quantity / (lot.Acquisition.QuantityBasis == LotQuantityBasis.Face ? 100m : 1m);
            var unitCost = acquisitionFunctionalPart / durableQuantity;
            if (!ExactLotNumeric(durableQuantity) || !ExactLotNumeric(unitCost) || unitCost * durableQuantity != acquisitionFunctionalPart)
                throw new ArgumentException("Successor acquisition quantity and unit cost must preserve original basis exactly at twelve decimals within the durable numeric range.");
            result.Add(new(successor, acquisitionTransactionPart, acquisitionFunctionalPart, transaction, functional)
            {
                PredecessorTaxLotRecordId = lot.TaxLotRecordId
            });
            allocatedQuantity += sourceQuantity;
            allocatedAcquisitionTransaction += acquisitionTransactionPart;
            allocatedAcquisitionFunctional += acquisitionFunctionalPart;
            allocatedTransaction += transaction;
            allocatedFunctional += functional;
        }
        return result;
    }

    public static IReadOnlyList<RetainedEvidenceIdentityDto> Evidence(OpenLotCorporateActionInstructionDto instruction)
        => Groups(instruction).SelectMany(group => group.ExpectedLot.Acquisition.Evidence).Concat([instruction.SecurityEvidence])
            .Concat(Groups(instruction).SelectMany(group => group.Successors)
                .SelectMany(successor => new[] { successor.SecurityEvidence, successor.AcquisitionEvidence })).Distinct().ToArray();

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
    private static decimal RoundJournal(decimal value) => decimal.Round(value, 10, MidpointRounding.ToEven);
    // Lot columns and mutation cost_basis are numeric(38,12); journal legs and FX are numeric(38,10).
    private static bool ExactLotNumeric(decimal value) => Exact(value) && Math.Abs(value) < 100000000000000000000000000m;
    private static bool ExactJournalNumeric(decimal value) => RoundJournal(value) == value && Math.Abs(value) < 10000000000000000000000000000m;

    private static void ValidatePrecision(OpenLotDto lot)
    {
        var acquisition = lot.Acquisition;
        if (!ExactLotNumeric(lot.OriginalQuantity) || !ExactLotNumeric(lot.OpenQuantity)
            || !Exact(acquisition.TransactionCostBasis) || !Exact(acquisition.FunctionalCostBasis))
            throw new ArgumentException("Predecessor quantities and original acquisition bases must be exact at twelve decimals within their durable numeric range.");
        if (!ExactJournalNumeric(lot.OpenTransactionCostBasis)
            || !ExactJournalNumeric(lot.OpenFunctionalCostBasis) || !ExactLotNumeric(lot.OpenFunctionalCostBasis)
            || !ExactJournalNumeric(acquisition.AcquisitionFxRateToFunctional))
            throw new ArgumentException("Current transaction and functional basis and acquisition FX must be exact at ten journal decimals and within durable numeric ranges before review.");
        if (Math.Abs(acquisition.FunctionalCostBasis - acquisition.TransactionCostBasis * acquisition.AcquisitionFxRateToFunctional) > 0.000000000001m
            || Math.Abs(lot.OpenFunctionalCostBasis - lot.OpenTransactionCostBasis * acquisition.AcquisitionFxRateToFunctional) > 0.000000000001m)
            throw new ArgumentException("Original and current predecessor bases must conserve retained acquisition FX.");
    }

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

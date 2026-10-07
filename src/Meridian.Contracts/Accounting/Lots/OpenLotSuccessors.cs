using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Integrity;

namespace Meridian.Contracts.Accounting.Lots;

/// <summary>Reviewed successor creation; persistence locks its Security Master and position again.</summary>
public sealed record OpenLotSuccessorTargetDto(
    OpenLotDto Lot,
    long ExpectedBookPositionVersion,
    long ExpectedSecurityVersion,
    string ExpectedSecurityHash);

/// <summary>
/// One fully relieved predecessor and its create-only successors. The original projection remains
/// part of the instruction, including its reviewed allocation, dependencies and reporting tags.
/// </summary>
public sealed record OpenLotSuccessorInstructionDto(
    CorporateActionAccountingProjectionDto Projection,
    OpenLotDto ExpectedLot,
    long ExpectedSecurityVersion,
    IReadOnlyList<OpenLotSuccessorTargetDto> Successors,
    string ExpectedSecurityHash);

/// <summary>Immutable successor origin; later disposal/basis adjustment cannot replace this lineage.</summary>
public sealed record OpenLotCorporateActionLineageDto(
    Guid CorporateActionId,
    CorporateActionAccountingTypeDto ActionType,
    DateOnly EffectiveDate,
    Guid PredecessorTaxLotRecordId,
    long PredecessorVersion,
    decimal BasisAllocationPercent,
    CorporateActionSuccessorRoleDto Role,
    IReadOnlyList<string> ReportingTags)
{
    /// <summary>The source action stays stable when its reviewed case/version produces another economic event.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Guid? SourceCorporateActionId { get; init; }
}

/// <summary>Bounded cashless exchange, whole-unit split, stock merger and advance-refunding conservation rules.</summary>
public static class OpenLotSuccessors
{
    public const string JournalFingerprintTag = "openLotSuccessorInstructionFingerprint";

    public static bool IsSupported(CorporateActionAccountingTypeDto action)
        => action is CorporateActionAccountingTypeDto.RegS144AExchange or CorporateActionAccountingTypeDto.AdvanceRefunding
            or CorporateActionAccountingTypeDto.StockSplit or CorporateActionAccountingTypeDto.ReverseStockSplit
            or CorporateActionAccountingTypeDto.MergerStock;

    public static OpenLotCorporateActionLineageDto ExpectedLineage(OpenLotSuccessorInstructionDto instruction, OpenLotDto target)
    {
        var mutation = instruction.Projection.LotMutations!.Mutations.Single(item => item.TargetLotId == target.TaxLotRecordId);
        var operation = instruction.Projection.Recipe.Single(item => item.Kind == CorporateActionEconomicOperationKindDto.ExchangeIn
            && item.SecurityId == target.SecurityId);
        return new OpenLotCorporateActionLineageDto(instruction.Projection.EconomicEvent!.EventId, instruction.Projection.Treatment.ActionType,
            instruction.Projection.EconomicEvent.EffectiveDate, instruction.ExpectedLot.TaxLotRecordId,
            instruction.ExpectedLot.Version, (mutation.AllocationPercent ?? 1m) * 100m,
            operation.SuccessorRole ?? CorporateActionSuccessorRoleDto.Successor, mutation.ReportingTags)
        { SourceCorporateActionId = ResolveSourceCorporateActionId(instruction.Projection) };
    }

    /// <summary>Add the reviewed immutable origin without changing the approved instruction or its replay fingerprint.</summary>
    public static OpenLotDto WithLineage(OpenLotSuccessorInstructionDto instruction, OpenLotDto target)
        => target with { Acquisition = target.Acquisition with { CorporateActionLineage = ExpectedLineage(instruction, target) } };

    public static void Validate(OpenLotSuccessorInstructionDto instruction)
    {
        ArgumentNullException.ThrowIfNull(instruction);
        var projection = instruction.Projection;
        ArgumentNullException.ThrowIfNull(projection);
        ArgumentNullException.ThrowIfNull(projection.Treatment);
        ArgumentNullException.ThrowIfNull(instruction.Successors);
        foreach (var target in instruction.Successors)
        {
            ArgumentNullException.ThrowIfNull(target);
            ArgumentNullException.ThrowIfNull(target.Lot);
        }
        var source = instruction.ExpectedLot;
        OpenLotValidation.Validate(source);
        var action = projection.Treatment.ActionType;
        var refunding = action == CorporateActionAccountingTypeDto.AdvanceRefunding;
        var split = action is CorporateActionAccountingTypeDto.StockSplit or CorporateActionAccountingTypeDto.ReverseStockSplit;
        var merger = action == CorporateActionAccountingTypeDto.MergerStock;
        Require(IsSupported(action), "Only cashless exchange, whole-unit splits, stock merger and advance-refunding successor posting is supported.");
        Require(projection.CanPreparePostingCandidate && projection.EconomicEvent is not null
            && projection.ProjectionLineage is not null && projection.AccountingScope is not null
            && projection.CaseId != Guid.Empty && projection.CaseVersion > 0
            && projection.PolicyDecisionId != Guid.Empty && projection.PolicyDecisionVersion > 0
            && projection.PositionSnapshotId != Guid.Empty && projection.LotSnapshotId != Guid.Empty
            && projection.LotSnapshotVersion > 0 && Sha256Digest.IsCanonical(projection.ProjectionInputHash)
            && Sha256Digest.IsCanonical(projection.PostingIntentHash),
            "Successor posting requires a complete reviewed authoritative corporate-action projection.");
        var mutations = projection.LotMutations!;
        var economicEvent = projection.EconomicEvent!;
        var sourceCorporateActionId = ResolveSourceCorporateActionId(projection);
        Require(source.Version > 0 && source.OpenQuantity > 0 && source.OpenFunctionalCostBasis > 0
            && instruction.ExpectedSecurityVersion > 0 && Sha256Digest.IsCanonical(instruction.ExpectedSecurityHash)
            && mutations.PositionId == source.BookPositionId && mutations.ExpectedPositionVersion > 0
            && projection.AccountingScope!.LedgerBookId == source.LedgerBookId
            && economicEvent.SecurityId == source.SecurityId && economicEvent.BookPositionId == source.BookPositionId
            && economicEvent.EffectiveDate >= source.AcquiredDate
            && economicEvent.EventType == AssetAccountingEventTypeNames.For(AssetAccountingEventKindDto.CorporateAction)
            && projection.EventAmount == source.OpenFunctionalCostBasis
            && projection.PostingSet!.Currency == source.Acquisition.FunctionalCurrency,
            "Successor projection must bind the exact versioned predecessor and functional carrying basis.");
        if (source.Acquisition.CorporateActionLineage is { } origin)
            Require(economicEvent.EffectiveDate >= origin.EffectiveDate && economicEvent.EventId != origin.CorporateActionId
                && sourceCorporateActionId != origin.SourceCorporateActionId,
                "A successor cannot undergo a corporate action before its immutable origin or repeat the same action.");
        var targets = instruction.Successors;
        Require(targets.Count == (refunding ? 2 : 1)
            && mutations.Mutations.Count == targets.Count,
            "The supported exchange, split or merger requires one successor; advance refunding requires exactly two.");
        Require(targets.Select(static target => target.Lot.TaxLotRecordId).Distinct().Count() == targets.Count
            && targets.Select(static target => target.Lot.SecurityId).Distinct().Count() == targets.Count
            && targets.Select(static target => target.Lot.BookPositionId).Distinct().Count() == targets.Count,
            "Successors require distinct durable lot, Security Master and book-position identities.");
        Require(projection.Recipe.Count == targets.Count + 1
            && projection.Recipe.Count(operation => operation.Kind == CorporateActionEconomicOperationKindDto.ExchangeOut
                && operation.SecurityId == source.SecurityId && operation.Quantity == source.OpenQuantity
                && operation.SuccessorRole is null) == 1
            && projection.Recipe.Count(operation => operation.Kind == CorporateActionEconomicOperationKindDto.ExchangeIn) == targets.Count,
            "Successor posting supports only the complete cashless exchange recipe.");
        Require(projection.PostingSet!.Components.Count == 2
            && projection.PostingSet.Components.All(component => component.Amount == source.OpenFunctionalCostBasis
                && component.Currency == source.Acquisition.FunctionalCurrency)
            && projection.PostingSet.Components.Count(component => component.Kind == CorporateActionPostingComponentKindDto.CarryingValueRelief) == 1
            && projection.PostingSet.Components.Count(component => component.Kind == CorporateActionPostingComponentKindDto.PurchaseCost) == 1,
            "Successor posting requires exact carrying-value relief and purchase-cost components without cash or recognition.");
        if (refunding)
            Require(projection.Recipe.Count(operation => operation.Kind == CorporateActionEconomicOperationKindDto.ExchangeIn
                    && operation.SuccessorRole == CorporateActionSuccessorRoleDto.Refunded) == 1
                && projection.Recipe.Count(operation => operation.Kind == CorporateActionEconomicOperationKindDto.ExchangeIn
                    && operation.SuccessorRole == CorporateActionSuccessorRoleDto.Unrefunded) == 1
                && source.Acquisition.QuantityBasis == LotQuantityBasis.Face
                && targets.Sum(target => target.Lot.OpenQuantity) == source.OpenQuantity,
                "Advance refunding requires refunded and unrefunded successors conserving the predecessor's face.");

        if (split)
            Require(source.Acquisition.QuantityBasis == LotQuantityBasis.Units
                && targets[0].Lot.SecurityId == source.SecurityId
                && decimal.Truncate(targets[0].Lot.OpenQuantity) == targets[0].Lot.OpenQuantity
                && (action == CorporateActionAccountingTypeDto.StockSplit
                    ? targets[0].Lot.OpenQuantity > source.OpenQuantity : targets[0].Lot.OpenQuantity < source.OpenQuantity),
                "A canonical split requires a same-security whole-unit successor with the reviewed forward or reverse ratio; cash-in-lieu is unsupported.");
        if (merger)
            Require(source.Acquisition.QuantityBasis == LotQuantityBasis.Units,
                "A supported stock merger requires a unit predecessor and one carried-basis successor.");

        var sourceBefore = new CorporateActionLotStateSnapshotDto(source.OpenQuantity,
            source.OpenFunctionalCostBasis, source.OpenTransactionCostBasis);
        var zero = new CorporateActionLotStateSnapshotDto(0m, 0m, 0m);
        var sourceAcquisition = source.Acquisition;
        var originalTransaction = sourceAcquisition.TransactionCostBasis * source.OpenQuantity / source.OriginalQuantity;
        var originalFunctional = sourceAcquisition.FunctionalCostBasis * source.OpenQuantity / source.OriginalQuantity;
        decimal assignedTransaction = 0m, assignedFunctional = 0m, assignedOriginalTransaction = 0m, assignedOriginalFunctional = 0m;
        for (var index = 0; index < mutations.Mutations.Count; index++)
        {
            var mutation = mutations.Mutations[index];
            var target = targets.SingleOrDefault(candidate => candidate.Lot.TaxLotRecordId == mutation.TargetLotId);
            Require(target is not null, "Every projected target must resolve to one reviewed successor.");
            var lot = target!.Lot;
            OpenLotValidation.Validate(lot);
            var acquisition = lot.Acquisition;
            var weight = mutation.AllocationPercent ?? 1m;
            Require(weight > 0m && weight <= 1m && (refunding || weight == 1m),
                "Successor basis allocations must be positive and complete.");
            var last = index == mutations.Mutations.Count - 1;
            var transaction = last ? source.OpenTransactionCostBasis - assignedTransaction
                : Allocate(source.OpenTransactionCostBasis, weight, sourceAcquisition.AcquisitionCurrency);
            var functional = last ? source.OpenFunctionalCostBasis - assignedFunctional
                : Allocate(source.OpenFunctionalCostBasis, weight, sourceAcquisition.FunctionalCurrency);
            var originalTx = last ? originalTransaction - assignedOriginalTransaction
                : Allocate(originalTransaction, weight, sourceAcquisition.AcquisitionCurrency);
            var originalFx = last ? originalFunctional - assignedOriginalFunctional
                : Allocate(originalFunctional, weight, sourceAcquisition.FunctionalCurrency);
            Require(mutation.Kind == (refunding ? CorporateActionLotMutationKindDto.Allocate : CorporateActionLotMutationKindDto.CarryOver)
                && mutation.SourceLotId == source.TaxLotRecordId && mutation.ExpectedSourceLotVersion == source.Version
                && mutation.SecurityId == source.SecurityId && mutation.SourceBefore == sourceBefore
                && (mutation.SourceAfter is null || mutation.SourceAfter == zero)
                && mutation.TargetOperation == CorporateActionLotTargetOperationDto.Create
                && mutation.ExpectedTargetLotVersion is null && mutation.TargetBefore is null
                && mutation.TargetSecurityId == lot.SecurityId && (split ? lot.SecurityId == source.SecurityId : lot.SecurityId != source.SecurityId)
                && lot.TaxLotRecordId != source.TaxLotRecordId && lot.LotId != source.LotId
                && (split || lot.BookPositionId != source.BookPositionId) && lot.LedgerBookId == source.LedgerBookId
                && lot.Version == 1 && lot.OriginalQuantity == lot.OpenQuantity && lot.OpenQuantity > 0
                && target.ExpectedBookPositionVersion > 0 && target.ExpectedSecurityVersion > 0
                && Sha256Digest.IsCanonical(target.ExpectedSecurityHash)
                && mutation.Quantity == lot.OpenQuantity
                && mutation.CarryingAmount == functional && mutation.BasisAmount == transaction
                && mutation.SourceCarryingAmount == functional && mutation.SourceBasisAmount == transaction
                && mutation.TargetAfter == new CorporateActionLotStateSnapshotDto(lot.OpenQuantity, functional, transaction)
                && mutation.HoldingPeriodTreatment == CorporateActionHoldingPeriodTreatmentDto.CarryOver,
                "Successor identities and before/after allocations must exactly match the authoritative create-only plan.");
            Require(lot.OpenTransactionCostBasis == transaction && lot.OpenFunctionalCostBasis == functional
                && acquisition.TransactionCostBasis == originalTx && acquisition.FunctionalCostBasis == originalFx
                && acquisition.AcquisitionCurrency == sourceAcquisition.AcquisitionCurrency
                && acquisition.FunctionalCurrency == sourceAcquisition.FunctionalCurrency
                && acquisition.AcquisitionFxRateToFunctional == sourceAcquisition.AcquisitionFxRateToFunctional
                && acquisition.QuantityBasis == sourceAcquisition.QuantityBasis
                && acquisition.FaceValueTerms == sourceAcquisition.FaceValueTerms
                && lot.AcquiredDate == source.AcquiredDate
                && acquisition.HoldingPeriodStartDate == sourceAcquisition.HoldingPeriodStartDate
                && sourceAcquisition.Evidence.All(acquisition.Evidence.Contains),
                "Successors must preserve acquisition FX, dates, face terms and evidence while allocating both bases exactly.");
            var operation = projection.Recipe.SingleOrDefault(operation => operation.Kind == CorporateActionEconomicOperationKindDto.ExchangeIn
                && operation.SecurityId == lot.SecurityId);
            Require(operation is not null && operation.Quantity == lot.OpenQuantity,
                "Successor quantity and identity must match the retained exchange recipe.");
            if (split || merger)
                Require(mutation.LinkedCaseId is null && (split
                    ? operation!.SuccessorRole == CorporateActionSuccessorRoleDto.Successor
                    : operation!.SuccessorRole is CorporateActionSuccessorRoleDto.Successor or CorporateActionSuccessorRoleDto.Acquirer),
                    "Split/merger successor roles must match the cashless reviewed treatment without a linked cash or correction case.");
            if (acquisition.CorporateActionLineage is { } targetOrigin)
                Require(JsonElement.DeepEquals(JsonSerializer.SerializeToElement(targetOrigin),
                    JsonSerializer.SerializeToElement(ExpectedLineage(instruction, lot))),
                    "A supplied successor origin must bind this exact action, predecessor, allocation and reporting treatment.");
            var scheduleD = refunding && operation!.SuccessorRole == CorporateActionSuccessorRoleDto.Refunded;
            Require(mutation.ReportingTags.SequenceEqual(scheduleD ? new[] { "ScheduleD" } : Array.Empty<string>()),
                "Only the refunded successor may carry Schedule D treatment.");
            assignedTransaction += transaction;
            assignedFunctional += functional;
            assignedOriginalTransaction += originalTx;
            assignedOriginalFunctional += originalFx;
        }
        Require(mutations.Mutations.Sum(mutation => mutation.SourceQuantity ?? 0m) == source.OpenQuantity
            && mutations.Mutations.Sum(mutation => mutation.AllocationPercent ?? 1m) == 1m,
            "Successor contributions must fully relieve and conserve the predecessor.");
    }

    private static Guid ResolveSourceCorporateActionId(CorporateActionAccountingProjectionDto projection)
    {
        var economicEvent = projection.EconomicEvent!;
        var retainedSourceIds = projection.EvidenceManifest
            .Where(item => item.Role == CorporateActionProjectionEvidenceRoleDto.SourceEvent
                && item.EvidenceVersion == economicEvent.EventVersion
                && string.Equals(item.ContentHashSha256, economicEvent.SourceContentHash, StringComparison.Ordinal)
                && Guid.TryParse(item.SubjectId, out var subjectId) && subjectId != Guid.Empty)
            .Select(item => Guid.Parse(item.SubjectId)).Distinct().ToArray();
        Require(retainedSourceIds.Length == 1
            && (projection.SourceCorporateActionId is null || projection.SourceCorporateActionId == retainedSourceIds[0]),
            "Successor posting must bind one stable source corporate action identity to its retained source-event evidence.");
        return retainedSourceIds[0];
    }

    /// <summary>Independent currency allocation; the final successor receives the exact residual.</summary>
    public static decimal Allocate(decimal basis, decimal fraction, string currency)
        => decimal.Round(basis * fraction, currency switch
        {
            "BHD" or "IQD" or "JOD" or "KWD" or "LYD" or "OMR" or "TND" => 3,
            "BIF" or "CLP" or "DJF" or "GNF" or "ISK" or "JPY" or "KMF" or "KRW" or "PYG" or
                "RWF" or "UGX" or "UYI" or "VND" or "VUV" or "XAF" or "XOF" or "XPF" => 0,
            _ => 2
        }, MidpointRounding.AwayFromZero);

    public static string Fingerprint(OpenLotSuccessorInstructionDto instruction)
    {
        ArgumentNullException.ThrowIfNull(instruction);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
            WriteCanonical(writer, JsonSerializer.SerializeToElement(instruction));
        return Sha256Digest.Compute(stream.ToArray());
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            writer.WriteStartObject();
            foreach (var property in element.EnumerateObject().OrderBy(static property => property.Name, StringComparer.Ordinal))
            {
                writer.WritePropertyName(property.Name);
                WriteCanonical(writer, property.Value);
            }
            writer.WriteEndObject();
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            writer.WriteStartArray();
            foreach (var item in element.EnumerateArray())
                WriteCanonical(writer, item);
            writer.WriteEndArray();
        }
        else
            element.WriteTo(writer);
    }

    private static void Require(bool valid, string message)
    {
        if (!valid)
            throw new ArgumentException(message);
    }
}

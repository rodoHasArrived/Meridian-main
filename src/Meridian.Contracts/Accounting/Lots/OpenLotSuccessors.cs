using System.Text.Json;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Ledger;

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

/// <summary>Bounded cashless Reg S/144A exchange and advance-refunding conservation rules.</summary>
public static class OpenLotSuccessors
{
    public const string JournalFingerprintTag = "openLotSuccessorInstructionFingerprint";

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
        Require(action == CorporateActionAccountingTypeDto.RegS144AExchange || refunding,
            "Only cashless Reg S/144A exchange and advance-refunding successor posting is supported.");
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
        var targets = instruction.Successors;
        Require(targets.Count == (refunding ? 2 : 1)
            && mutations.Mutations.Count == targets.Count,
            "The supported exchange requires one successor; advance refunding requires exactly two.");
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
            Require(functional > 0m,
                "Every successor requires a positive functional carrying-basis allocation before journal drafting.");
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
                && mutation.TargetSecurityId == lot.SecurityId && lot.SecurityId != source.SecurityId
                && lot.TaxLotRecordId != source.TaxLotRecordId && lot.LotId != source.LotId
                && lot.BookPositionId != source.BookPositionId && lot.LedgerBookId == source.LedgerBookId
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
            Require(operation is not null && operation.Quantity == lot.OpenQuantity
                && (refunding || operation.SuccessorRole == CorporateActionSuccessorRoleDto.Successor),
                "Successor quantity, identity and role must match the retained exchange recipe.");
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

    /// <summary>Independent currency allocation; the final successor receives the exact residual.</summary>
    public static decimal Allocate(decimal basis, decimal fraction, string currency)
        => decimal.Round(basis * fraction, CurrencyMinorUnits.GetPrecision(currency), MidpointRounding.AwayFromZero);

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

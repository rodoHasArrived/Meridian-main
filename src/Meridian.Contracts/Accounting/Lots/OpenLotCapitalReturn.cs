using System.Text.Json;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Integrity;

namespace Meridian.Contracts.Accounting.Lots;

/// <summary>
/// Reviewed in-place capital return. The canonical plan binds CarryingValue to functional basis
/// and BasisAmount to acquisition-currency basis, as for canonical successor instructions.
/// Both approved reductions are retained independently; this is not a tax-basis calculation.
/// </summary>
public sealed record OpenLotCapitalReturnInstructionDto(
    CorporateActionAccountingProjectionDto Projection,
    OpenLotDto ExpectedLot,
    long ExpectedSecurityVersion,
    string ExpectedSecurityHash);

/// <summary>Applies the approved reduction without inventing an FX translation or excess-distribution policy.</summary>
public static class OpenLotCapitalReturn
{
    public const string JournalFingerprintTag = "openLotCapitalReturnInstructionFingerprint";

    public static OpenLotDto Project(OpenLotCapitalReturnInstructionDto instruction)
    {
        ArgumentNullException.ThrowIfNull(instruction);
        var projection = instruction.Projection;
        ArgumentNullException.ThrowIfNull(projection);
        var lot = instruction.ExpectedLot;
        OpenLotValidation.Validate(lot);
        Require(lot.Acquisition.Direction == OpenLotDirection.Long,
            "Capital-return basis reduction currently supports Long lots only; short distributions require separate posting authority.");
        Require(projection.CanPreparePostingCandidate && projection.EconomicEvent is not null
            && projection.ProjectionLineage is not null && projection.AccountingScope is not null
            && projection.CaseId != Guid.Empty && projection.CaseVersion > 0
            && projection.PolicyDecisionId != Guid.Empty && projection.PolicyDecisionVersion > 0
            && projection.PositionSnapshotId != Guid.Empty && projection.LotSnapshotId != Guid.Empty
            && projection.LotSnapshotVersion > 0 && Sha256Digest.IsCanonical(projection.ProjectionInputHash)
            && Sha256Digest.IsCanonical(projection.PostingIntentHash),
            "Capital return requires a complete reviewed authoritative corporate-action projection.");
        var treatment = projection.Treatment;
        ArgumentNullException.ThrowIfNull(treatment);
        Require(treatment.ActionType == CorporateActionAccountingTypeDto.ReturnOfCapital
            && treatment.BookValueTreatment == CorporateActionBookValueTreatmentDto.Reduce
            && treatment.HoldingPeriodTreatment == CorporateActionHoldingPeriodTreatmentDto.Unchanged,
            "Capital return must retain the approved carrying-basis reduction and unchanged holding period.");
        var sourceId = OpenLotSuccessors.GetSourceCorporateActionId(projection);
        var economicEvent = projection.EconomicEvent!;
        var mutations = projection.LotMutations!;
        Require(lot.Version > 0 && lot.OpenQuantity > 0m && instruction.ExpectedSecurityVersion > 0
            && Sha256Digest.IsCanonical(instruction.ExpectedSecurityHash)
            && mutations.PositionId == lot.BookPositionId && mutations.ExpectedPositionVersion > 0
            && projection.AccountingScope!.LedgerBookId == lot.LedgerBookId
            && economicEvent.SecurityId == lot.SecurityId && economicEvent.BookPositionId == lot.BookPositionId
            && economicEvent.EffectiveDate >= lot.AcquiredDate
            && economicEvent.EventType == AssetAccountingEventTypeNames.For(AssetAccountingEventKindDto.CorporateAction)
            && projection.PostingSet!.Currency == lot.Acquisition.FunctionalCurrency,
            "Capital return must bind the exact lot, reference, position, book, effective date and functional currency.");
        if (lot.Acquisition.CorporateActionLineage is { } origin)
            Require(economicEvent.EffectiveDate >= origin.EffectiveDate && economicEvent.EventId != origin.CorporateActionId
                && sourceId != origin.SourceCorporateActionId,
                "Capital return cannot precede the lot's immutable origin or repeat its source action.");
        Require(mutations.Mutations.Count == 1, "Capital return requires one reviewed in-place lot transition.");
        var mutation = mutations.Mutations[0];
        var before = new CorporateActionLotStateSnapshotDto(lot.OpenQuantity,
            lot.OpenFunctionalCostBasis, lot.OpenTransactionCostBasis);
        Require(mutation.Kind == CorporateActionLotMutationKindDto.ReduceCarryingValue
            && mutation.SecurityId == lot.SecurityId && mutation.SourceLotId == lot.TaxLotRecordId
            && mutation.ExpectedSourceLotVersion == lot.Version && mutation.SourceBefore == before
            && mutation.SourceAfter is not null && mutation.SourceAfter.Quantity == lot.OpenQuantity
            && mutation.CarryingAmount is > 0m && mutation.BasisAmount is > 0m
            && mutation.CarryingAmount <= lot.OpenFunctionalCostBasis
            && mutation.BasisAmount <= lot.OpenTransactionCostBasis
            && mutation.CarryingAmount == projection.EventAmount
            && (lot.Acquisition.AcquisitionCurrency != lot.Acquisition.FunctionalCurrency
                || mutation.BasisAmount == mutation.CarryingAmount)
            && mutation.SourceAfter.CarryingValue == lot.OpenFunctionalCostBasis - mutation.CarryingAmount
            && mutation.SourceAfter.BasisAmount == lot.OpenTransactionCostBasis - mutation.BasisAmount
            && mutation.HoldingPeriodTreatment == CorporateActionHoldingPeriodTreatmentDto.Unchanged
            && mutation.LinkedCaseId is null && mutation.ReportingTags.Count == 0,
            "Capital return must preserve quantity and reconcile both reviewed basis reductions within the remaining bases.");
        Require(projection.Recipe.Count == 1
            && projection.Recipe[0].Kind == CorporateActionEconomicOperationKindDto.ReturnOfCapital
            && projection.Recipe[0].SecurityId == lot.SecurityId
            && projection.Recipe[0].Amount == projection.EventAmount
            && projection.PostingSet!.Components.Count == 2
            && projection.PostingSet.Components.Count(component => component.Kind == CorporateActionPostingComponentKindDto.Cash
                && component.Amount == projection.EventAmount && component.Currency == lot.Acquisition.FunctionalCurrency) == 1
            && projection.PostingSet.Components.Count(component => component.Kind == CorporateActionPostingComponentKindDto.ReturnOfCapital
                && component.Amount == projection.EventAmount && component.Currency == lot.Acquisition.FunctionalCurrency) == 1,
            "Capital return requires the exact approved cash and carrying-basis reduction recipe.");
        var after = lot with
        {
            OpenTransactionCostBasis = mutation.SourceAfter!.BasisAmount,
            OpenFunctionalCostBasis = mutation.SourceAfter.CarryingValue,
            Version = checked(lot.Version + 1)
        };
        OpenLotValidation.Validate(after);
        return after;
    }

    public static string Fingerprint(OpenLotCapitalReturnInstructionDto instruction)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
            WriteCanonical(writer, JsonSerializer.SerializeToElement(instruction));
        return Sha256Digest.Compute(stream.ToArray());
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            writer.WriteStartObject();
            foreach (var property in value.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal))
            {
                writer.WritePropertyName(property.Name);
                WriteCanonical(writer, property.Value);
            }
            writer.WriteEndObject();
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            writer.WriteStartArray();
            foreach (var item in value.EnumerateArray())
                WriteCanonical(writer, item);
            writer.WriteEndArray();
        }
        else
            value.WriteTo(writer);
    }

    private static void Require(bool valid, string message)
    {
        if (!valid)
            throw new ArgumentException(message);
    }
}

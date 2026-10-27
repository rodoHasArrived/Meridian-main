using System.Text.Json;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Ledger;

namespace Meridian.Contracts.Accounting.Lots;

/// <summary>
/// One open face lot's share of a reviewed factor paydown: the lot as it is expected to stand, the
/// factor it is expected to hold, the principal it repays and the open bases it is left with.
/// <see cref="Principal"/> is in currency minor units, allocated by largest remainder so the lots
/// sum to the position principal exactly.
/// </summary>
public sealed record OpenLotFactorPaydownLotDto(
    OpenLotDto ExpectedLot,
    decimal ExpectedCurrentFactor,
    decimal Principal,
    decimal TransactionBasisAfter,
    decimal FunctionalBasisAfter,
    int Ordinal);

/// <summary>
/// Reviewed in-place principal paydown of every open face lot of one (security, book position)
/// scope. Each lot's open basis is relieved pro rata to the principal it repays; the difference
/// between principal and relieved basis is the premium or discount embedded in the repaid
/// principal and is recognised as realized gain or loss. A new factor of zero is a full paydown
/// that closes every lot. Same-currency lots only; this is not an FX translation.
/// </summary>
public sealed record OpenLotFactorPaydownInstructionDto(
    EconomicEventReferenceDto EconomicEvent,
    ProjectionLineageDto Lineage,
    AssetAccountingEventScopeDto Scope,
    long ExpectedPeriodVersion,
    string ExpectedSecurityHash,
    decimal PriorFactor,
    decimal NewFactor,
    decimal PrincipalPaydown,
    string Currency,
    IReadOnlyList<OpenLotFactorPaydownLotDto> Lots);

/// <summary>Pure allocation and projection rules of a factor paydown; the store re-runs them under lock.</summary>
public static class OpenLotFactorPaydown
{
    public const string JournalFingerprintTag = "openLotFactorPaydownInstructionFingerprint";

    /// <summary>Rules Studio component keys a factor-paydown rule's formulas resolve against.</summary>
    public const string PrincipalComponent = "lot.principal";
    public const string BasisRelievedComponent = "lot.basisRelieved";
    public const string RealizedGainComponent = "lot.realizedGain";
    public const string RealizedLossComponent = "lot.realizedLoss";

    /// <summary>
    /// Allocates one position-level principal across the open face lots in the order given (the
    /// store's acquired-date, lot-id order). The position principal must be the lots' held par face
    /// (par face × open share, summed) × (prior − new), rounded once to the currency's minor units
    /// away from zero — the projector's own arithmetic — so the factors determine it exactly. Each
    /// lot's raw share is par face × open share × (prior − new); every lot first takes its raw share
    /// truncated to minor units, and the minor units left over go one each to the lots with the
    /// largest truncated remainders, ties to the later lot (largest remainder). Every lot's
    /// principal is therefore non-negative, never more than its raw share rounded up, and the lots
    /// sum to the position principal exactly. Each lot's open bases are relieved pro rata to the
    /// principal repaid, which for a lot at the prior factor is the ratio new ÷ prior, rounded per
    /// lot, so a full paydown leaves zero basis exactly; across several lots the currency rounding
    /// of principal and basis is recognised in the realized result.
    /// </summary>
    public static IReadOnlyList<OpenLotFactorPaydownLotDto> Allocate(
        IReadOnlyList<OpenLotDto> lots,
        decimal priorFactor,
        decimal newFactor,
        decimal principalPaydown,
        string currency)
    {
        ArgumentNullException.ThrowIfNull(lots);
        RequireFactors(priorFactor, newFactor);
        Require(IsCurrency(currency), "Factor paydown requires a three-letter currency code.");
        Require(lots.Count > 0, "Factor paydown requires at least one open face lot.");
        Require(principalPaydown > 0m, "Factor paydown requires a positive position principal.");
        var precision = CurrencyMinorUnits.GetPrecision(currency);
        var minorUnit = new decimal(1, 0, 0, false, (byte)precision);
        var currents = new decimal[lots.Count];
        var floors = new decimal[lots.Count];
        var remainders = new decimal[lots.Count];
        var heldParFace = 0m;
        var ids = new HashSet<Guid>();
        for (var index = 0; index < lots.Count; index++)
        {
            var lot = lots[index];
            var terms = RequireOpenFaceLot(lot, currency);
            Require(ids.Add(lot.TaxLotRecordId), "Factor paydown cannot repeat a lot.");
            var current = lot.CurrentFactor ?? terms.BookedFactor;
            Require(current == priorFactor,
                "Every lot in a factor paydown must hold the position's prior factor; a lot already below it is stale.");
            // The same arithmetic as the held-face resolver: par face is the booked face restated
            // to a factor of one, scaled by the share of the lot still open.
            var parFace = lot.OriginalQuantity / terms.BookedFactor;
            var openShare = lot.OpenQuantity / lot.OriginalQuantity;
            heldParFace += parFace * openShare;
            var raw = parFace * openShare * (priorFactor - newFactor);
            currents[index] = current;
            floors[index] = decimal.Round(raw, precision, MidpointRounding.ToZero);
            remainders[index] = raw - floors[index];
        }

        // Held par face times the factor decline, rounded once, exactly as the projector derives the
        // position principal. Without this tie a lot could absorb principal shifted onto it and the
        // shift would post as realized gain or loss.
        Require(principalPaydown == decimal.Round(heldParFace * (priorFactor - newFactor), precision, MidpointRounding.AwayFromZero),
            "Factor paydown position principal must equal the lots' held par face x (prior - new) rounded once to the currency's minor units.");
        // Largest remainder: the position rounding leaves at most one minor unit per lot with a
        // fractional share, so each extra unit lifts a lot from its truncated share to its rounded-up
        // share and no lot is ever pushed below zero or above its raw share rounded up.
        var extraUnits = (principalPaydown - floors.Sum()) / minorUnit;
        Require(extraUnits >= 0m && extraUnits == decimal.Truncate(extraUnits)
            && extraUnits <= remainders.Count(static remainder => remainder > 0m),
            "Factor paydown position principal cannot be allocated to the lots within one minor unit of each lot's raw share.");
        var receivesUnit = Enumerable.Range(0, lots.Count)
            .OrderByDescending(index => remainders[index])
            .ThenByDescending(static index => index)
            .Take((int)extraUnits)
            .ToHashSet();
        var allocated = new OpenLotFactorPaydownLotDto[lots.Count];
        for (var index = 0; index < lots.Count; index++)
        {
            var lot = lots[index];
            var principal = receivesUnit.Contains(index) ? floors[index] + minorUnit : floors[index];
            var transactionAfter = decimal.Round(lot.OpenTransactionCostBasis * newFactor / priorFactor, precision, MidpointRounding.AwayFromZero);
            var functionalAfter = decimal.Round(lot.OpenFunctionalCostBasis * newFactor / priorFactor, precision, MidpointRounding.AwayFromZero);
            allocated[index] = new(lot, currents[index], principal, transactionAfter, functionalAfter, index);
        }

        return allocated;
    }

    /// <summary>
    /// Validates the instruction completely, re-runs <see cref="Allocate"/> over the expected lots
    /// and requires the stated per-lot amounts to agree, then returns each lot as it stands after
    /// the paydown: version advanced, bases relieved, the new factor held, and closed when the new
    /// factor is zero.
    /// </summary>
    public static IReadOnlyList<OpenLotDto> Project(OpenLotFactorPaydownInstructionDto instruction)
    {
        ArgumentNullException.ThrowIfNull(instruction);
        var economicEvent = instruction.EconomicEvent;
        var lineage = instruction.Lineage;
        var scope = instruction.Scope;
        ArgumentNullException.ThrowIfNull(economicEvent);
        ArgumentNullException.ThrowIfNull(lineage);
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(instruction.Lots);
        RequireFactors(instruction.PriorFactor, instruction.NewFactor);
        Require(IsCurrency(instruction.Currency), "Factor paydown requires a three-letter currency code.");
        Require(scope.SecurityId != Guid.Empty && scope.BookPositionId != Guid.Empty && scope.LedgerBookId != Guid.Empty
            && scope.PeriodId != Guid.Empty && scope.ExpectedSecurityVersion > 0 && scope.ExpectedBookPositionVersion > 0
            && !string.IsNullOrWhiteSpace(scope.FundProfileId) && instruction.ExpectedPeriodVersion >= 0
            && Sha256Digest.IsCanonical(instruction.ExpectedSecurityHash),
            "Factor paydown requires a complete versioned security, position, book, period and reference scope.");
        Require(economicEvent.EventId != Guid.Empty && economicEvent.EventVersion > 0
            && economicEvent.EventType == AssetAccountingEventTypeNames.For(AssetAccountingEventKindDto.CorporateAction)
            && economicEvent.SecurityId == scope.SecurityId && economicEvent.BookPositionId == scope.BookPositionId
            && Sha256Digest.IsCanonical(economicEvent.SourceContentHash),
            "Factor paydown requires the canonical corporate-action event bound to its security, position and retained source hash.");
        Require(economicEvent.EventId == FactorPaydownProjectionIdentity.EventId(scope.SecurityId, scope.BookPositionId,
                economicEvent.EffectiveDate, instruction.PriorFactor, instruction.NewFactor, economicEvent.SourceContentHash!),
            "Factor paydown event identity must be the projector's deterministic identity of its security, position, date, factors and source hash.");
        Require(string.Equals(lineage.ModelKey, FactorPaydownProjectionIdentity.ModelKey, StringComparison.Ordinal)
            && lineage.BookPositionId == scope.BookPositionId && lineage.TriggerEvent is not null
            && lineage.TriggerEvent.EventId == economicEvent.EventId,
            "Factor paydown requires lineage from the registered factor-paydown projection model triggered by its event.");
        var lots = instruction.Lots;
        Require(lots.Count > 0, "Factor paydown requires at least one open face lot.");
        for (var index = 0; index < lots.Count; index++)
        {
            var lot = lots[index];
            ArgumentNullException.ThrowIfNull(lot);
            Require(lot.Ordinal == index, "Factor paydown lots must be stated in contiguous ordinal order.");
            var expected = lot.ExpectedLot;
            ArgumentNullException.ThrowIfNull(expected);
            Require(expected.SecurityId == scope.SecurityId && expected.BookPositionId == scope.BookPositionId
                && expected.LedgerBookId == scope.LedgerBookId && expected.AcquiredDate <= economicEvent.EffectiveDate,
                "Factor paydown lots must belong to the exact security, position and book and be acquired on or before the paydown date.");
            if (expected.Acquisition?.CorporateActionLineage is { } origin)
                Require(economicEvent.EffectiveDate >= origin.EffectiveDate && economicEvent.EventId != origin.CorporateActionId,
                    "Factor paydown cannot precede a successor lot's immutable origin or repeat it.");
        }

        var allocated = Allocate(lots.Select(static lot => lot.ExpectedLot).ToArray(),
            instruction.PriorFactor, instruction.NewFactor, instruction.PrincipalPaydown, instruction.Currency);
        var projected = new OpenLotDto[lots.Count];
        for (var index = 0; index < lots.Count; index++)
        {
            var stated = lots[index];
            var computed = allocated[index];
            Require(stated.ExpectedCurrentFactor == computed.ExpectedCurrentFactor && stated.Principal == computed.Principal
                && stated.TransactionBasisAfter == computed.TransactionBasisAfter
                && stated.FunctionalBasisAfter == computed.FunctionalBasisAfter,
                "Factor paydown per-lot principal, factor and relieved bases must equal the server allocation.");
            var lot = stated.ExpectedLot;
            var after = lot with
            {
                OpenQuantity = instruction.NewFactor == 0m ? 0m : lot.OpenQuantity,
                OpenTransactionCostBasis = stated.TransactionBasisAfter,
                OpenFunctionalCostBasis = stated.FunctionalBasisAfter,
                Version = checked(lot.Version + 1),
                CurrentFactor = instruction.NewFactor
            };
            OpenLotValidation.Validate(after);
            projected[index] = after;
        }

        Require(lots.Sum(static lot => lot.Principal) == instruction.PrincipalPaydown,
            "Factor paydown lot principals must sum to the position principal exactly.");
        return projected;
    }

    /// <summary>Functional and acquisition-currency basis relieved across the lots.</summary>
    public static (decimal Transaction, decimal Functional) BasisRelieved(OpenLotFactorPaydownInstructionDto instruction)
    {
        ArgumentNullException.ThrowIfNull(instruction);
        return (instruction.Lots.Sum(static lot => lot.ExpectedLot.OpenTransactionCostBasis - lot.TransactionBasisAfter),
            instruction.Lots.Sum(static lot => lot.ExpectedLot.OpenFunctionalCostBasis - lot.FunctionalBasisAfter));
    }

    /// <summary>
    /// Principal repaid less functional basis relieved: positive is realized gain (a discount lot),
    /// negative is realized loss (a premium lot), zero books no realized line.
    /// </summary>
    public static decimal RealizedResult(OpenLotFactorPaydownInstructionDto instruction)
        => instruction.PrincipalPaydown - BasisRelieved(instruction).Functional;

    /// <summary>The Rules Studio lot-mutation components a factor-paydown rule's lines resolve to.</summary>
    public static IReadOnlyDictionary<string, decimal> Components(OpenLotFactorPaydownInstructionDto instruction)
    {
        ArgumentNullException.ThrowIfNull(instruction);
        var result = RealizedResult(instruction);
        return new Dictionary<string, decimal>(StringComparer.Ordinal)
        {
            [PrincipalComponent] = instruction.PrincipalPaydown,
            [BasisRelievedComponent] = BasisRelieved(instruction).Functional,
            [RealizedGainComponent] = Math.Max(result, 0m),
            [RealizedLossComponent] = Math.Max(-result, 0m)
        };
    }

    public static string Fingerprint(OpenLotFactorPaydownInstructionDto instruction)
    {
        ArgumentNullException.ThrowIfNull(instruction);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
            WriteCanonical(writer, JsonSerializer.SerializeToElement(instruction));
        return Sha256Digest.Compute(stream.ToArray());
    }

    private static FaceValueAcquisitionTermsDto RequireOpenFaceLot(OpenLotDto lot, string currency)
    {
        OpenLotValidation.Validate(lot);
        var acquisition = lot.Acquisition;
        Require(acquisition.Direction == OpenLotDirection.Long,
            "Factor paydown currently supports Long lots only; short principal receipts require separate posting authority.");
        Require(acquisition.QuantityBasis == LotQuantityBasis.Face && acquisition.FaceValueTerms is not null,
            "Factor paydown requires face lots that retain their acquisition-time par terms.");
        Require(lot.Version > 0 && lot.OpenQuantity > 0m, "Factor paydown requires versioned open lots.");
        Require(acquisition.AcquisitionCurrency == currency && acquisition.FunctionalCurrency == currency,
            "Factor paydown supports same-currency lots only; a translated paydown requires explicit FX authority.");
        return acquisition.FaceValueTerms!;
    }

    private static void RequireFactors(decimal priorFactor, decimal newFactor)
        => Require(priorFactor > 0m && priorFactor <= 1m && newFactor >= 0m && newFactor < priorFactor,
            "Factor paydown requires a prior factor in (0, 1] and a new factor in [0, prior); an equal or higher factor is not a paydown.");

    private static bool IsCurrency(string? value) => value is { Length: 3 } && value.All(static c => c is >= 'A' and <= 'Z');

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            writer.WriteStartObject();
            foreach (var property in value.EnumerateObject().OrderBy(static property => property.Name, StringComparer.Ordinal))
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

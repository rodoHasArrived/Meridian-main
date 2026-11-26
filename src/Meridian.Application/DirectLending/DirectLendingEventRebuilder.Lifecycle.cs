using System.Text.Json;
using Meridian.Contracts.DirectLending;
using Meridian.FSharp.DirectLendingInterop;

namespace Meridian.Application.DirectLending;

public sealed partial class DirectLendingEventRebuilder
{
    private sealed class LifecycleReplayContext
    {
        public bool HasAmortizationBasis { get; set; }
    }

    private static IEnumerable<LoanEventLineageDto> OrderForRebuild(IReadOnlyList<LoanEventLineageDto> history)
    {
        var identities = new Dictionary<Guid, LoanEventLineageDto>();
        var versions = new Dictionary<long, LoanEventLineageDto>();
        var expectedVersion = 1L;
        foreach (var entry in history.OrderBy(static item => item.AggregateVersion))
        {
            if (entry.EventId == Guid.Empty || entry.AggregateVersion < 1)
                throw new InvalidDataException($"Retained direct-lending event '{entry.EventType}' requires an event identity and positive aggregate version.");

            if (identities.TryGetValue(entry.EventId, out var sameIdentity))
            {
                if (entry != sameIdentity)
                    throw new InvalidDataException($"Conflicting retained {entry.EventType} event identity '{entry.EventId:D}' during rebuild.");
                continue;
            }
            if (versions.TryGetValue(entry.AggregateVersion, out var sameVersion))
            {
                if (entry != sameVersion)
                    throw new InvalidDataException($"Conflicting retained {entry.EventType} aggregate version '{entry.AggregateVersion}' during rebuild.");
                continue;
            }
            if (entry.AggregateVersion != expectedVersion)
                throw new InvalidDataException($"Retained direct-lending history is incomplete: expected aggregate version '{expectedVersion}' but found '{entry.AggregateVersion}'.");

            identities.Add(entry.EventId, entry);
            versions.Add(entry.AggregateVersion, entry);
            expectedVersion++;
            yield return entry;
        }
    }

    private static (LoanContractDetailDto Contract, LoanServicingStateDto Servicing) RebuildLifecycleEvent(
        Guid loanId,
        LoanContractDetailDto contract,
        LoanServicingStateDto servicing,
        LoanEventLineageDto entry,
        JsonElement root,
        LifecycleReplayContext context)
    {
        try
        {
            var fields = ReadLifecycleFields(root);
            if (fields.TryGetValue("loanId", out var retainedLoanId) && retainedLoanId.GetGuid() != loanId)
                throw new InvalidDataException("Retained lifecycle evidence belongs to another loan.");

            var before = servicing;
            string revisionType;
            string notes;
            DateOnly effectiveDate;
            switch (entry.EventType)
            {
                case "loan.fee-assessed":
                    var fee = ReadLifecycleAmount(fields, "amount");
                    if (fee <= 0m)
                        throw new InvalidDataException("Retained assessed fee must be positive.");
                    servicing = servicing with
                    {
                        Balances = servicing.Balances with { FeesAccruedUnpaid = servicing.Balances.FeesAccruedUnpaid + fee }
                    };
                    effectiveDate = ReadLifecycleDate(entry, fields, "effectiveDate");
                    revisionType = "InternalEvent";
                    notes = FormattableString.Invariant($"Fee assessed for {fee:0.00}.");
                    break;

                case "loan.write-off-applied":
                    var applied = ReadLifecycleAmount(fields, "appliedAmount", "amount");
                    if (applied < 0m || applied > servicing.Balances.PrincipalOutstanding)
                        throw new InvalidDataException("Retained write-off exceeds the prior principal or is negative.");
                    if (fields.ContainsKey("requestedAmount"))
                    {
                        var requested = ReadLifecycleAmount(fields, "requestedAmount");
                        if (requested <= 0m || Math.Min(requested, servicing.Balances.PrincipalOutstanding) != applied)
                            throw new InvalidDataException("Retained write-off requested and applied amounts conflict.");
                    }
                    var totalDrawn = Math.Max(0m, servicing.TotalDrawn - applied);
                    servicing = servicing with
                    {
                        TotalDrawn = totalDrawn,
                        AvailableToDraw = DirectLendingInterop.CalculateAvailableToDraw(servicing.CurrentCommitment, totalDrawn),
                        Balances = servicing.Balances with { PrincipalOutstanding = servicing.Balances.PrincipalOutstanding - applied },
                        DrawdownLots = ApplyPaymentToLots(servicing.DrawdownLots, applied)
                    };
                    effectiveDate = ReadLifecycleDate(entry, fields, "effectiveDate");
                    revisionType = "InternalEvent";
                    notes = ReadLifecycleString(fields, "reason");
                    break;

                case "loan.servicer-position-imported":
                    var batchId = fields["batchId"].GetGuid();
                    if (batchId == Guid.Empty || fields["servicerReportLineId"].GetGuid() == Guid.Empty)
                        throw new InvalidDataException("Retained servicer revision requires its batch and report-line identities.");
                    servicing = servicing with
                    {
                        AvailableToDraw = ReadOptionalLifecycleAmount(fields, "commitmentAvailable") ?? servicing.AvailableToDraw,
                        Balances = servicing.Balances with
                        {
                            PrincipalOutstanding = ReadOptionalLifecycleAmount(fields, "principalOutstanding") ?? servicing.Balances.PrincipalOutstanding,
                            InterestAccruedUnpaid = ReadOptionalLifecycleAmount(fields, "interestAccruedUnpaid") ?? servicing.Balances.InterestAccruedUnpaid,
                            FeesAccruedUnpaid = ReadOptionalLifecycleAmount(fields, "feesAccruedUnpaid") ?? servicing.Balances.FeesAccruedUnpaid,
                            PenaltyAccruedUnpaid = ReadOptionalLifecycleAmount(fields, "penaltyAccruedUnpaid") ?? servicing.Balances.PenaltyAccruedUnpaid
                        }
                    };
                    effectiveDate = ReadLifecycleDate(entry, fields, "reportAsOfDate");
                    revisionType = "ServicerReport";
                    notes = $"Servicer position revision applied from batch {batchId:D}.";
                    break;

                case "loan.collateral-added":
                case "CollateralAdded":
                    var collateralElement = fields.TryGetValue("collateral", out var nestedCollateral) ? nestedCollateral : root;
                    var collateralFields = ReadLifecycleFields(collateralElement);
                    var collateral = collateralElement.Deserialize<CollateralDto>(JsonOptions)
                        ?? throw new InvalidDataException("Retained added collateral is unavailable.");
                    if (collateral.CollateralId == Guid.Empty || !collateralFields.ContainsKey("collateralType")
                        || !collateralFields.ContainsKey("currency") || !collateralFields.ContainsKey("appraisalDate"))
                        throw new InvalidDataException("Retained added collateral is missing identity, classification, currency or appraisal date.");
                    if ((servicing.Collateral ?? []).Any(item => item.CollateralId == collateral.CollateralId))
                        throw new InvalidDataException("Retained added collateral identity already exists.");
                    servicing = servicing with { Collateral = [.. (servicing.Collateral ?? []), collateral] };
                    effectiveDate = entry.EffectiveDate ?? collateral.AppraisalDate;
                    if (effectiveDate != collateral.AppraisalDate)
                        throw new InvalidDataException("Retained collateral appraisal and effective dates conflict.");
                    revisionType = "CollateralAdded";
                    notes = $"Added collateral {collateral.CollateralId}";
                    break;

                case "loan.collateral-removed":
                case "CollateralRemoved":
                    var removedId = fields["collateralId"].GetGuid();
                    if (entry.EventType == "loan.collateral-removed"
                        && !(servicing.Collateral ?? []).Any(item => item.CollateralId == removedId))
                        throw new InvalidDataException("Retained collateral removal has no matching collateral identity.");
                    servicing = servicing with { Collateral = (servicing.Collateral ?? []).Where(item => item.CollateralId != removedId).ToArray() };
                    effectiveDate = ReadLifecycleDate(entry, fields, "effectiveDate");
                    revisionType = "CollateralRemoved";
                    notes = ReadLifecycleString(fields, "reason");
                    break;

                case "loan.collateral-value-updated":
                case "CollateralValueUpdated":
                    var revaluedId = fields["collateralId"].GetGuid();
                    if (entry.EventType == "loan.collateral-value-updated"
                        && !(servicing.Collateral ?? []).Any(item => item.CollateralId == revaluedId))
                        throw new InvalidDataException("Retained collateral revaluation has no matching collateral identity.");
                    var value = ReadLifecycleAmount(fields, "newEstimatedValue");
                    effectiveDate = ReadLifecycleDate(entry, fields, "newAppraisalDate");
                    servicing = servicing with
                    {
                        Collateral = (servicing.Collateral ?? []).Select(item => item.CollateralId == revaluedId
                            ? item with { EstimatedValue = value, AppraisalDate = effectiveDate } : item).ToArray()
                    };
                    revisionType = "CollateralValueUpdated";
                    notes = $"Revalued collateral {revaluedId}";
                    break;

                case "loan.status-transitioned":
                case "LoanStatusTransitioned":
                    var status = fields["newStatus"].Deserialize<LoanStatus>(JsonOptions);
                    contract = contract with { Status = status };
                    servicing = servicing with { Status = status };
                    effectiveDate = ReadLifecycleDate(entry, fields, "effectiveDate");
                    revisionType = "StatusTransition";
                    notes = ReadLifecycleString(fields, "reason");
                    break;

                case "loan.discount-premium-amortized":
                case "DiscountPremiumAmortized":
                    var discount = ReadLifecycleAmount(fields, "discountAmortization", "discountAmort");
                    var premium = ReadLifecycleAmount(fields, "premiumAmortization", "premiumAmort");
                    servicing = RebuildAmortization(servicing, fields, context, discount, premium);
                    effectiveDate = ReadLifecycleDate(entry, fields, "accrualDate");
                    revisionType = "DiscountPremiumAmortization";
                    notes = FormattableString.Invariant($"Amortized discount={discount:F4} premium={premium:F4}");
                    break;

                default:
                    throw new InvalidDataException($"Unsupported retained lifecycle event '{entry.EventType}'.");
            }

            return (contract, RestoreLifecycleRevision(before, servicing, fields, entry, revisionType, effectiveDate, notes));
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException
            or OverflowException or ArgumentException or KeyNotFoundException)
        {
            throw new InvalidDataException($"Malformed retained {entry.EventType} event '{entry.EventId:D}'.", ex);
        }
    }

    private static LoanServicingStateDto RebuildAmortization(
        LoanServicingStateDto servicing,
        IReadOnlyDictionary<string, JsonElement> fields,
        LifecycleReplayContext context,
        decimal discount,
        decimal premium)
    {
        if (discount < 0m || premium < 0m)
            throw new InvalidDataException("Retained amortization must not be negative.");

        string[] basisFields = ["unamortizedDiscountBefore", "unamortizedDiscountAfter", "unamortizedPremiumBefore", "unamortizedPremiumAfter"];
        var hasBasis = basisFields.Any(fields.ContainsKey);
        decimal discountAfter;
        decimal premiumAfter;
        if (hasBasis)
        {
            if (!basisFields.All(fields.ContainsKey))
                throw new InvalidDataException("Retained amortization basis evidence is incomplete.");
            var discountBefore = ReadLifecycleAmount(fields, basisFields[0]);
            discountAfter = ReadLifecycleAmount(fields, basisFields[1]);
            var premiumBefore = ReadLifecycleAmount(fields, basisFields[2]);
            premiumAfter = ReadLifecycleAmount(fields, basisFields[3]);
            if (discountBefore < 0m || premiumBefore < 0m || discount > discountBefore || premium > premiumBefore
                || discountAfter != Math.Max(0m, discountBefore - discount)
                || premiumAfter != Math.Max(0m, premiumBefore - premium))
                throw new InvalidDataException("Retained amortization amounts and basis balances conflict.");
            if (context.HasAmortizationBasis
                && (discountBefore != servicing.UnamortizedDiscount || premiumBefore != servicing.UnamortizedPremium))
                throw new InvalidDataException("Retained amortization opening basis conflicts with earlier retained basis evidence.");
            // The first complete receipt retains the live pre/post basis. No opening balance is
            // inferred from the amortization amount, current terms or a later state snapshot.
            context.HasAmortizationBasis = true;
        }
        else
        {
            if (!context.HasAmortizationBasis && (discount != 0m || premium != 0m))
                throw new InvalidDataException("Historical nonzero amortization has no retained opening basis; recovery requires explicit basis evidence.");
            if (discount > servicing.UnamortizedDiscount || premium > servicing.UnamortizedPremium)
                throw new InvalidDataException("Retained amortization exceeds the remaining basis.");
            discountAfter = Math.Max(0m, servicing.UnamortizedDiscount - discount);
            premiumAfter = Math.Max(0m, servicing.UnamortizedPremium - premium);
        }

        return servicing with { UnamortizedDiscount = discountAfter, UnamortizedPremium = premiumAfter };
    }

    private static LoanServicingStateDto RestoreLifecycleRevision(
        LoanServicingStateDto before,
        LoanServicingStateDto after,
        IReadOnlyDictionary<string, JsonElement> fields,
        LoanEventLineageDto entry,
        string revisionType,
        DateOnly effectiveDate,
        string notes)
    {
        var revision = fields.TryGetValue("servicingRevision", out var retainedRevision)
            ? retainedRevision.Deserialize<ServicingRevisionDto>(JsonOptions)
                ?? throw new InvalidDataException("Retained servicing revision is missing.")
            : new ServicingRevisionDto(before.ServicingRevision + 1, revisionType, effectiveDate, entry.RecordedAt, notes);
        if (revision.RevisionNumber != before.ServicingRevision + 1 || revision.RevisionSourceType != revisionType
            || revision.EffectiveAsOfDate != effectiveDate || revision.CreatedAt == default || revision.Notes is null)
            throw new InvalidDataException("Retained servicing revision conflicts with its lifecycle event or prior state.");
        return after with
        {
            ServicingRevision = revision.RevisionNumber,
            RevisionHistory = new[] { revision }.Concat(before.RevisionHistory).ToArray()
        };
    }

    private static Dictionary<string, JsonElement> ReadLifecycleFields(JsonElement root)
        => root.EnumerateObject().ToDictionary(static field => field.Name, static field => field.Value, StringComparer.OrdinalIgnoreCase);

    private static decimal ReadLifecycleAmount(IReadOnlyDictionary<string, JsonElement> fields, params string[] names)
    {
        decimal? amount = null;
        foreach (var name in names)
        {
            if (!fields.TryGetValue(name, out var field))
                continue;
            var value = field.GetDecimal();
            if (amount.HasValue && amount.Value != value)
                throw new InvalidDataException($"Retained lifecycle amount aliases for '{names[0]}' conflict.");
            amount = value;
        }
        return amount ?? throw new InvalidDataException($"Retained lifecycle amount '{names[0]}' is missing.");
    }

    private static decimal? ReadOptionalLifecycleAmount(IReadOnlyDictionary<string, JsonElement> fields, string name)
        => fields.TryGetValue(name, out var value) && value.ValueKind != JsonValueKind.Null ? value.GetDecimal() : null;

    private static string ReadLifecycleString(IReadOnlyDictionary<string, JsonElement> fields, string name)
        => fields[name].GetString() ?? throw new InvalidDataException($"Retained lifecycle '{name}' is missing.");

    private static DateOnly ReadLifecycleDate(
        LoanEventLineageDto entry, IReadOnlyDictionary<string, JsonElement> fields, params string[] names)
    {
        DateOnly? result = entry.EffectiveDate;
        foreach (var name in names)
        {
            if (!fields.TryGetValue(name, out var field))
                continue;
            var value = field.Deserialize<DateOnly>(JsonOptions);
            if (result.HasValue && result.Value != value)
                throw new InvalidDataException($"Retained lifecycle '{name}' conflicts with its effective date.");
            result = value;
        }
        return result is { } date
            ? date : throw new InvalidDataException("Retained lifecycle effective date is missing.");
    }
}

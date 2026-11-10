using System.Text;
using System.Text.Json;
using Meridian.Contracts.DirectLending;
using Meridian.FSharp.DirectLendingInterop;
using Meridian.Storage.DirectLending;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Text;

namespace Meridian.Application.DirectLending;

internal static class DirectLendingServiceSupport
{
    public static Guid CreateRunIdentity(Guid loanId, string kind, DirectLendingCommandMetadataDto? metadata)
    {
        var identity = metadata?.CommandId ?? (metadata?.ReplayFlag == true ? metadata.CausationId : null);
        if (identity is null)
            return Guid.NewGuid();
        var bytes = Sha256Digest.ComputeBytesUtf8(
            $"meridian/direct-lending/{kind}/{loanId:N}/{identity.Value:N}");
        return new Guid(bytes.AsSpan(0, 16));
    }

    private static readonly JsonSerializerOptions HashJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static readonly JsonSerializerOptions EventJsonOptions = new(JsonSerializerDefaults.Web);

    public static DirectLendingCommandError? ValidateCreateRequest(CreateLoanRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.FacilityName))
        {
            return new DirectLendingCommandError(DirectLendingErrorCode.Validation, "Facility name is required.");
        }

        return ValidateTerms(request.Terms);
    }

    public static DirectLendingCommandError? ValidateTerms(DirectLendingTermsDto terms)
    {
        if (terms.CommitmentAmount <= 0m)
        {
            return new DirectLendingCommandError(DirectLendingErrorCode.Validation, "Commitment amount must be positive.");
        }

        if (terms.MaturityDate < terms.OriginationDate)
        {
            return new DirectLendingCommandError(DirectLendingErrorCode.Validation, "Maturity date cannot be earlier than origination date.");
        }

        if (terms.RateTypeKind == RateTypeKind.Fixed && terms.FixedAnnualRate is null)
        {
            return new DirectLendingCommandError(DirectLendingErrorCode.Validation, "Fixed-rate terms require a fixed annual rate.");
        }

        if (terms.RateTypeKind == RateTypeKind.Floating && string.IsNullOrWhiteSpace(terms.InterestIndexName))
        {
            return new DirectLendingCommandError(DirectLendingErrorCode.Validation, "Floating-rate terms require an interest index name.");
        }

        if (terms.EffectiveRateFloor is { } floor && terms.EffectiveRateCap is { } cap && cap < floor)
        {
            return new DirectLendingCommandError(DirectLendingErrorCode.Validation, "Effective rate cap must be greater than or equal to the effective rate floor.");
        }

        if (DirectLendingServicingPolicyValidator.Validate(terms) is { } policyError)
        {
            return new DirectLendingCommandError(DirectLendingErrorCode.Validation, policyError);
        }

        return null;
    }

    public static DirectLendingCommandError? ValidateSecurityMasterReferenceRequired(DirectLendingTermsDto terms)
        => terms.SecurityMasterReference is null || terms.SecurityMasterReference.SecurityId == Guid.Empty
            ? new DirectLendingCommandError(
                DirectLendingErrorCode.Validation,
                "Direct lending durable writes require a Security Master reference when MERIDIAN_DIRECT_LENDING_REQUIRE_SECURITY_MASTER_REFERENCE is enabled.")
            : null;

    public static DirectLendingCommandError? EnsureActive(LoanContractDetailDto contract)
        => contract.Status == LoanStatus.Active
            ? null
            : new DirectLendingCommandError(DirectLendingErrorCode.Validation, "Loan must be active before servicing actions can be applied.");

    public static DirectLendingCommandResult<decimal> ResolveAnnualRate(DirectLendingTermsDto terms, RateResetDto? currentRateReset)
    {
        return terms.RateTypeKind switch
        {
            RateTypeKind.Fixed when terms.FixedAnnualRate is { } fixedRate => DirectLendingCommandResult<decimal>.Success(fixedRate),
            RateTypeKind.Floating when currentRateReset is not null => DirectLendingCommandResult<decimal>.Success(currentRateReset.AllInRate),
            RateTypeKind.Floating => DirectLendingCommandResult<decimal>.Failure(DirectLendingErrorCode.Validation, "Floating-rate loans require a current rate reset before accrual."),
            _ => DirectLendingCommandResult<decimal>.Failure(DirectLendingErrorCode.Validation, "Unable to resolve annual rate.")
        };
    }

    public static LoanTermsVersionDto CreateTermsVersion(
        int versionNumber,
        DirectLendingTermsDto terms,
        string sourceAction,
        string? amendmentReason,
        DateTimeOffset recordedAt)
    {
        return new LoanTermsVersionDto(
            versionNumber,
            ComputeTermsHash(terms),
            terms,
            sourceAction,
            amendmentReason,
            recordedAt);
    }

    public static string ComputeTermsHash(DirectLendingTermsDto terms)
    {
        var json = JsonSerializer.Serialize(terms, HashJsonOptions);
        var bytes = Sha256Digest.ComputeBytesUtf8(json);
        return Convert.ToHexString(bytes);
    }

    public static JsonDocument CreatePayloadDocument(object payload)
        => JsonSerializer.SerializeToDocument(payload, EventJsonOptions);

    public static DirectLendingEventWriteMetadata CreateEventMetadata(DirectLendingCommandMetadataDto? metadata, string defaultSourceSystem)
    {
        var commandId = metadata?.CommandId ?? Guid.NewGuid();
        var correlationId = metadata?.CorrelationId ?? commandId;
        return new DirectLendingEventWriteMetadata(
            CausationId: metadata?.CausationId,
            CorrelationId: correlationId,
            CommandId: commandId,
            SourceSystem: metadata?.SourceSystem ?? defaultSourceSystem,
            ReplayFlag: metadata?.ReplayFlag ?? false);
    }

    /// <summary>
    /// PRD-115: whether a command id is already recorded on a loan's history. A retry of an Apply or
    /// Refund that committed carries the same id; recomputing it against the updated balances would
    /// describe a second movement. Only the same request (same mutation, amount, effective date and
    /// external reference) is a replay; any other reuse of the id is a conflict.
    /// </summary>
    public static CommittedCommandMatch FindCommittedCommand(
        IEnumerable<LoanEventLineageDto> history,
        Guid? commandId,
        string eventType,
        decimal amount,
        DateOnly effectiveDate,
        string? externalRef)
    {
        if (commandId is not Guid id || id == Guid.Empty)
        {
            return CommittedCommandMatch.None;
        }

        var committed = history.FirstOrDefault(entry => entry.CommandId == id);
        if (committed is null)
        {
            return CommittedCommandMatch.None;
        }

        return string.Equals(committed.EventType, eventType, StringComparison.Ordinal)
            && RecordedRequestMatches(committed.PayloadJson, amount, effectiveDate, externalRef)
                ? CommittedCommandMatch.Replay
                : CommittedCommandMatch.Conflict;
    }

    private static bool RecordedRequestMatches(string payloadJson, decimal amount, DateOnly effectiveDate, string? externalRef)
    {
        try
        {
            using var document = JsonDocument.Parse(payloadJson);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            JsonElement? Find(string name)
            {
                foreach (var property in root.EnumerateObject())
                {
                    if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                    {
                        return property.Value;
                    }
                }

                return null;
            }

            var recordedRef = Find("externalRef");
            return Find("amount") is { ValueKind: JsonValueKind.Number } recordedAmount && recordedAmount.GetDecimal() == amount
                && Find("effectiveDate") is { ValueKind: JsonValueKind.String } recordedDate
                && DateOnly.TryParse(recordedDate.GetString(), System.Globalization.CultureInfo.InvariantCulture, out var date) && date == effectiveDate
                && string.Equals(
                    TextPrimitives.NormalizeOptional(recordedRef is { ValueKind: JsonValueKind.String } reference ? reference.GetString() : null),
                    TextPrimitives.NormalizeOptional(externalRef),
                    StringComparison.Ordinal);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>Cash transaction type for an outgoing refund of unapplied cash (PRD-115).</summary>
    public const string UnappliedCashRefundTransactionType = "UnappliedCashRefund";

    /// <summary>
    /// True for cash the loan received. Reconciliation matches expected receipts only against these,
    /// so an outgoing refund can never satisfy a projected payment.
    /// </summary>
    public static bool IsIncomingReceipt(CashTransactionDto transaction)
        => !string.Equals(transaction.TransactionType, UnappliedCashRefundTransactionType, StringComparison.Ordinal);

    /// <summary>
    /// PRD-115: a refund's external reference identifies the outgoing payment, so a second refund under
    /// the same reference is a retry, not a new refund. Refusing it keeps a lost response from moving
    /// held cash and posting Cash twice. Returns the conflict message, or null when the reference is new.
    /// </summary>
    public static string? FindDuplicateRefund(IEnumerable<CashTransactionDto> existing, string externalRef)
        => existing.Any(transaction =>
                string.Equals(transaction.TransactionType, UnappliedCashRefundTransactionType, StringComparison.Ordinal) &&
                string.Equals(transaction.ExternalRef?.Trim(), externalRef.Trim(), StringComparison.Ordinal))
            ? $"A refund under external reference '{externalRef.Trim()}' is already recorded for this loan."
            : null;

    /// <summary>
    /// PRD-115: the amount to apply or refund must be positive and no more than the loan's unapplied
    /// cash. Returns the validation message, or null when the amount is acceptable.
    /// </summary>
    public static string? ValidateUnappliedCashAmount(decimal amount, decimal unappliedCash, string action)
        => amount <= 0m
            ? $"Amount to {action} must be positive."
            : amount > unappliedCash
                ? $"Amount to {action} ({amount:0.00}) exceeds the loan's unapplied cash ({unappliedCash:0.00})."
                : null;

    /// <summary>
    /// Resolves a mixed payment exactly as the F# servicing aggregate does: allocate to amounts owed
    /// (the automatic waterfall, or a manual breakdown capped at the amount owed), then route any
    /// excess by the loan's <see cref="ExcessPaymentApplication"/> rule (PRD-115).
    /// </summary>
    public static MixedPaymentResolutionDto ResolveMixedPayment(
        LoanServicingStateDto servicing,
        DirectLendingTermsDto terms,
        decimal amount,
        PaymentBreakdownDto? requestedBreakdown,
        DateOnly effectiveDate)
    {
        ArgumentNullException.ThrowIfNull(servicing);
        ArgumentNullException.ThrowIfNull(terms);
        if (amount <= 0m)
        {
            return new MixedPaymentResolutionDto(new PaymentBreakdownDto(0m, 0m, 0m, 0m, 0m), "Invalid", null, amount);
        }

        return RouteExcess(servicing, terms, AllocateToAmountsOwed(servicing, terms, amount, requestedBreakdown, effectiveDate));
    }

    /// <summary>
    /// Allocates cash to amounts owed, without routing what is left: the automatic waterfall, or a
    /// manual breakdown capped at what is owed. <see cref="MixedPaymentResolutionDto.UnappliedAmount"/>
    /// is the cash not allocated. Mirrors the F# aggregate's <c>autoAllocate</c> / <c>buildResolution</c>.
    /// </summary>
    public static MixedPaymentResolutionDto AllocateToAmountsOwed(
        LoanServicingStateDto servicing,
        DirectLendingTermsDto terms,
        decimal amount,
        PaymentBreakdownDto? requestedBreakdown,
        DateOnly effectiveDate)
    {
        var remaining = amount;
        var owed = servicing.Balances;
        if (requestedBreakdown is not null)
        {
            // A manual breakdown says where cash goes, but never more than is owed in a bucket:
            // allocating 1,000 to interest when 10 is accrued would record 990 of income that
            // does not exist. Each line is capped at the requested amount, the cash still
            // unallocated, and the bucket's outstanding balance; the rest is returned unallocated.
            decimal Take(decimal requested, decimal outstanding)
            {
                var applied = Math.Min(remaining, Math.Min(Math.Max(0m, requested), Math.Max(0m, outstanding)));
                remaining -= applied;
                return applied;
            }

            var manual = new PaymentBreakdownDto(
                Take(requestedBreakdown.ToInterest, owed.InterestAccruedUnpaid),
                Take(requestedBreakdown.ToCommitmentFee, owed.CommitmentFeeAccruedUnpaid),
                Take(requestedBreakdown.ToFees, owed.FeesAccruedUnpaid),
                Take(requestedBreakdown.ToPenalty, owed.PenaltyAccruedUnpaid),
                Take(requestedBreakdown.ToPrincipal, owed.PrincipalOutstanding));

            return new MixedPaymentResolutionDto(manual, "Manual", "manual-v1", remaining);
        }

        decimal TakeAll(decimal outstanding)
        {
            var applied = remaining <= 0m || outstanding <= 0m ? 0m : Math.Min(remaining, outstanding);
            remaining -= applied;
            return applied;
        }

        var toInterest = TakeAll(owed.InterestAccruedUnpaid);
        var toCommitmentFee = TakeAll(owed.CommitmentFeeAccruedUnpaid);
        var toFees = TakeAll(owed.FeesAccruedUnpaid);
        var toPenalty = TakeAll(owed.PenaltyAccruedUnpaid);
        // During the interest-only period scheduled principal is 0 in the automatic waterfall.
        var toPrincipal = DirectLendingInterop.IsInterestOnlyPeriod(terms.OriginationDate, terms.InterestOnlyMonths, effectiveDate)
            ? 0m
            : TakeAll(owed.PrincipalOutstanding);

        return new MixedPaymentResolutionDto(
            new PaymentBreakdownDto(toInterest, toCommitmentFee, toFees, toPenalty, toPrincipal),
            "WaterfallAuto",
            "waterfall-v1",
            remaining);
    }

    /// <summary>
    /// Routes the cash left after allocation (<see cref="MixedPaymentResolutionDto.UnappliedAmount"/> on
    /// entry) by the loan's excess rule. Mirrors <c>ServicingAggregate.routeExcess</c> in F#.
    /// </summary>
    public static MixedPaymentResolutionDto RouteExcess(LoanServicingStateDto servicing, DirectLendingTermsDto terms, MixedPaymentResolutionDto allocated)
    {
        var excess = allocated.UnappliedAmount;
        if (excess <= 0m)
        {
            return allocated;
        }

        var rule = terms.EffectiveExcessPaymentApplication;
        MixedPaymentResolutionDto Routed(decimal held, decimal toPrincipal, decimal toPrepaid, decimal penalty, string? note)
            => allocated with
            {
                UnappliedAmount = held,
                ExcessToPrincipal = toPrincipal,
                ExcessToPrepaidInterest = toPrepaid,
                ExcessRuleApplied = rule,
                ExcessRuleNote = note,
                PrepaymentPenaltyCharged = penalty
            };

        switch (rule)
        {
            case ExcessPaymentApplication.PrincipalPrepayment when !terms.PrepaymentAllowed:
                return Routed(excess, 0m, 0m, 0m, ExcessPaymentRuleNotes.PrepaymentNotAllowed);

            case ExcessPaymentApplication.PrincipalPrepayment:
                {
                    var principalAfterAllocation = Math.Max(0m, servicing.Balances.PrincipalOutstanding - allocated.Breakdown.ToPrincipal);
                    var toPrincipal = Math.Min(excess, principalAfterAllocation);
                    var held = excess - toPrincipal;
                    var rate = terms.PrepaymentPenaltyRate ?? 0m;
                    var penalty = rate > 0m && toPrincipal > 0m
                        ? Math.Round(toPrincipal * rate, 2, MidpointRounding.AwayFromZero)
                        : 0m;
                    return Routed(held, toPrincipal, 0m, penalty, held > 0m ? ExcessPaymentRuleNotes.ExceedsOutstandingPrincipal : null);
                }

            case ExcessPaymentApplication.PrepaidInterest:
                return Routed(0m, 0m, excess, 0m, null);

            default:
                return Routed(excess, 0m, 0m, 0m, null);
        }
    }

    public static DirectLendingPersistenceBatch WithOutbox(
        Guid loanId,
        Guid sourceEventId,
        string eventType,
        DateOnly? effectiveDate,
        long servicingRevision,
        DirectLendingEventWriteMetadata metadata,
        DirectLendingPersistenceBatch? batch,
        bool includeReconciliation = true)
    {
        if (metadata.ReplayFlag)
        {
            return batch ?? DirectLendingPersistenceBatch.Empty;
        }

        var messages = new List<DirectLendingOutboxMessageWrite>();
        messages.Add(CreateOutboxMessage("direct-lending.projection.requested"));
        messages.Add(CreateOutboxMessage("direct-lending.journal.requested"));
        if (includeReconciliation)
        {
            messages.Add(CreateOutboxMessage("direct-lending.reconciliation.requested"));
        }

        return new DirectLendingPersistenceBatch(
            batch?.CashTransactions ?? [],
            batch?.PaymentAllocations ?? [],
            batch?.FeeBalances ?? [],
            (batch?.OutboxMessages ?? []).Concat(messages).ToArray());

        DirectLendingOutboxMessageWrite CreateOutboxMessage(string topic)
            => new(
                topic,
                loanId.ToString("D"),
                JsonSerializer.Serialize(new
                {
                    loanId,
                    sourceEventId,
                    eventType,
                    effectiveDate,
                    servicingRevision,
                    metadata.CommandId,
                    metadata.CorrelationId,
                    metadata.CausationId,
                    metadata.SourceSystem
                }, EventJsonOptions),
                JsonSerializer.Serialize(new
                {
                    metadata.CommandId,
                    metadata.CorrelationId,
                    metadata.CausationId,
                    metadata.SourceSystem
                }, EventJsonOptions),
                DateTimeOffset.UtcNow,
                (DateTimeOffset?)null);
    }

    public static IReadOnlyList<DrawdownLotDto> ApplyPaymentToLots(IReadOnlyList<DrawdownLotDto> lots, decimal appliedAmount)
    {
        var updated = lots.ToArray();
        var remaining = appliedAmount;
        for (var i = 0; i < updated.Length && remaining > 0m; i++)
        {
            var lot = updated[i];
            if (lot.RemainingPrincipal <= 0m)
            {
                continue;
            }

            var appliedToLot = Math.Min(remaining, lot.RemainingPrincipal);
            updated[i] = lot with
            {
                RemainingPrincipal = lot.RemainingPrincipal - appliedToLot
            };
            remaining -= appliedToLot;
        }

        return updated;
    }

    public static IReadOnlyList<ServicingRevisionDto> PrependRevision(
        IReadOnlyList<ServicingRevisionDto> history,
        long revisionNumber,
        string sourceType,
        DateOnly effectiveAsOf,
        string notes)
    {
        return new[]
        {
            new ServicingRevisionDto(
                revisionNumber,
                sourceType,
                effectiveAsOf,
                DateTimeOffset.UtcNow,
                notes)
        }.Concat(history).ToArray();
    }

    public static T RequireSuccess<T>(DirectLendingCommandResult<T> result)
        => result.IsSuccess
            ? result.Value!
            : throw new DirectLendingCommandException(result.Error!);

    public static System.Nullable<decimal> ToNullable(decimal? value) =>
        value.HasValue ? new System.Nullable<decimal>(value.Value) : new System.Nullable<decimal>();
}

/// <summary>Outcome of <see cref="DirectLendingServiceSupport.FindCommittedCommand"/>.</summary>
public enum CommittedCommandMatch
{
    /// <summary>The command id is not recorded: process the command.</summary>
    None,

    /// <summary>The same request already committed: return the committed state.</summary>
    Replay,

    /// <summary>The command id was used for a different mutation or different inputs.</summary>
    Conflict
}

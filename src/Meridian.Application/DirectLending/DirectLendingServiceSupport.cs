using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Meridian.Contracts.DirectLending;
using Meridian.Storage.DirectLending;

namespace Meridian.Application.DirectLending;

internal static class DirectLendingServiceSupport
{
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

        return null;
    }

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
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(json));
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

    public static MixedPaymentResolutionDto ResolveMixedPayment(LoanServicingStateDto servicing, decimal amount, PaymentBreakdownDto? requestedBreakdown)
    {
        if (amount <= 0m)
        {
            return new MixedPaymentResolutionDto(new PaymentBreakdownDto(0m, 0m, 0m, 0m, 0m), "Invalid", null, amount);
        }

        var remaining = amount;
        PaymentBreakdownDto resolved;

        if (requestedBreakdown is not null)
        {
            resolved = new PaymentBreakdownDto(
                Math.Min(remaining, Math.Max(0m, requestedBreakdown.ToInterest)),
                0m,
                0m,
                0m,
                0m);
            remaining -= resolved.ToInterest;

            resolved = resolved with
            {
                ToCommitmentFee = Math.Min(remaining, Math.Max(0m, requestedBreakdown.ToCommitmentFee))
            };
            remaining -= resolved.ToCommitmentFee;

            resolved = resolved with
            {
                ToFees = Math.Min(remaining, Math.Max(0m, requestedBreakdown.ToFees))
            };
            remaining -= resolved.ToFees;

            resolved = resolved with
            {
                ToPenalty = Math.Min(remaining, Math.Max(0m, requestedBreakdown.ToPenalty))
            };
            remaining -= resolved.ToPenalty;

            resolved = resolved with
            {
                ToPrincipal = Math.Min(remaining, Math.Max(0m, requestedBreakdown.ToPrincipal))
            };
            remaining -= resolved.ToPrincipal;

            return new MixedPaymentResolutionDto(resolved, "Manual", "v1", remaining);
        }

        var balances = servicing.Balances;
        var toInterest = Math.Min(remaining, balances.InterestAccruedUnpaid);
        remaining -= toInterest;
        var toCommitmentFee = Math.Min(remaining, balances.CommitmentFeeAccruedUnpaid);
        remaining -= toCommitmentFee;
        var toFees = Math.Min(remaining, balances.FeesAccruedUnpaid);
        remaining -= toFees;
        var toPenalty = Math.Min(remaining, balances.PenaltyAccruedUnpaid);
        remaining -= toPenalty;
        var toPrincipal = Math.Min(remaining, balances.PrincipalOutstanding);
        remaining -= toPrincipal;

        return new MixedPaymentResolutionDto(
            new PaymentBreakdownDto(toInterest, toCommitmentFee, toFees, toPenalty, toPrincipal),
            "WaterfallAuto",
            "v1",
            remaining);
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

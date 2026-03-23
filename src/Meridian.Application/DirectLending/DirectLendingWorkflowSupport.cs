using System.Text.Json;
using Meridian.Contracts.DirectLending;
using Meridian.Storage.DirectLending;

namespace Meridian.Application.DirectLending;

internal static class DirectLendingWorkflowSupport
{
    public static DirectLendingCommandResult<PaymentBreakdownDto> ResolveBreakdown(LoanServicingStateDto servicing, decimal amount, PaymentBreakdownDto? requested)
    {
        if (amount <= 0m)
        {
            return DirectLendingCommandResult<PaymentBreakdownDto>.Failure(DirectLendingErrorCode.Validation, "Payment amount must be positive.");
        }

        var breakdown = requested ?? AutoAllocate(servicing, amount);
        if (breakdown.TotalAllocated > amount)
        {
            return DirectLendingCommandResult<PaymentBreakdownDto>.Failure(DirectLendingErrorCode.Validation, "Payment breakdown exceeds payment amount.");
        }

        return DirectLendingCommandResult<PaymentBreakdownDto>.Success(breakdown);
    }

    public static PaymentBreakdownDto AutoAllocate(LoanServicingStateDto servicing, decimal amount)
    {
        var remaining = amount;
        var toInterest = Take(ref remaining, servicing.Balances.InterestAccruedUnpaid);
        var toCommitmentFee = Take(ref remaining, servicing.Balances.CommitmentFeeAccruedUnpaid);
        var toFees = Take(ref remaining, servicing.Balances.FeesAccruedUnpaid);
        var toPenalty = Take(ref remaining, servicing.Balances.PenaltyAccruedUnpaid);
        var toPrincipal = Take(ref remaining, servicing.Balances.PrincipalOutstanding);

        return new PaymentBreakdownDto(toInterest, toCommitmentFee, toFees, toPenalty, toPrincipal);
    }

    public static MixedPaymentResolutionDto BuildResolution(PaymentBreakdownDto breakdown, decimal amount, bool manual)
        => new(breakdown, manual ? "Manual" : "WaterfallAuto", manual ? "manual-v1" : "waterfall-v1", amount - breakdown.TotalAllocated);

    public static DirectLendingPersistenceBatch CreatePersistenceBatch(
        Guid loanId,
        long aggregateVersion,
        string eventType,
        DateOnly? effectiveDate,
        DirectLendingCommandMetadataDto metadata,
        IReadOnlyList<DirectLendingCashTransactionWrite>? cashTransactions = null,
        IReadOnlyList<DirectLendingPaymentAllocationWrite>? paymentAllocations = null,
        IReadOnlyList<DirectLendingFeeBalanceWrite>? feeBalances = null,
        bool requestProjection = false,
        bool requestJournal = false,
        bool requestReconciliation = false)
    {
        var messages = new List<DirectLendingOutboxMessageWrite>();
        var payload = JsonSerializer.Serialize(new DirectLendingWorkflowMessage(
            loanId,
            aggregateVersion,
            eventType,
            effectiveDate,
            metadata.CommandId,
            metadata.CorrelationId,
            metadata.CausationId,
            metadata.SourceSystem));

        if (requestProjection)
        {
            messages.Add(new DirectLendingOutboxMessageWrite(
                DirectLendingWorkflowTopics.ProjectionRequested,
                $"{loanId}:projection:{aggregateVersion}",
                payload,
                HeadersJson: null,
                OccurredAt: DateTimeOffset.UtcNow,
                VisibleAfter: null));
        }

        if (requestJournal)
        {
            messages.Add(new DirectLendingOutboxMessageWrite(
                DirectLendingWorkflowTopics.JournalRequested,
                $"{loanId}:journal:{aggregateVersion}",
                payload,
                HeadersJson: null,
                OccurredAt: DateTimeOffset.UtcNow,
                VisibleAfter: null));
        }

        if (requestReconciliation)
        {
            messages.Add(new DirectLendingOutboxMessageWrite(
                DirectLendingWorkflowTopics.ReconciliationRequested,
                $"{loanId}:recon:{aggregateVersion}",
                payload,
                HeadersJson: null,
                OccurredAt: DateTimeOffset.UtcNow,
                VisibleAfter: null));
        }

        return new DirectLendingPersistenceBatch(
            cashTransactions ?? [],
            paymentAllocations ?? [],
            feeBalances ?? [],
            messages);
    }

    public static DirectLendingCommandMetadataDto ToCommandMetadata(DirectLendingWorkflowMessage message)
        => new(message.CommandId, message.CorrelationId, message.CausationId, message.SourceSystem, ReplayFlag: false);

    public static decimal Take(ref decimal remaining, decimal available)
    {
        if (remaining <= 0m || available <= 0m)
        {
            return 0m;
        }

        var applied = Math.Min(remaining, available);
        remaining -= applied;
        return applied;
    }
}

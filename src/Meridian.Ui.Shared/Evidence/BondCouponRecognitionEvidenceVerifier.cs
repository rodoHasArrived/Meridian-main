using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.AssetOperations;
using Meridian.Instruments.AssetOperations;

namespace Meridian.Ui.Shared.Evidence;

/// <summary>Rechecks exact observed bank cash and independently reviewed deductions before recognition.</summary>
public sealed class BondCouponRecognitionEvidenceVerifier(IEvidenceArtifactStore vault, TimeProvider? clock = null)
    : IBondCouponRecognitionEvidenceVerifier
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    public Task VerifyAsync(AccountCashForecastScopeDto scope, IReadOnlyList<BondCouponRecognizedCashDto> cash,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(cash);
        if (!AccountCashForecastEngine.IsCompleteScope(scope) || cash.Any(row => row is null || row.Allocation is null
            || row.RetainedAllocation is null || row.ObservedPayment?.Provenance?.Evidence is null))
            throw Failure("Coupon clearing requires exact retained cash and allocation sources.");
        foreach (var row in cash)
        {
            var claimed = row.Allocation;
            var retained = row.RetainedAllocation;
            var payment = row.ObservedPayment;
            if (payment.AccountId != scope.AccountId || payment.BookId != scope.BookId || payment.Currency != scope.BaseCurrency
                || payment.Status != AccountCashFlowStatus.Actual || payment.FlowType != "Coupon"
                || payment.Provenance.Role != AccountCashEvidenceRole.Observation || row.ObservedPaymentAmount != payment.Amount
                || claimed.PaymentVersionId != payment.VersionId || retained.ActualVersionId != payment.VersionId
                || !BondCouponRecognitionCalculator.IsBoundedShare(claimed, retained)
                || claimed.DeductionEvidence != retained.DeductionEvidence
                || retained.DeductionEvidence is { Role: not AccountCashEvidenceRole.Observation })
                throw Failure("Coupon clearing differs from the exact observed account cash or retained gross/net deduction allocation.");
        }
        if (cash.Select(row => row.Allocation.AllocationId).Distinct(StringComparer.Ordinal).Count() != cash.Count
            || cash.GroupBy(row => row.ObservedPayment.VersionId, StringComparer.Ordinal)
                .Any(group => group.Any(row => row.ObservedPayment != group.First().ObservedPayment)))
            throw Failure("Coupon clearing source identities are duplicated or conflicting.");
        var now = _clock.GetUtcNow();
        var date = DateOnly.FromDateTime(now.UtcDateTime);
        var request = new AccountCashForecastRequestDto("coupon-recognition-source-verification", scope,
            date, date, null, null, cash.Select(row => row.ObservedPayment).DistinctBy(row => row.VersionId).ToArray(), [], now)
        { CalculationVersion = "account-cash-forecast-v2", PaymentAllocations = cash.Select(row => row.RetainedAllocation).ToArray() };
        return new AccountCashForecastEvidenceAdapter(vault).VerifyAsync(request, ct);
    }

    private static AccountCashForecastSourceAuthorityException Failure(string reason) => new(reason);
}

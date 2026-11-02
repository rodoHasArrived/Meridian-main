using FluentAssertions;
using Meridian.Application.DirectLending;
using Meridian.Contracts.DirectLending;
using Xunit;

namespace Meridian.Tests.Application.DirectLending;

/// <summary>
/// A manual payment breakdown must never allocate more to a bucket than the loan owes there:
/// otherwise the allocation records income (or principal reduction) that never existed.
/// </summary>
public sealed class ManualPaymentResolutionTests
{
    private static LoanServicingStateDto Servicing(decimal principal, decimal interest, decimal fees = 0m) =>
        new(
            Guid.NewGuid(),
            LoanStatus.Active,
            CurrentCommitment: 1_000_000m,
            TotalDrawn: principal,
            AvailableToDraw: 1_000_000m - principal,
            new OutstandingBalancesDto(principal, interest, 0m, fees, 0m),
            DrawdownLots: [],
            CurrentRateReset: null,
            LastAccrualDate: null,
            LastPaymentDate: null,
            ServicingRevision: 1,
            RevisionHistory: [],
            AccrualEntries: []);

    [Fact]
    public void ManualLineAboveTheAmountOwed_IsCappedAndExcessReportedUnapplied()
    {
        // 10 of interest is owed; the operator types 1,000 to interest.
        var resolution = DirectLendingServiceSupport.ResolveMixedPayment(
            Servicing(principal: 0m, interest: 10m),
            amount: 1_000m,
            new PaymentBreakdownDto(1_000m, 0m, 0m, 0m, 0m));

        resolution.ResolutionBasis.Should().Be("Manual");
        resolution.Breakdown.ToInterest.Should().Be(10m);
        resolution.UnappliedAmount.Should().Be(990m);
    }

    [Fact]
    public void ManualLines_AreCappedByOwedAndByRemainingCash()
    {
        var resolution = DirectLendingServiceSupport.ResolveMixedPayment(
            Servicing(principal: 5_000m, interest: 40m, fees: 25m),
            amount: 1_000m,
            new PaymentBreakdownDto(100m, 0m, 100m, 0m, 2_000m));

        resolution.Breakdown.ToInterest.Should().Be(40m);
        resolution.Breakdown.ToFees.Should().Be(25m);
        resolution.Breakdown.ToPrincipal.Should().Be(935m, "principal takes the cash left, not the 2,000 requested");
        resolution.UnappliedAmount.Should().Be(0m);
    }
}

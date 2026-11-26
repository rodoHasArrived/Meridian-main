using FluentAssertions;
using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.Workstation;
using Meridian.Ui.Shared.Evidence;

namespace Meridian.Tests.Ui;

public sealed class BondCouponRecognitionEvidenceVerifierTests
{
    [Fact]
    public async Task PostedCashSourceMustStillHaveExactAcceptedReviewAtFinalVerification()
    {
        using var source = new AccountCashForecastEvidenceAdapterTests();
        var request = await source.ValidRequestAsync();
        var payment = request.Flows[0];
        var scope = request.Scope with { BaseCurrency = payment.Currency };
        var allocation = new BondCouponCashAllocationDto("coupon-clear", payment.VersionId, payment.Amount, payment.Amount);
        var cash = new BondCouponRecognizedCashDto(allocation, payment.Amount, payment,
            new(allocation.AllocationId, "coupon-1", payment.VersionId, payment.Amount, payment.Amount));
        var verifier = new BondCouponRecognitionEvidenceVerifier(source.Store());
        await verifier.VerifyAsync(scope, [cash]);
        var evidence = payment.Provenance.Evidence;
        EvidenceVaultReference.TryParseCanonical(evidence.EvidenceUri, out var vault).Should().BeTrue();
        await source.Store().ReviewDocumentAsync(vault, evidence.EvidenceId, scope.TenantId, scope.CompanyId,
            new(EvidenceDocumentReviewStatusDto.Rejected, "revocation-reviewer", "Bank source no longer establishes cash authority."));
        var postVerification = () => verifier.VerifyAsync(scope, [cash]);
        await postVerification.Should().ThrowAsync<AccountCashForecastSourceAuthorityException>();
    }

    [Fact]
    public async Task RecognitionCannotSubstituteGrossNetOrAccountAgainstRetainedCash()
    {
        using var source = new AccountCashForecastEvidenceAdapterTests();
        var request = await source.ValidRequestAsync();
        var payment = request.Flows[0];
        var scope = request.Scope with { BaseCurrency = payment.Currency };
        var allocation = new BondCouponCashAllocationDto("coupon-clear", payment.VersionId, payment.Amount, payment.Amount);
        var cash = new BondCouponRecognizedCashDto(allocation, payment.Amount, payment,
            new(allocation.AllocationId, "coupon-1", payment.VersionId, payment.Amount, payment.Amount));
        var verifier = new BondCouponRecognitionEvidenceVerifier(source.Store());
        var replaced = () => verifier.VerifyAsync(scope, [cash with { Allocation = allocation with { GrossAmount = payment.Amount + 1m } }]);
        await replaced.Should().ThrowAsync<AccountCashForecastSourceAuthorityException>();
        var foreign = () => verifier.VerifyAsync(scope with { AccountId = Guid.NewGuid().ToString("D") }, [cash]);
        await foreign.Should().ThrowAsync<AccountCashForecastSourceAuthorityException>();
    }

    [Fact]
    public async Task LotShareReopensFullRetainedPaymentWithoutReplacingItsAcceptedEconomics()
    {
        using var source = new AccountCashForecastEvidenceAdapterTests();
        var request = await source.ValidRequestAsync();
        var payment = request.Flows[0];
        var scope = request.Scope with { BaseCurrency = payment.Currency };
        var share = new BondCouponCashAllocationDto("coupon-clear", payment.VersionId, payment.Amount / 2m, payment.Amount / 2m);
        var full = new Meridian.Contracts.AssetOperations.AccountCashPaymentAllocationDto("coupon-clear", "coupon-1",
            payment.VersionId, payment.Amount, payment.Amount);
        await new BondCouponRecognitionEvidenceVerifier(source.Store()).VerifyAsync(scope,
            [new(share, payment.Amount, payment, full)]);
        Assert.Equal(payment.Amount, full.GrossAmount);
    }
}

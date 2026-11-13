using FluentAssertions;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.Workstation;
using Meridian.Ui.Shared.Evidence;
using Meridian.Ui.Shared.Services;

namespace Meridian.Tests.Ui;

public sealed class BondPerformanceSourceVerifierTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CachedCashAndFxCannotOutliveAcceptedSourceReview(bool fx)
    {
        using var source = new AccountCashForecastEvidenceAdapterTests();
        var cash = await source.ValidRequestAsync();
        var verifier = new BondPerformanceSourceVerifier(source.Store());
        var monitoring = new BondMonitoringCaptureRequest("performance", cash.Scope, DateTimeOffset.UtcNow,
            [], cash.Flows, [], [], []);
        if (fx)
            await verifier.VerifyFxAsync(cash.Scope, cash.FxConversions);
        else
            await verifier.VerifyMonitoringAsync(monitoring);
        var evidence = fx ? cash.FxConversions[0].Provenance.Evidence : cash.Flows[0].Provenance.Evidence;
        EvidenceVaultReference.TryParseCanonical(evidence.EvidenceUri, out var vault).Should().BeTrue();
        await source.Store().ReviewDocumentAsync(vault, evidence.EvidenceId, cash.Scope.TenantId, cash.Scope.CompanyId,
            new(EvidenceDocumentReviewStatusDto.Rejected, "revoking-reviewer", "Source authority withdrawn after capture."));
        Func<Task> reused = fx ? () => verifier.VerifyFxAsync(cash.Scope, cash.FxConversions)
            : () => verifier.VerifyMonitoringAsync(monitoring);
        await reused.Should().ThrowAsync<AccountCashForecastSourceAuthorityException>();
    }

    [Fact]
    public async Task FxCannotChangeRateOrOwnershipWhileReusingRetainedSourceIdentity()
    {
        using var source = new AccountCashForecastEvidenceAdapterTests();
        var cash = await source.ValidRequestAsync();
        var verifier = new BondPerformanceSourceVerifier(source.Store());
        await verifier.VerifyFxAsync(cash.Scope, cash.FxConversions);
        var changedRate = () => verifier.VerifyFxAsync(cash.Scope,
            [cash.FxConversions[0] with { BaseUnitsPerCurrencyUnit = 99m }]);
        await changedRate.Should().ThrowAsync<AccountCashForecastSourceAuthorityException>();
        var foreign = () => verifier.VerifyFxAsync(cash.Scope with { AccountId = Guid.NewGuid().ToString("D") }, cash.FxConversions);
        await foreign.Should().ThrowAsync<AccountCashForecastSourceAuthorityException>();
    }
}

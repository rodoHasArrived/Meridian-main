using FluentAssertions;
using Meridian.Application.Composition;
using Meridian.Application.Composition.Features;
using Meridian.Application.SecurityMaster;
using Meridian.FinancialOperations.Ledger;
using Meridian.Tests.SecurityMaster;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Meridian.Tests.Application.Composition;

/// <summary>
/// The ledger composition hands the registered Security Master terms capability verifier to the posting
/// service, so a new amortization batch on a node the floor refuses is refused; dropping the wiring would
/// leave amortization ungated in every host.
/// </summary>
[Trait("Category", "Unit")]
public sealed class LedgerFeatureRegistrationCapabilityFloorTests
{
    [Fact]
    public async Task ComposedPostingService_AsksTheRegisteredFloorVerifierBeforeANewAmortizationBatch()
    {
        using var provider = Compose(SecurityMasterRebuildCapabilityFloorTests.RefusingVerifier());

        var posting = provider.GetRequiredService<IAccountingPostingCandidatePostService>()
            .Should().BeOfType<AccountingPostingCandidatePostService>().Subject;

        posting.TermsCapabilityCheck.Should().NotBeNull("the registered verifier gates new amortization batches");
        await posting.TermsCapabilityCheck!.Invoking(check => check(CancellationToken.None))
            .Should().ThrowAsync<SecurityMasterTermsCapabilityRefusedException>();
    }

    [Fact]
    public async Task ComposedPostingService_CallsTheRegisteredVerifier()
    {
        var verifier = Substitute.For<ISecurityMasterTermsCapabilityVerifier>();
        using var provider = Compose(verifier);
        var posting = (AccountingPostingCandidatePostService)provider.GetRequiredService<IAccountingPostingCandidatePostService>();

        await posting.TermsCapabilityCheck!(CancellationToken.None);

        await verifier.Received(1).VerifyAsync(Arg.Any<CancellationToken>());
    }

    // The real ledger registration; only the candidate builders, whose own dependencies are other
    // features', are stood in for (the registration adds them with TryAdd).
    private static ServiceProvider Compose(ISecurityMasterTermsCapabilityVerifier verifier)
    {
        var services = new ServiceCollection().AddLogging();
        services.AddSingleton(verifier);
        services.AddSingleton(Substitute.For<IAccountingPostingCandidateWriteBuilder>());
        services.AddSingleton(Substitute.For<IAccountingPostingCandidateAuthorityBuilder>());
        new LedgerFeatureRegistration().Register(services, CompositionOptions.Minimal);
        return services.BuildServiceProvider();
    }
}

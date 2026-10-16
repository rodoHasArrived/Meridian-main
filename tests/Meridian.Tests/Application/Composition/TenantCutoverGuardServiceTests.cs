using FluentAssertions;
using Meridian.Application.Composition;
using Meridian.Contracts.Tenancy;
using Meridian.Storage.Tenancy;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Meridian.Tests.Application.Composition;

public sealed class TenantCutoverGuardServiceTests
{
    [Fact]
    public async Task ReadyDatabase_StartsWithoutChangingRetainedData()
    {
        var check = Substitute.For<ITenantCutoverReadinessCheck>();
        check.InspectAsync(Arg.Any<CancellationToken>()).Returns(new TenantCutoverReadiness([]));
        await Create(check).StartAsync(CancellationToken.None);
        await check.Received(1).InspectAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpgradeWithUnattributedRecords_RefusesWithExplicitDispositionAndRecovery()
    {
        var check = Substitute.For<ITenantCutoverReadinessCheck>();
        check.InspectAsync(Arg.Any<CancellationToken>()).Returns(new TenantCutoverReadiness(
            [new TenantCutoverFinding("ledger", "ledger_books", "missing tenant", 7)]));
        Func<Task> start = () => Create(check).StartAsync(CancellationToken.None);
        await start.Should().ThrowAsync<StartupRefusedException>()
            .WithMessage("*ledger/ledger_books: 7*retained data remains intact*--action preview*deployment-boundary*");
    }

    [Fact]
    public async Task UnavailableInspector_RefusesWithoutExposingDatabaseSecrets()
    {
        var check = Substitute.For<ITenantCutoverReadinessCheck>();
        check.InspectAsync(Arg.Any<CancellationToken>()).Returns<Task<TenantCutoverReadiness>>(
            _ => throw new InvalidOperationException("Password=secret; retained sensitive data"));
        Func<Task> start = () => Create(check).StartAsync(CancellationToken.None);
        var thrown = await start.Should().ThrowAsync<StartupRefusedException>();
        thrown.Which.ToString().Should().NotContain("secret").And.NotContain("sensitive data");
    }

    [Fact]
    public async Task ExplicitMigrationCompatibility_PreservesExistingDataAccessWithoutClaimingReadiness()
    {
        var check = Substitute.For<ITenantCutoverReadinessCheck>();
        await Create(check, TenantScopeEnforcementOptions.DeploymentBoundary).StartAsync(CancellationToken.None);
        await check.DidNotReceive().InspectAsync(Arg.Any<CancellationToken>());
    }

    private static TenantCutoverGuardService Create(ITenantCutoverReadinessCheck check, TenantScopeEnforcementOptions? options = null)
        => new(options ?? TenantScopeEnforcementOptions.FailClosed, check, NullLogger<TenantCutoverGuardService>.Instance);
}

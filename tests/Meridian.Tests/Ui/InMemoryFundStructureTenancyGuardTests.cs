using FluentAssertions;
using Meridian.Application.Composition;
using Meridian.Application.FundStructure;
using Meridian.Contracts.Services;
using Meridian.Identity;
using Meridian.Identity.Auth;
using Meridian.PortfolioRecords.FundAccounts;
using Meridian.Ui.Shared.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Meridian.Tests.Ui;

/// <summary>
/// W9-GOV-008 criterion 2. <c>InMemoryFundStructureService</c> carries no tenant or company
/// identifier anywhere, so every session it serves shares one graph. The criterion allowed either
/// partitioning it or refusing multi-company access; these tests pin the refusal, and pin that it
/// leaves the single-company deployments that actually run this posture alone.
/// </summary>
public sealed class InMemoryFundStructureTenancyGuardTests
{
    [Fact]
    public async Task Guard_RefusesToStartWhenAnUnpartitionedStoreWouldServeSeveralCompanies()
    {
        var guard = CreateGuard(
            new InMemoryFundStructureService(new InMemoryFundAccountService()),
            "company-alpha",
            "company-beta");

        var start = async () => await guard.StartAsync(CancellationToken.None);

        (await start.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should().Contain("fund structure");
    }

    [Fact]
    public async Task Guard_RaisesARefusalRatherThanAnOrdinaryStartupFailure()
    {
        // The distinction is load-bearing, not cosmetic. A host that tolerates a worker failing to
        // start -- the desktop shell does, on purpose, so a projection pump that cannot reach its
        // database does not take the application down -- has nothing to tell a refusal apart by
        // unless the type says so, and swallows it. That is what happened to this guard on the WPF
        // lane in PR #2866: registered, throwing, and absorbed by the catch one frame up.
        var guard = CreateGuard(
            new InMemoryFundStructureService(new InMemoryFundAccountService()),
            "company-alpha",
            "company-beta");

        var start = async () => await guard.StartAsync(CancellationToken.None);

        (await start.Should().ThrowAsync<StartupRefusedException>())
            .Which.Should().BeAssignableTo<InvalidOperationException>(
                "every existing catch and assertion naming that type has to keep matching");
    }

    [Fact]
    public async Task ComposedHost_FailsToStartRatherThanServingTheUnpartitionedStructure()
    {
        // End to end through a real host: the refusal has to survive IHostedService orchestration
        // and surface from StartAsync, which is the moment any shell decides whether to carry on.
        using var host = new Microsoft.Extensions.Hosting.HostBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton<IFundStructureService>(
                    new InMemoryFundStructureService(new InMemoryFundAccountService()));
                services.AddSingleton(AccountStoreFor("company-alpha", "company-beta"));
                services.AddLogging();
                services.AddHostedService<InMemoryFundStructureTenancyGuard>();
            })
            .Build();

        var start = async () => await host.StartAsync(CancellationToken.None);

        var refusal = await start.Should().ThrowAsync<Exception>();
        HostStartupEscalation.IsRefusal(refusal.Which).Should().BeTrue();
    }

    [Fact]
    public void Escalation_TreatsAnOrdinaryWorkerFailureAsDegradableAndARefusalAsFatal()
    {
        // The rule the desktop shell's two catch clauses are selected by. Its own startup path
        // cannot be run off Windows, so this is where the rule is actually exercised.
        HostStartupEscalation.IsRefusal(new InvalidOperationException("database unreachable"))
            .Should().BeFalse();
        HostStartupEscalation.IsRefusal(null).Should().BeFalse();
        HostStartupEscalation.IsRefusal(new StartupRefusedException("refused")).Should().BeTrue();

        // Hosts may start services concurrently and may wrap; a wrapped refusal is still a refusal,
        // and one refusal among several faults decides the batch -- there is no partial refusal.
        HostStartupEscalation.IsRefusal(
            new InvalidOperationException("outer", new StartupRefusedException("refused")))
            .Should().BeTrue();
        HostStartupEscalation.IsRefusal(new AggregateException(
            new InvalidOperationException("database unreachable"),
            new StartupRefusedException("refused")))
            .Should().BeTrue();
        HostStartupEscalation.IsRefusal(new AggregateException(
            new InvalidOperationException("database unreachable")))
            .Should().BeFalse();
    }

    [Fact]
    public async Task Guard_LeavesASingleCompanyDeploymentAlone()
    {
        var guard = CreateGuard(
            new InMemoryFundStructureService(new InMemoryFundAccountService()),
            "company-alpha",
            "company-alpha");

        await guard.StartAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Guard_LeavesADeploymentWithNoConfiguredCompanyAlone()
    {
        var guard = CreateGuard(
            new InMemoryFundStructureService(new InMemoryFundAccountService()),
            null,
            null);

        await guard.StartAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Guard_DoesNotConstrainAPartitionedStore()
    {
        // The Postgres service carries the tenant column and the scoping this guard substitutes for,
        // so several companies on it is the supported arrangement, not a refusal.
        var partitioned = Substitute.For<IFundStructureService>();
        var guard = CreateGuard(partitioned, "company-alpha", "company-beta");

        await guard.StartAsync(CancellationToken.None);
    }

    private static InMemoryFundStructureTenancyGuard CreateGuard(
        IFundStructureService fundStructureService,
        params string?[] accountCompanyIds)
        => new(
            fundStructureService,
            AccountStoreFor(accountCompanyIds),
            NullLogger<InMemoryFundStructureTenancyGuard>.Instance);

    private static IUserAccountStore AccountStoreFor(params string?[] accountCompanyIds)
    {
        var accountStore = Substitute.For<IUserAccountStore>();
        accountStore.LoadAccounts().Returns(
        [
            .. accountCompanyIds.Select((companyId, index) => new UserAccountConfig(
                $"operator-{index.ToString()}",
                new string('0', 64),
                UserRole.ReadOnly,
                CompanyId: companyId)),
        ]);

        return accountStore;
    }
}

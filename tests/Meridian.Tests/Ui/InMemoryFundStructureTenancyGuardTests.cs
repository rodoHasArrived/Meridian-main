using FluentAssertions;
using Meridian.Application.FundStructure;
using Meridian.Contracts.Services;
using Meridian.Identity;
using Meridian.Identity.Auth;
using Meridian.PortfolioRecords.FundAccounts;
using Meridian.Ui.Shared.Services;
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

        return new InMemoryFundStructureTenancyGuard(
            fundStructureService,
            accountStore,
            NullLogger<InMemoryFundStructureTenancyGuard>.Instance);
    }
}

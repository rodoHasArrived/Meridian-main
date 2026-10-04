using Meridian.Application.Tenancy;
using System.Reflection;
using Meridian.Application.Composition;
using Meridian.Application.FundStructure;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Services;
using Meridian.Contracts.Tenancy;
using Meridian.PortfolioRecords.Accounts;
using Meridian.PortfolioRecords.FundAccounts;
using Meridian.Wpf.Features.Accounting;
using Meridian.Wpf.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Wpf.Tests.Services;

public sealed class DesktopLocalTenantIsolationTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("tenant-alpha")]
    [InlineData("tenant-beta")]
    public async Task StrictLocalServices_RefuseEveryReadAndMutationForUnattributedSnapshots(string? callerTenant)
    {
        using var authority = callerTenant is null ? null : FundScopeTenantAuthority.Enter(callerTenant, "retained work");
        var rawAccounts = new InMemoryFundAccountService();
        var gate = new LocalTenantMigrationGate(TenantScopeEnforcementOptions.FailClosed);
        var accounts = new TenantGuardedLocalFundAccountService(rawAccounts, gate);
        var structure = new TenantGuardedLocalFundStructureService(new InMemoryFundStructureService(rawAccounts), gate);

        // Sweep every public contract, including account aliases, so adding an operation without
        // its migration gate cannot quietly reopen a desktop-local read or write path.
        foreach (var (contract, service) in new (Type, object)[]
        {
            (typeof(IFundStructureService), structure),
            (typeof(IFundAccountService), accounts),
            (typeof(IAccountManagementService), accounts),
            (typeof(IAccountQueryService), accounts)
        })
        {
            foreach (var method in contract.GetMethods())
            {
                var arguments = method.GetParameters().Select(parameter => parameter.ParameterType.IsValueType
                    ? Activator.CreateInstance(parameter.ParameterType) : null).ToArray();
                Func<Task> operation = async () =>
                {
                    try
                    {
                        await (Task)method.Invoke(service, arguments)!;
                    }
                    catch (TargetInvocationException exception) when (exception.InnerException is not null)
                    {
                        System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
                        throw;
                    }
                };
                await operation.Should().ThrowAsync<LocalTenantMigrationRequiredException>(
                    $"{contract.Name}.{method.Name} must refuse access to unattributed desktop records")
                    .WithMessage(LocalTenantMigrationGate.RefusalMessage);
            }
        }
        (await rawAccounts.QueryAccountsAsync(new())).Should().BeEmpty();
    }

    [Fact]
    public async Task AccountingRegistration_UsesOneGuardedAccountInstanceAcrossEveryAlias()
    {
        var services = new ServiceCollection();
        new AccountingFeatureModule().Register(services);
        var guarded = new TenantGuardedLocalFundAccountService(new InMemoryFundAccountService(),
            new LocalTenantMigrationGate(TenantScopeEnforcementOptions.FailClosed));
        // Substitute only the backing persistence path; exercise the module's real alias factories.
        services.AddSingleton(guarded);
        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IFundAccountService>().Should().BeSameAs(guarded);
        provider.GetRequiredService<IAccountManagementService>().Should().BeSameAs(guarded);
        provider.GetRequiredService<IAccountQueryService>().Should().BeSameAs(guarded);
        provider.GetService<InMemoryFundAccountService>().Should().BeNull();
        Func<Task> query = () => provider.GetRequiredService<IAccountQueryService>().ListAccountsAsync(null, null, null);
        await query.Should().ThrowAsync<LocalTenantMigrationRequiredException>().WithMessage(LocalTenantMigrationGate.RefusalMessage);
    }

    [Fact]
    public async Task UpgradeRefusal_RetainsSnapshotBytesAndExplicitMigrationPostureCanReadThem()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"meridian-tenant-cutover-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var accountPath = Path.Combine(directory, "fund-accounts.json");
        var structurePath = Path.Combine(directory, "fund-structure.json");
        try
        {
            var accountId = Guid.NewGuid();
            var organizationId = Guid.NewGuid();
            var legacyAccounts = new InMemoryFundAccountService(accountPath);
            await legacyAccounts.CreateAccountAsync(new(accountId, AccountTypeDto.Bank, "LEGACY", "Retained bank",
                "USD", DateTimeOffset.UtcNow, "legacy-operator"));
            var legacyStructure = new InMemoryFundStructureService(legacyAccounts, structurePath);
            await legacyStructure.CreateOrganizationAsync(new(organizationId, "LEGACY", "Retained organization",
                "USD", DateTimeOffset.UtcNow, "legacy-operator"));
            var accountBytes = await File.ReadAllBytesAsync(accountPath);
            var structureBytes = await File.ReadAllBytesAsync(structurePath);

            var strict = new LocalTenantMigrationGate(TenantScopeEnforcementOptions.FailClosed);
            var strictAccounts = new TenantGuardedLocalFundAccountService(new InMemoryFundAccountService(accountPath), strict);
            var strictStructure = new TenantGuardedLocalFundStructureService(
                new InMemoryFundStructureService(strictAccounts, structurePath), strict);
            Func<Task> accountRead = () => strictAccounts.GetAccountAsync(accountId);
            Func<Task> structureRead = () => strictStructure.GetOrganizationStructureAsync(new(OrganizationId: organizationId));
            await accountRead.Should().ThrowAsync<LocalTenantMigrationRequiredException>().WithMessage(LocalTenantMigrationGate.RefusalMessage);
            await structureRead.Should().ThrowAsync<LocalTenantMigrationRequiredException>().WithMessage(LocalTenantMigrationGate.RefusalMessage);
            (await File.ReadAllBytesAsync(accountPath)).Should().Equal(accountBytes);
            (await File.ReadAllBytesAsync(structurePath)).Should().Equal(structureBytes);

            var migration = new LocalTenantMigrationGate(TenantScopeEnforcementOptions.DeploymentBoundary);
            var migrationAccounts = new TenantGuardedLocalFundAccountService(new InMemoryFundAccountService(accountPath), migration);
            var migrationStructure = new TenantGuardedLocalFundStructureService(
                new InMemoryFundStructureService(migrationAccounts, structurePath), migration);
            (await migrationAccounts.GetAccountAsync(accountId))!.DisplayName.Should().Be("Retained bank");
            (await migrationStructure.GetOrganizationStructureAsync(new(OrganizationId: organizationId)))
                .Organizations.Should().ContainSingle(row => row.OrganizationId == organizationId);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void GuardedLocalServices_KeepTheirNonProductionClassification()
    {
        typeof(INonProductionOnlyService).IsAssignableFrom(typeof(TenantGuardedLocalFundAccountService)).Should().BeTrue();
        typeof(INonProductionOnlyService).IsAssignableFrom(typeof(TenantGuardedLocalFundStructureService)).Should().BeTrue();
    }
}

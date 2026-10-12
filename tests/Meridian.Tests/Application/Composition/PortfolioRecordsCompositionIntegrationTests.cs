using System.Text.Json;
using FluentAssertions;
using Meridian.Application.Composition;
using Meridian.Application.Composition.Features;
using Meridian.Application.Composition.Startup;
using Meridian.Application.Services;
using Meridian.Application.Tenancy;
using Meridian.Application.UI;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Tenancy;
using Meridian.Core.Config;
using Meridian.FinancialOperations.Reconciliation;
using Meridian.PortfolioRecords.Accounts;
using Meridian.PortfolioRecords.FundAccounts;
using Meridian.Storage;
using Meridian.Storage.FundAccounts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Serilog.Core;

namespace Meridian.Tests.Application.Composition;

public sealed class PortfolioRecordsCompositionIntegrationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"meridian-portfolio-composition-{Guid.NewGuid():N}");
    private readonly Guid _accountId = Guid.NewGuid();
    private string ConfigPath => Path.Combine(_root, "appsettings.json");
    private string SnapshotPath => Path.Combine(_root, "governance", "fund-accounts.json");

    public PortfolioRecordsCompositionIntegrationTests() => Directory.CreateDirectory(_root);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RuntimeAndCommandGraphs_UseTheSameConfiguredDurableAccountAuthority(bool runtime)
    {
        var retained = await SeedLocalAccountAsync();
        var balance = new AccountBalanceSnapshotDto(Guid.NewGuid(), _accountId, null,
            new DateOnly(2026, 5, 31), "USD", 725m, null, null, null, "ledger", DateTimeOffset.UtcNow, null);
        var store = Substitute.For<IFundAccountStore>();
        store.GetAccountAsync(_accountId, Arg.Any<CancellationToken>()).Returns(retained);
        store.GetBalanceHistoryAsync(_accountId, Arg.Any<DateOnly?>(), Arg.Any<DateOnly?>(), Arg.Any<CancellationToken>())
            .Returns(new[] { balance });
        var services = new ServiceCollection();
        services.AddSingleton(store);
        var configuration = Configuration(new Dictionary<string, string?>
        {
            [FundAccountsStartup.ConnectionStringVariable] = "Host=localhost;Database=account_authority;Username=unused",
            [FundAccountsStartup.SchemaVariable] = "retained_accounts"
        });
        await WriteConfigAsync();
        await using var configService = new ConfigurationService(Logger.None);
        if (runtime)
        {
            services.AddLogging();
            services.AddSingleton(new ConfigStore(ConfigPath));
            services.AddSingleton(TenantScopeEnforcementOptions.FailClosed);
            new StorageFeatureRegistration().Register(services, CompositionOptions.Minimal with { Configuration = configuration });
        }
        else
        {
            services.AddSingleton(new CompositionConfiguration(configuration));
            services.AddCommandDispatchServices(new AppConfig(DataRoot: _root), ConfigPath, Logger.None, configService);
        }
        using var provider = services.BuildServiceProvider();
        var accounts = provider.GetRequiredService<IFundAccountService>();
        accounts.Should().BeOfType<PostgresFundAccountService>();
        provider.GetRequiredService<IAccountManagementService>().Should().BeSameAs(accounts);
        provider.GetRequiredService<IAccountQueryService>().Should().BeSameAs(accounts);
        provider.GetRequiredService<FundAccountStoreOptions>().Schema.Should().Be("retained_accounts");
        (await provider.GetRequiredService<IAccountQueryService>().GetLatestBalanceSnapshotAsync(_accountId))!
            .CashBalance.Should().Be(725m);
        (await provider.GetRequiredService<IAccountQueryService>().GetBalanceTimelineAsync(_accountId))
            .Should().ContainSingle().Which.CashBalance.Should().Be(725m);
        if (!runtime)
        {
            var population = await provider.GetRequiredService<IInternalReconciliationPopulationProvider>()
                .GetPopulationsAsync(Context());
            population.CashBalances.Should().ContainSingle().Which.Balance.Should().Be(725m,
                "command reconciliation must use durable balances instead of the legacy snapshot's 125m");
        }
    }

    [Fact]
    public async Task CommandGraph_DefaultStrictPosture_RefusesLocalSnapshotAndPreservesItsBytes()
    {
        await SeedLocalAccountAsync();
        var before = await File.ReadAllBytesAsync(SnapshotPath);
        await WriteConfigAsync();
        await using var configService = new ConfigurationService(Logger.None);
        var services = new ServiceCollection();
        services.AddSingleton(new CompositionConfiguration(Configuration()));
        services.AddCommandDispatchServices(new AppConfig(DataRoot: _root), ConfigPath, Logger.None, configService);
        using var provider = services.BuildServiceProvider();
        var accounts = provider.GetRequiredService<IAccountQueryService>();
        accounts.Should().BeOfType<TenantGuardedLocalFundAccountService>();
        Func<Task> read = () => accounts.GetAccountAsync(_accountId);
        await read.Should().ThrowAsync<LocalTenantMigrationRequiredException>();
        var population = await provider.GetRequiredService<IInternalReconciliationPopulationProvider>()
            .GetPopulationsAsync(Context());
        population.Should().BeSameAs(InternalReconciliationPopulations.Empty);
        (await File.ReadAllBytesAsync(SnapshotPath)).Should().Equal(before);
    }

    [Fact]
    public async Task CommandGraph_ExplicitDeploymentBoundaryInConfig_CanReconcileRetainedLocalCash()
    {
        await SeedLocalAccountAsync();
        await WriteConfigAsync("deployment-boundary");
        await using var configService = new ConfigurationService(Logger.None);
        var services = new ServiceCollection();
        services.AddSingleton(new CompositionConfiguration(Configuration()));
        services.AddCommandDispatchServices(new AppConfig(DataRoot: _root), ConfigPath, Logger.None, configService);
        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<TenantScopeEnforcementOptions>().Should().Be(TenantScopeEnforcementOptions.DeploymentBoundary);
        var accounts = provider.GetRequiredService<IFundAccountService>();
        provider.GetRequiredService<IAccountQueryService>().Should().BeSameAs(accounts);
        provider.GetRequiredService<IAccountManagementService>().Should().BeSameAs(accounts);
        var population = await provider.GetRequiredService<IInternalReconciliationPopulationProvider>()
            .GetPopulationsAsync(Context());
        population.CashBalances.Should().ContainSingle().Which.Balance.Should().Be(125m);
    }

    [Fact]
    public async Task CommandGraph_UnifiedDatabaseConfiguration_UsesDurableStoreAndRejectsUnscopedRead()
    {
        await WriteConfigAsync();
        await using var configService = new ConfigurationService(Logger.None);
        var services = new ServiceCollection();
        services.AddSingleton(new CompositionConfiguration(Configuration(new Dictionary<string, string?>
        {
            [MeridianDatabaseEnvironment.UnifiedVariable] = "postgresql://unused:unused@localhost/account_authority"
        })));
        services.AddCommandDispatchServices(new AppConfig(DataRoot: _root), ConfigPath, Logger.None, configService);
        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IAccountQueryService>().Should().BeOfType<PostgresFundAccountService>();
        provider.GetRequiredService<IFundAccountStore>().Should().BeOfType<PostgresFundAccountStore>();
        provider.GetRequiredService<FundAccountStoreOptions>().ConnectionString
            .Should().Be(MeridianDatabaseEnvironment.NormalizeToConnectionString("postgresql://unused:unused@localhost/account_authority"));
        Func<Task> read = () => provider.GetRequiredService<IAccountQueryService>().GetAccountAsync(_accountId);
        await read.Should().ThrowAsync<TenantScopeRejectedException>();
    }

    private async Task<AccountSummaryDto> SeedLocalAccountAsync()
    {
        var accounts = new InMemoryFundAccountService(SnapshotPath);
        var account = await accounts.CreateAccountAsync(new(_accountId, AccountTypeDto.Bank, "LOCAL",
            "Retained local account", "USD", DateTimeOffset.UnixEpoch, "operator"));
        await accounts.RecordBalanceSnapshotAsync(new(_accountId, new DateOnly(2026, 5, 31), "USD", 125m, "ledger"));
        return account;
    }

    private Task WriteConfigAsync(string? tenantPosture = null)
        => File.WriteAllTextAsync(ConfigPath, JsonSerializer.Serialize(new AppConfig(DataRoot: _root,
            TenantScopeEnforcement: tenantPosture)));

    private static IConfiguration Configuration(Dictionary<string, string?>? settings = null)
    {
        settings ??= new();
        settings["MERIDIAN_USE_INMEMORY_GOVERNANCE"] = "true";
        settings["DOTNET_ENVIRONMENT"] = "Development";
        return new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
    }

    private InternalReconciliationPopulationContext Context()
        => new(_accountId.ToString("D"), "EXTERNAL", new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 31), "USD");

    public void Dispose() => Directory.Delete(_root, recursive: true);
}

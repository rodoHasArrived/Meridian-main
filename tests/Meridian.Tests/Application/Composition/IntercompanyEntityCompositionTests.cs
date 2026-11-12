using Meridian.Application.Composition;
using Meridian.Application.FundStructure;
using Meridian.Application.Tenancy;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Services;
using Meridian.Contracts.Tenancy;
using Meridian.Entities.FundStructure;
using Meridian.FinancialOperations.Intercompany;
using Meridian.FinancialOperations.Ledger;
using Meridian.Ledger;
using Meridian.PortfolioRecords.FundAccounts;
using Meridian.Storage;
using Meridian.Storage.FundStructure;
using Meridian.Storage.Ledger;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;

namespace Meridian.Tests.Application.Composition;

public sealed class IntercompanyEntityCompositionTests
{
    [Fact]
    public void DurableComposition_ResolvesSharedPostingAndPreflightAuthorityWithoutCycles()
    {
        const string connection = "Host=localhost;Database=meridian_synthetic;Username=synthetic";
        var configuration = new CompositionConfiguration(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["MERIDIAN_LEDGER_CONNECTION_STRING"] = connection,
            ["MERIDIAN_FUND_STRUCTURE_CONNECTION_STRING"] = connection
        }).Build());
        var services = Basics();
        services.AddSingleton(new LedgerJournalStoreOptions { ConnectionString = connection, SchemaName = "ledger_synthetic" });
        services.AddSingleton(new FundStructureStoreOptions { ConnectionString = connection, Schema = "structure_synthetic" });
        services.AddSingleton<IFundStructureStore, PostgresFundStructureStore>();
        services.AddSingleton(new Mock<IFundAccountService>().Object);
        services.AddSingleton<IFundStructurePolicyService, FundStructurePolicyService>();
        services.AddSingleton<PostgresFundStructureService>();
        services.Replace(ServiceDescriptor.Singleton<IFundStructureService>(sp => sp.GetRequiredService<PostgresFundStructureService>()));
        services.AddSingleton<PostgresLedgerJournalStore>();
        services.AddSingleton<ILedgerJournalStore>(sp => sp.GetRequiredService<PostgresLedgerJournalStore>());
        services.AddIntercompanyEntityServices(configuration);
        services.AddIntercompanyEntityServices(configuration);
        using var provider = services.BuildServiceProvider();

        var posting = provider.GetServices<ILedgerPostingAuthority>().ToArray();
        var preflight = provider.GetServices<ILedgerBookControlPreflight>().ToArray();
        Assert.Equal(2, posting.Length);
        Assert.Equal(2, preflight.Length);
        Assert.Same(provider.GetRequiredService<LifecycleLedgerPostingAuthority>(), posting.Single(x => x is LifecycleLedgerPostingAuthority));
        Assert.Same(posting.Single(x => x is LifecycleLedgerPostingAuthority), preflight.Single(x => x is LifecycleLedgerPostingAuthority));
        Assert.Same(posting.Single(x => x is IntercompanyLedgerPostingAuthority), preflight.Single(x => x is IntercompanyLedgerPostingAuthority));
        Assert.Same(provider.GetRequiredService<PostgresLedgerJournalStore>(), provider.GetRequiredService<ILedgerBookControlStore>());
        Assert.Same(provider.GetRequiredService<PostgresLedgerJournalStore>(), provider.GetRequiredService<IControlledCloseAuthorizationStore>());
        Assert.IsType<ConsolidationFxPostgresStore>(provider.GetRequiredService<IConsolidationFxStore>());
        Assert.IsType<PostgresIntercompanyAgreementStore>(provider.GetRequiredService<IIntercompanyAgreementStore>());
        Assert.NotNull(provider.GetRequiredService<Func<IConsolidationPostingAuthority?>>()());
        Assert.NotNull(provider.GetRequiredService<IntercompanyBalanceScheduleService>());
    }

    [Fact]
    public async Task LocalComposition_RetainsHistoryCapabilityWithoutFabricatingOutboxOrLedger()
    {
        var directory = Path.Combine(Path.GetTempPath(), "meridian-ic-composition-" + Guid.NewGuid().ToString("N"));
        var services = Basics();
        services.AddSingleton(new StorageOptions { RootPath = directory });
        services.AddIntercompanyEntityServices(new CompositionConfiguration(new ConfigurationBuilder().Build()));
        using var provider = services.BuildServiceProvider();
        try
        {
            Assert.IsType<FileEntityLifecycleStore>(provider.GetRequiredService<IEntityLifecycleStore>());
            Assert.IsType<FileIntercompanyAgreementStore>(provider.GetRequiredService<IIntercompanyAgreementStore>());
            Assert.IsType<ConsolidationFxFileStore>(provider.GetRequiredService<IConsolidationFxStore>());
            Assert.Empty(await provider.GetRequiredService<IntercompanyAgreementService>().ListAsync("synthetic-tenant"));
            Assert.Null(provider.GetService<IIntercompanyProposalStore>());
            Assert.Null(provider.GetService<ILedgerJournalStore>());
            Assert.Null(provider.GetService<IIntercompanyAccountBindingStore>());
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task MissingPartitionedGraph_IsAnExplicitNewControlDiagnosticInsteadOfBypassingTenantGuard()
    {
        const string connection = "Host=localhost;Database=meridian_synthetic;Username=synthetic";
        var services = Basics();
        var guarded = new TenantGuardedLocalFundStructureService(new Mock<IFundStructureService>(MockBehavior.Strict).Object,
            new LocalTenantMigrationGate(TenantScopeEnforcementOptions.FailClosed));
        services.Replace(ServiceDescriptor.Singleton<IFundStructureService>(guarded));
        services.AddSingleton(new LedgerJournalStoreOptions { ConnectionString = connection, SchemaName = "ledger_synthetic" });
        services.AddSingleton<PostgresLedgerJournalStore>();
        services.AddSingleton<ILedgerJournalStore>(sp => sp.GetRequiredService<PostgresLedgerJournalStore>());
        services.AddIntercompanyEntityServices(new CompositionConfiguration(new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["MERIDIAN_LEDGER_CONNECTION_STRING"] = connection }).Build()));
        using var provider = services.BuildServiceProvider();
        var authority = provider.GetServices<ILedgerPostingAuthority>().Single(x => x.ControlName == "entity-lifecycle");
        var id = Guid.NewGuid();
        var at = DateTimeOffset.UnixEpoch;
        var book = new LedgerBookRecord(Guid.NewGuid(), "fund", Guid.NewGuid(), Meridian.Contracts.FundStructure.FundStructureNodeKindDto.Entity,
            "Synthetic book", "USD", at, at);
        var write = new LedgerJournalEntryWrite(new JournalEntry(id, at, "Synthetic availability check",
            [new(Guid.NewGuid(), id, at, new("Cash", LedgerAccountType.Asset), 1m, 0m, "Synthetic availability check"),
             new(Guid.NewGuid(), id, at, new("Capital", LedgerAccountType.Equity), 0m, 1m, "Synthetic availability check")]), Guid.NewGuid(), Guid.NewGuid());
        var context = new LedgerPostingAuthorityContext(write, book,
            new(book.LedgerBookId, LedgerBookControlModeDto.LegacyWarning, 1, "entity-intercompany-v1", at), "tenant-a");

        await using var lease = await authority.AcquireAsync([context]);

        var decision = Assert.Single(context.Decisions);
        Assert.False(decision.Allowed);
        Assert.Equal("lifecycle-authority-unavailable", decision.Code);
        var preflight = await ((ILedgerBookControlPreflight)authority).CheckAsync(book, "tenant-a");
        await using var preflightLease = preflight.Lease;
        Assert.False(Assert.Single(preflight.Decisions).Allowed);
        Assert.True(guarded.RefusesUnattributedAccess);
    }

    private static ServiceCollection Basics()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new Mock<IFundStructureService>().Object);
        services.AddSingleton(new Mock<IAccountingConfigurationStore>().Object);
        services.AddSingleton(new Mock<IFundProfileTenancyRegistry>().Object);
        services.AddSingleton(new Mock<IAccountingPolicyService>().Object);
        services.AddSingleton(new Mock<IAccountingJournalDraftService>().Object);
        return services;
    }
}

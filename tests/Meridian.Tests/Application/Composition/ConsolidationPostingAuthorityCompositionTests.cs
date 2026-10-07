using FluentAssertions;
using Meridian.Application.Composition;
using Meridian.Application.Composition.Features;
using Meridian.Application.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Services;
using Meridian.Entities.FundStructure;
using Meridian.FinancialOperations.Consolidation;
using Meridian.FinancialOperations.Ledger;
using Meridian.PortfolioRecords.FundAccounts;
using Meridian.Storage;
using Meridian.Storage.FundAccounts;
using Meridian.Storage.FundStructure;
using Meridian.Storage.Ledger;
using Meridian.Ui.Shared.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Tests.Application.Composition;

public sealed class ConsolidationPostingAuthorityCompositionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ProductionAuthorities_ResolveSharedInstancesWithoutDependencyCycle(bool resolveAuthorityFirst)
    {
        using var provider = ComposeAuthorities();
        object first = resolveAuthorityFirst
            ? provider.GetRequiredService<IConsolidationPostingAuthority>()
            : provider.GetRequiredService<ILedgerJournalStore>();

        var ledger = provider.GetRequiredService<PostgresLedgerJournalStore>();
        var authority = provider.GetRequiredService<IConsolidationPostingAuthority>();
        authority.Should().BeOfType<PostgresConsolidationPostingAuthority>();
        first.Should().BeSameAs(resolveAuthorityFirst ? authority : ledger);
        provider.GetRequiredService<ILedgerJournalStore>().Should().BeSameAs(ledger);
        provider.GetRequiredService<ITransactionalLedgerJournalStore>().Should().BeSameAs(ledger);
        provider.GetRequiredService<Func<IConsolidationPostingAuthority?>>()()
            .Should().BeSameAs(authority);
        provider.GetRequiredService<IConsolidationDraftGuard>()
            .Should().BeSameAs(provider.GetRequiredService<ConsolidationService>());

        var structure = provider.GetRequiredService<PostgresFundStructureService>();
        provider.GetRequiredService<IFundStructureService>().Should().BeSameAs(structure);
        var structureStore = provider.GetRequiredService<IFundStructureStore>();
        structureStore.Should().BeOfType<PostgresFundStructureStore>();
        provider.GetRequiredService<IFundStructureStore>().Should().BeSameAs(structureStore);
        var policies = provider.GetRequiredService<IAccountingPolicyService>();
        policies.Should().BeOfType<AccountingPolicyService>();
        provider.GetRequiredService<IAccountingPolicyService>().Should().BeSameAs(policies);
        provider.GetRequiredService<IAccountingJournalDraftService>()
            .Should().BeOfType<AccountingJournalDraftService>();
    }

    [Fact]
    public async Task ProductionAuthority_RetainsTheRegisteredAccountingPolicyInstance()
    {
        using var provider = ComposeAuthorities();
        var policies = provider.GetRequiredService<IAccountingPolicyService>();
        await using var policyLease = await policies.AcquireConsolidationAuthorityLeaseAsync();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var evidence = new ConsolidationEvidenceDto(
            new ConsolidationRequestDto(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
                new DateOnly(2026, 10, 1)), "scope", "source", [], "perimeter", "w10-v1", [], []);

        // Holding the provider's policy lease must stop the composed authority before any
        // database access. A separate policy instance in the factory would miss this gate.
        var acquisition = provider.GetRequiredService<IConsolidationPostingAuthority>()
            .AcquireValidatedLeaseAsync(evidence, cancellation.Token);
        acquisition.IsCompleted.Should().BeFalse();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => acquisition);
    }

    [Theory]
    [InlineData("posting-authority")]
    [InlineData("ownership")]
    [InlineData("policy")]
    public void IncompleteAuthorities_PreserveLedgerResolutionWithoutInventingConsolidationAuthority(string missing)
    {
        using var provider = ComposeAuthorities(missing);

        provider.GetRequiredService<ILedgerJournalStore>().Should().BeOfType<PostgresLedgerJournalStore>();
        provider.GetRequiredService<Func<IConsolidationPostingAuthority?>>()().Should().BeNull();
        provider.GetService<IConsolidationPostingAuthority>().Should().BeNull();
        if (missing != "posting-authority")
        {
            provider.GetService<ConsolidationService>().Should().BeNull();
            provider.GetService<IConsolidationDraftGuard>().Should().BeNull();
        }
    }

    private static ServiceProvider ComposeAuthorities(string? missing = null)
    {
        const string connectionString = "Host=composition-db;Database=meridian;Username=postgres;Password=fixture";
        var settings = new Dictionary<string, string?> { ["DOTNET_ENVIRONMENT"] = "Production" };
        foreach (var variable in MeridianDatabaseEnvironment.PropagatedConnectionStringVariables)
            settings[variable] = connectionString;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var production = new ServiceCollection();
        new StorageFeatureRegistration().Register(production, CompositionOptions.WebDashboard with
        {
            Configuration = configuration
        });
        new LedgerFeatureRegistration().Register(production, CompositionOptions.WebDashboard);
        production.AddWorkstationSharedServices(configuration);

        // Retain the actual production descriptors for the complete consolidation dependency
        // graph. Unrelated workstation features need not be configured, and constructors do
        // not open database connections. An eager ledger-to-authority dependency would cycle
        // back through ConsolidationService and fail either resolution order here.
        var types = new HashSet<Type>
        {
            typeof(LedgerJournalStoreOptions), typeof(PostgresLedgerJournalStore),
            typeof(ILedgerJournalStore), typeof(ITransactionalLedgerJournalStore),
            typeof(Func<IConsolidationPostingAuthority?>),
            typeof(FundAccountStoreOptions), typeof(IFundAccountStore),
            typeof(PostgresFundAccountService), typeof(IFundAccountService),
            typeof(FundStructureStoreOptions), typeof(IFundStructureStore),
            typeof(IFundStructurePolicyService), typeof(PostgresFundStructureService),
            typeof(IFundStructureService), typeof(IAccountingPolicyService),
            typeof(IAccountingBasisProjectionService), typeof(IAccountingJournalDraftService),
            typeof(ConsolidationService), typeof(IConsolidationDraftGuard),
            typeof(IConsolidationPostingAuthority)
        };
        if (missing == "posting-authority")
            types.Remove(typeof(IConsolidationPostingAuthority));
        if (missing == "ownership")
            types.Remove(typeof(IFundStructureService));
        if (missing == "policy")
        {
            types.Remove(typeof(IAccountingPolicyService));
            types.Remove(typeof(IAccountingBasisProjectionService));
            types.Remove(typeof(IAccountingJournalDraftService));
        }
        IServiceCollection services = new ServiceCollection();
        foreach (var descriptor in production.Where(descriptor => types.Contains(descriptor.ServiceType)))
            services.Add(descriptor);
        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
    }
}

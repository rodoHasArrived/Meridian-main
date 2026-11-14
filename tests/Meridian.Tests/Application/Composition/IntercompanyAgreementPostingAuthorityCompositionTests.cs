using FluentAssertions;
using Meridian.Application.Composition;
using Meridian.Application.Composition.Features;
using Meridian.Application.FundStructure;
using Meridian.Storage;
using Meridian.Storage.FundStructure;
using Meridian.Storage.Ledger;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Tests.Application.Composition;

public sealed class IntercompanyAgreementPostingAuthorityCompositionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConfiguredAgreementAuthority_IsSharedByLedgerValidationRegardlessOfResolutionOrder(bool resolveAuthorityFirst)
    {
        using var provider = Compose();
        object first = resolveAuthorityFirst
            ? provider.GetRequiredService<IIntercompanyAgreementPostingAuthority>()
            : provider.GetRequiredService<ILedgerJournalStore>();

        var ledger = provider.GetRequiredService<PostgresLedgerJournalStore>();
        var authority = provider.GetRequiredService<IIntercompanyAgreementPostingAuthority>();
        first.Should().BeSameAs(resolveAuthorityFirst ? authority : ledger);
        authority.Should().BeOfType<PostgresIntercompanyAgreementPostingAuthority>();
        provider.GetRequiredService<Func<IIntercompanyAgreementPostingAuthority?>>()().Should().BeSameAs(authority);
        provider.GetRequiredService<ILedgerJournalStore>().Should().BeSameAs(ledger);
        provider.GetRequiredService<ITransactionalLedgerJournalStore>().Should().BeSameAs(ledger);
        provider.GetRequiredService<IIntercompanyAgreementJournalValidation>().Should().BeSameAs(ledger);
        provider.GetRequiredService<IIntercompanyAgreementStore>().Should().BeOfType<PostgresIntercompanyAgreementStore>();
    }

    [Fact]
    public void MissingAgreementAuthority_DoesNotPreventOrdinaryLedgerResolutionOrInventApprovalAuthority()
    {
        using var provider = Compose(includeAuthority: false);

        provider.GetRequiredService<ILedgerJournalStore>().Should().BeOfType<PostgresLedgerJournalStore>();
        provider.GetRequiredService<Func<IIntercompanyAgreementPostingAuthority?>>()().Should().BeNull();
        provider.GetService<IIntercompanyAgreementPostingAuthority>().Should().BeNull();
    }

    private static ServiceProvider Compose(bool includeAuthority = true)
    {
        const string connectionString = "Host=composition.invalid;Database=meridian;Username=synthetic";
        var settings = new Dictionary<string, string?> { ["DOTNET_ENVIRONMENT"] = "Production" };
        foreach (var variable in MeridianDatabaseEnvironment.PropagatedConnectionStringVariables)
            settings[variable] = connectionString;
        var production = new ServiceCollection();
        new StorageFeatureRegistration().Register(production, CompositionOptions.WebDashboard with
        {
            Configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build()
        });

        // Exercise the host's real ledger/structure descriptors without connecting to a database
        // or constructing unrelated host workers. Authority resolution stays deferred until use.
        var types = new HashSet<Type>
        {
            typeof(LedgerJournalStoreOptions), typeof(PostgresLedgerJournalStore),
            typeof(ILedgerJournalStore), typeof(ITransactionalLedgerJournalStore),
            typeof(IIntercompanyAgreementJournalValidation), typeof(Func<IIntercompanyAgreementPostingAuthority?>),
            typeof(FundStructureStoreOptions), typeof(IIntercompanyAgreementStore)
        };
        if (includeAuthority)
            types.Add(typeof(IIntercompanyAgreementPostingAuthority));
        IServiceCollection services = new ServiceCollection();
        foreach (var descriptor in production.Where(descriptor => types.Contains(descriptor.ServiceType)))
            services.Add(descriptor);
        return services.BuildServiceProvider();
    }
}

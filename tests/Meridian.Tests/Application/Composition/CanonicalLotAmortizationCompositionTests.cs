using FluentAssertions;
using Meridian.Application.Composition;
using Meridian.Application.Composition.Features;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Ledger;
using Meridian.FinancialOperations.Ledger;
using Meridian.Storage;
using Meridian.Storage.AssetOperations;
using Meridian.Storage.Ledger;
using Meridian.Storage.SecurityMaster;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using AuthEnvironmentScope = Meridian.Tests.Identity.EnvironmentVariableScope;

namespace Meridian.Tests.Application.Composition;

[Collection("Sequential")]
public sealed class CanonicalLotAmortizationCompositionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PrimaryStorage_WithCompleteAmortizationAuthority_ResolvesSharedStoresWithoutDependencyCycle(
        bool resolvePositionsFirst)
    {
        using var environment = ConfigurePrimaryStorageEnvironment(configuredAuthorities: 3);
        var services = new ServiceCollection();
        new StorageFeatureRegistration().Register(services, CompositionOptions.WebDashboard);
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        if (resolvePositionsFirst)
            provider.GetRequiredService<IInstrumentPositionProjectionStore>()
                .Should().BeOfType<PostgresAssetOperationsProjectionStore>();

        var ledger = provider.GetRequiredService<PostgresLedgerJournalStore>();
        provider.GetRequiredService<ILedgerJournalStore>().Should().BeSameAs(ledger);
        provider.GetRequiredService<ITransactionalLedgerJournalStore>().Should().BeSameAs(ledger);
        var resolveSecurities = provider.GetRequiredService<Func<ISecurityMasterStore?>>();
        var resolvePositions = provider.GetRequiredService<Func<IInstrumentPositionProjectionStore?>>();
        resolveSecurities().Should().BeOfType<PostgresSecurityMasterStore>()
            .Which.Should().BeSameAs(provider.GetRequiredService<ISecurityMasterStore>());
        resolvePositions().Should().BeOfType<PostgresAssetOperationsProjectionStore>()
            .Which.Should().BeSameAs(provider.GetRequiredService<IInstrumentPositionProjectionStore>());

        var options = provider.GetRequiredService<LedgerJournalStoreOptions>();
        options.RequireGovernedPostingCommand.Should().BeTrue();
        options.RequireExpectedVersion.Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void PrimaryStorage_WithIncompleteAmortizationAuthority_DoesNotInventPostingAuthorities(
        int configuredAuthorities)
    {
        using var environment = ConfigurePrimaryStorageEnvironment(configuredAuthorities);
        var services = new ServiceCollection();
        new StorageFeatureRegistration().Register(services, CompositionOptions.WebDashboard);
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        provider.GetRequiredService<ILedgerJournalStore>().Should().BeOfType<PostgresLedgerJournalStore>();
        var securities = provider.GetRequiredService<Func<ISecurityMasterStore?>>()();
        var positions = provider.GetRequiredService<Func<IInstrumentPositionProjectionStore?>>()();
        (securities is PostgresSecurityMasterStore).Should().Be((configuredAuthorities & 1) != 0);
        (positions is PostgresAssetOperationsProjectionStore).Should().Be((configuredAuthorities & 2) != 0);
        if ((configuredAuthorities & 1) == 0)
            securities.Should().BeNull();
        if ((configuredAuthorities & 2) == 0)
            positions.Should().BeOfType<InMemoryAssetOperationsProjectionStore>();
    }

    [Theory]
    [InlineData(0, "ledger journal")]
    [InlineData(1, "Security Master")]
    [InlineData(2, "ledger journal")]
    [InlineData(3, "book-position")]
    [InlineData(4, "ledger journal")]
    [InlineData(5, "Security Master")]
    [InlineData(6, "ledger journal")]
    public async Task Register_WithIncompleteAmortizationAuthority_PreservesStartupAndRefusesPreview(
        int configuredAuthorities, string missingAuthority)
    {
        var lots = Substitute.For<ILedgerJournalStore>();
        var securities = Substitute.For<ISecurityMasterStore>();
        var positions = Substitute.For<IInstrumentPositionProjectionStore>();
        var services = new ServiceCollection()
            .AddSingleton(Substitute.For<IAccountingConfigurationService>());
        if ((configuredAuthorities & 1) != 0)
            services.AddSingleton(lots);
        if ((configuredAuthorities & 2) != 0)
            services.AddSingleton(securities);
        if ((configuredAuthorities & 4) != 0)
            services.AddSingleton(positions);
        new LedgerFeatureRegistration().Register(services, CompositionOptions.Default);

        // Workstation profiles without these stores still validate and start. The feature
        // refuses actual use rather than registering a substitute persistence authority.
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
        var previewService = provider.GetRequiredService<CanonicalLotAmortizationService>();
        previewService.Should().BeSameAs(provider.GetRequiredService<CanonicalLotAmortizationService>());
        var preview = () => previewService.PreviewAsync(Guid.NewGuid(), Guid.NewGuid(),
            new DateOnly(2026, 1, 1), SecurityEvidence());

        await preview.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage($"*no authoritative {missingAuthority} store is configured*");
        lots.ReceivedCalls().Should().BeEmpty();
        securities.ReceivedCalls().Should().BeEmpty();
        positions.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task Register_WithCompleteAmortizationAuthority_ReadsTheRegisteredLotStore()
    {
        var lots = Substitute.For<ILedgerJournalStore>();
        var securities = Substitute.For<ISecurityMasterStore>();
        var positions = Substitute.For<IInstrumentPositionProjectionStore>();
        var bookId = Guid.NewGuid();
        var lotId = Guid.NewGuid();
        lots.GetTaxLotsByIdsAsync(bookId, Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<LedgerTaxLotRecord>>([]));
        var services = new ServiceCollection()
            .AddSingleton(Substitute.For<IAccountingConfigurationService>())
            .AddSingleton(lots)
            .AddSingleton(securities)
            .AddSingleton(positions);
        new LedgerFeatureRegistration().Register(services, CompositionOptions.Default);
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
        var preview = () => provider.GetRequiredService<CanonicalLotAmortizationService>()
            .PreviewAsync(bookId, lotId, new DateOnly(2026, 1, 1), SecurityEvidence());

        await preview.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("The canonical lot was not found in the requested book.");
        await lots.Received(1).GetTaxLotsByIdsAsync(bookId,
            Arg.Is<IReadOnlyList<Guid>>(ids => ids.Count == 1 && ids[0] == lotId), Arg.Any<CancellationToken>());
        securities.ReceivedCalls().Should().BeEmpty();
        positions.ReceivedCalls().Should().BeEmpty();
    }

    private static AuthEnvironmentScope ConfigurePrimaryStorageEnvironment(int configuredAuthorities)
    {
        const string connectionString = "Host=composition-db;Database=meridian;Username=postgres;Password=fixture";
        var environment = new AuthEnvironmentScope();
        environment.Set(MeridianDatabaseEnvironment.UnifiedVariable, null)
            .Set("DOTNET_ENVIRONMENT", "Production")
            .Set("ASPNETCORE_ENVIRONMENT", "Production")
            .Set("MERIDIAN_USE_INMEMORY_GOVERNANCE", null)
            .Set(DirectLendingStartup.ConnectionStringVariable, null)
            .Set(DirectLendingStartup.SchemaVariable, "security_master");
        foreach (var variable in MeridianDatabaseEnvironment.PropagatedConnectionStringVariables)
        {
            environment.Set(variable, connectionString);
            environment.Set(variable.Replace("_CONNECTION_STRING", "_SCHEMA", StringComparison.Ordinal), "composition_test");
        }
        environment.Set(SecurityMasterStartup.ConnectionStringVariable,
                (configuredAuthorities & 1) != 0 ? connectionString : null)
            .Set(AssetOperationsStartup.ConnectionStringVariable,
                (configuredAuthorities & 2) != 0 ? connectionString : null);
        return environment;
    }

    private static RetainedEvidenceIdentityDto SecurityEvidence()
    {
        var now = DateTimeOffset.UtcNow;
        return new("security-evidence", "document://security/evidence", new string('a', 64),
            "custodian", "security-reference", RetainedEvidenceIdentityValidator.AcceptedReviewStatus,
            "reviewer", now, new DateOnly(2025, 1, 1), 1, now, "retention-service",
            "SecurityMasterProjection", Guid.NewGuid().ToString("D"));
    }
}

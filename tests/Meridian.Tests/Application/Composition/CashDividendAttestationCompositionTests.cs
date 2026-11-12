using System.Text.Json;
using FluentAssertions;
using Meridian.Application.Composition;
using Meridian.Application.Composition.Features;
using Meridian.Application.SecurityMaster.CorporateActions;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage;
using Meridian.Storage.AssetOperations;
using Meridian.Storage.Ledger;
using Meridian.Storage.SecurityMaster;
using Meridian.Ui.Shared.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Meridian.Tests.Application.Composition;

[Collection("Sequential")]
public sealed class CashDividendAttestationCompositionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "meridian-cash-dividend-composition-" + Guid.NewGuid().ToString("N"));
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ProductionCashDividendComposition_IsExplicitlyOptInAndSharesNativeOrdinaryService(bool enabled)
    {
        using var provider = ComposePrimaryStorage(enabled);

        provider.GetRequiredService<SecurityMasterOptions>().EnableSinglePrincipalCashDividendAttestations.Should().Be(enabled);
        var accounting = provider.GetRequiredService<CorporateActionCaseAccountingService>();
        provider.GetRequiredService<ICorporateActionCaseAccountingService>().Should().BeSameAs(accounting);
        provider.GetRequiredService<ICorporateActionCashDividendAccountingService>().Should().BeSameAs(accounting);
        provider.GetServices<ICorporateActionCashDividendAccountingService>().Should().ContainSingle();
        provider.GetRequiredService<ICorporateActionCashDividendAccountingStore>().Should()
            .BeSameAs(provider.GetRequiredService<ICorporateActionOperationsStore>());
        var ledger = provider.GetRequiredService<PostgresLedgerJournalStore>();
        provider.GetRequiredService<ILedgerSecurityEntitlementInventory>().Should().BeSameAs(ledger);
        provider.GetRequiredService<ILedgerJournalStore>().Should().BeSameAs(ledger);
        provider.GetRequiredService<ITransactionalLedgerJournalStore>().Should().BeSameAs(ledger);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProductionCashDividendComposition_ResolvesAllEligibilityAuthoritiesWithoutCyclesOrDatabaseIo(bool adapterFirst)
    {
        using var provider = ComposePrimaryStorage(enabled: true);
        if (adapterFirst)
            provider.GetRequiredService<ICorporateActionCashDividendReconciliationSupport>()
                .Should().BeOfType<CorporateActionCashDividendReconciliationSupport>();
        var resolver = provider.GetRequiredService<ICorporateActionCashDividendEligibilityResolver>();
        resolver.Should().BeOfType<CorporateActionCashDividendEligibilityResolver>();
        provider.GetRequiredService<IInstrumentPositionProjectionStore>().Should().BeOfType<PostgresAssetOperationsProjectionStore>();
        provider.GetRequiredService<ISecurityCorporateActionsQueryService>().Should()
            .BeSameAs(provider.GetRequiredService<ISecurityMasterQueryService>());

        // A missing source snapshot is the first domain preflight after all eight composed
        // authorities. Reaching it proves the producer case-fact store was supplied too, while
        // deliberately stopping before any connection or source-query operation is attempted.
        var processingCase = new CorporateActionProcessingCaseDto(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            new("tenant-test", "company-test"), CorporateActionCaseStates.ReadyForApproval, 1, null, null, null,
            "preparer", DateTimeOffset.UnixEpoch, "preparer", DateTimeOffset.UnixEpoch);
        var projection = JsonSerializer.Deserialize<CorporateActionCaseAccountingProjectionDto>("{}")!;
        var spine = JsonSerializer.Deserialize<AssetAccountingEventSpineDto>("{}")!;
        var error = await Assert.ThrowsAsync<CorporateActionOperationException>(() => resolver.ResolveAsync(processingCase, projection, spine));
        error.Code.Should().Be(CorporateActionCashDividendAttestationValidation.ExcludedProblemCode);
        error.Message.Should().Be("The retained provider source snapshot is unavailable.");
    }

    [Fact]
    public async Task ProductionReconciliationDescriptor_OptionalMissingAuthoritiesResolveAndFailClosed()
    {
        IServiceCollection services = new ServiceCollection();
        services.Add(ProductionReconciliationDescriptor());
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        var support = provider.GetRequiredService<ICorporateActionCashDividendReconciliationSupport>();
        support.Should().BeOfType<CorporateActionCashDividendReconciliationSupport>();

        var error = await Assert.ThrowsAsync<CorporateActionValidationException>(() => support.GetSupportAsync(
            new("tenant-test", "company-test", FundProfileId: "fund-test", LedgerBookId: Guid.NewGuid().ToString("D")),
            Guid.NewGuid(), new DateOnly(2026, 8, 13), [new(Guid.NewGuid().ToString("D"), 100m)]));

        error.Code.Should().Be(CorporateActionProblemCodes.ValidationFailed);
        error.Message.Should().Contain("requires scoped statements");
    }

    private ServiceProvider ComposePrimaryStorage(bool enabled)
    {
        const string unreachable = "Host=127.0.0.1;Port=1;Database=meridian;Username=fixture;Password=fixture;Timeout=1";
        var settings = new Dictionary<string, string?>
        {
            ["DOTNET_ENVIRONMENT"] = "Production"
        };
        if (enabled)
            settings["SinglePrincipalAttestation:EnableCashDividends"] = "true";
        foreach (var variable in MeridianDatabaseEnvironment.PropagatedConnectionStringVariables)
            settings[variable] = unreachable;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        IServiceCollection services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(new Meridian.Application.UI.ConfigStore(Path.Combine(_root, "appsettings.json")));
        services.AddSingleton(Substitute.For<IAccountingConfigurationService>());
        new StorageFeatureRegistration().Register(services, CompositionOptions.WebDashboard with { Configuration = configuration });
        // Use the actual workstation registration for this one adapter; unrelated workstation
        // features are outside the graph under test and do not need to construct file stores.
        services.Add(ProductionReconciliationDescriptor());
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    private static ServiceDescriptor ProductionReconciliationDescriptor()
    {
        var workstation = new ServiceCollection();
        workstation.AddWorkstationSharedServices(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DOTNET_ENVIRONMENT"] = "Test",
            ["MERIDIAN_USE_INMEMORY_GOVERNANCE"] = "true"
        }).Build());
        return workstation.Single(descriptor => descriptor.ServiceType == typeof(ICorporateActionCashDividendReconciliationSupport));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}

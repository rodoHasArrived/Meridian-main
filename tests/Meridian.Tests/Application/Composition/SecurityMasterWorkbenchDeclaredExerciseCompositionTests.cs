using FluentAssertions;
using Meridian.Application.Composition;
using Meridian.Application.Composition.Features;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.Calendars;
using Meridian.Contracts.FixedIncome;
using Meridian.Reporting;
using Meridian.Storage;
using Meridian.Storage.Calendars;
using Meridian.Strategies.Interfaces;
using Meridian.Strategies.Services;
using Meridian.Testing;
using Meridian.Ui.Shared.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;

namespace Meridian.Tests.Application.Composition;

/// <summary>
/// Pins what the composed Security Master workbench query service receives for declared call and put
/// rows (ADR-025, increment 8b): the workstation's own registration, resolved beside the storage
/// feature, is handed the registered business-day calendar source, the retained cutover policy and the
/// registered clock. Without them a declaring bond would be read with no calendars and no cutover gate.
/// </summary>
[Collection("Sequential")]
public sealed class SecurityMasterWorkbenchDeclaredExerciseCompositionTests : IDisposable
{
    private readonly TestArtifactDirectory _artifacts =
        TestArtifactDirectory.Create(nameof(SecurityMasterWorkbenchDeclaredExerciseCompositionTests));

    public void Dispose() => _artifacts.Dispose();

    [Fact]
    public void WorkstationRegistration_HandsTheWorkbenchTheComposedCalendarSourceAndCutoverPolicy()
    {
        using var provider = BuildProvider(withStorageFeature: true);

        var workbench = provider.GetRequiredService<ISecurityMasterWorkbenchQueryService>()
            .Should().BeOfType<SecurityMasterWorkbenchQueryService>().Subject;

        workbench.CalendarSource.Should().BeOfType<FileBusinessDayCalendarSource>()
            .And.BeSameAs(provider.GetRequiredService<IBusinessDayCalendarSource>());
        workbench.BondCalculationPolicy.Should().BeSameAs(provider.GetRequiredService<IBondCalculationPolicy>());
    }

    [Fact]
    public void WorkstationRegistration_WithoutTheStorageFeature_StillResolves_WithNoCalendarSourceOrPolicy()
    {
        using var provider = BuildProvider(withStorageFeature: false);

        var workbench = provider.GetRequiredService<ISecurityMasterWorkbenchQueryService>()
            .Should().BeOfType<SecurityMasterWorkbenchQueryService>().Subject;

        workbench.CalendarSource.Should().BeNull("a host without the source gets the watch's refusal, not a fault");
        workbench.BondCalculationPolicy.Should().BeNull();
    }

    private ServiceProvider BuildProvider(bool withStorageFeature)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        if (withStorageFeature)
        {
            var settings = new Dictionary<string, string?>
            {
                ["DOTNET_ENVIRONMENT"] = "Test",
                ["MERIDIAN_USE_INMEMORY_GOVERNANCE"] = "true",
                [SecurityMasterStartup.ConnectionStringVariable] = null
            };
            new StorageFeatureRegistration().Register(services, CompositionOptions.Minimal with
            {
                Configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build()
            });
            services.Replace(ServiceDescriptor.Singleton(new StorageOptions { RootPath = _artifacts.RootPath }));
        }
        else
        {
            services.AddSingleton(Substitute.For<Meridian.Contracts.SecurityMaster.ISecurityMasterQueryService>());
        }

        // The production descriptor, isolated from unrelated workstation features.
        var workstation = new ServiceCollection();
        workstation.AddWorkstationSharedServices();
        services.Add(workstation.Single(static descriptor => descriptor.ServiceType == typeof(ISecurityMasterWorkbenchQueryService)));

        // The workbench's other required collaborators, which this test does not exercise.
        services.TryAddSingleton(Substitute.For<ISecurityValidationService>());
        services.TryAddSingleton(Substitute.For<ISecurityMasterConflictService>());
        services.TryAddSingleton(Substitute.For<ISecurityMasterIngestStatusService>());
        services.TryAddSingleton(Substitute.For<IStrategyRepository>());
        services.TryAddSingleton(new PortfolioReadService());
        services.TryAddSingleton(new LedgerReadService());
        services.TryAddSingleton(sp => new ReportGenerationService(sp.GetRequiredService<Meridian.Contracts.SecurityMaster.ISecurityMasterQueryService>()));
        return services.BuildServiceProvider();
    }
}

using FluentAssertions;
using Meridian.FinancialOperations.Reconciliation;
using Meridian.FinancialOperations.Reconciliation.Connectors;
using Meridian.Reporting;
using Meridian.Storage.Reporting;
using Meridian.Ui.Shared.Evidence;
using Meridian.Ui.Shared.Services;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using CoreConfigStore = Meridian.Application.UI.ConfigStore;

namespace Meridian.Tests.Ui;

[Collection("Sequential")]
public sealed class StatementReconciliationAuthorityCompositionTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "Meridian.Tests",
        nameof(StatementReconciliationAuthorityCompositionTests),
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void ConfiguredStagingPostgresAuthority_UsesReportingEvidenceRetainer()
    {
        using var quietProductionEnvironment =
            new Meridian.Tests.Application.Composition.ProductionEnvironmentQuietScope();
        using var environment = new Meridian.Tests.Application.Composition.EnvironmentVariableScope(
            "ASPNETCORE_ENVIRONMENT",
            "Staging");
        using var unified = new Meridian.Tests.Application.Composition.EnvironmentVariableScope(
            Meridian.Storage.MeridianDatabaseEnvironment.UnifiedVariable,
            null);
        using var reporting = new Meridian.Tests.Application.Composition.EnvironmentVariableScope(
            "MERIDIAN_REPORTING_CONNECTION_STRING",
            "Host=127.0.0.1;Port=1;Database=meridian;Username=test;Password=test;Timeout=1");
        using var ledger = new Meridian.Tests.Application.Composition.EnvironmentVariableScope(
            "MERIDIAN_LEDGER_CONNECTION_STRING",
            null);
        var services = CreateMinimalWorkstationServices();

        services.AddWorkstationSharedServices();

        using var provider = services.BuildServiceProvider();
        var authority = provider.GetRequiredService<IStatementReconciliationReportAuthorityStore>();
        authority.Should().BeOfType<PostgresStatementReconciliationReportAuthorityStore>();
        authority.IsDurableAuthority.Should().BeTrue();
        provider.GetRequiredService<IStatementImportEvidenceRetainer>()
            .Should().BeOfType<ReportingStatementImportEvidenceRetainer>();
        provider.GetRequiredService<StatementReconciliationReportWorkflowService>()
            .IsDurablyComposedWith(authority).Should().BeTrue();
    }

    [Fact]
    public void UnconfiguredNonProductionAuthority_UsesFileEvidenceBridge()
    {
        using var quietProductionEnvironment =
            new Meridian.Tests.Application.Composition.ProductionEnvironmentQuietScope();
        using var unified = new Meridian.Tests.Application.Composition.EnvironmentVariableScope(
            Meridian.Storage.MeridianDatabaseEnvironment.UnifiedVariable,
            null);
        using var reporting = new Meridian.Tests.Application.Composition.EnvironmentVariableScope(
            "MERIDIAN_REPORTING_CONNECTION_STRING",
            null);
        using var ledger = new Meridian.Tests.Application.Composition.EnvironmentVariableScope(
            "MERIDIAN_LEDGER_CONNECTION_STRING",
            null);
        var services = CreateMinimalWorkstationServices();

        services.AddWorkstationSharedServices();

        using var provider = services.BuildServiceProvider();
        var authority = provider.GetRequiredService<IStatementReconciliationReportAuthorityStore>();
        authority.Should().BeOfType<FileStatementReconciliationReportAuthorityStore>();
        authority.IsDurableAuthority.Should().BeFalse();
        provider.GetRequiredService<IStatementImportEvidenceRetainer>()
            .Should().BeSameAs(provider.GetRequiredService<StatementImportEvidenceBridge>());
        provider.GetRequiredService<StatementReconciliationReportWorkflowService>()
            .IsDurablyComposed.Should().BeFalse();
    }

    private ServiceCollection CreateMinimalWorkstationServices()
    {
        var configPath = Path.Combine(_root, "appsettings.json");
        Directory.CreateDirectory(_root);
        File.WriteAllText(configPath, "{}");

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(new CoreConfigStore(configPath));
        services.AddSingleton(Substitute.For<IStatementImportCommitService>());
        services.AddSingleton(Substitute.For<IStatementRunWorkflowService>());
        services.AddSingleton(Substitute.For<IStatementReconciliationIntakeAuthority>());
        return services;
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}

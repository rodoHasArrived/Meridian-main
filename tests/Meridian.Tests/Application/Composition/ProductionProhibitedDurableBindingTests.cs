using FluentAssertions;
using Meridian.Application.Composition;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Meridian.Tests.Application.Composition;

/// <summary>
/// W9-TRUTH-001 startup rejection tests per prohibited binding: every real in-memory,
/// null, or no-op implementation of a durable role in the codebase must be classified
/// non-production by the policy, must fail a production composition outright, and (for
/// durable-store contracts) must fail the supported local posture whenever the host
/// asserts durability. These pin the real types, not synthetic doubles, so a renamed or
/// newly opted-out implementation surfaces here.
/// </summary>
public sealed class ProductionProhibitedDurableBindingTests
{
    /// <summary>Real prohibited implementations of durable-role service contracts.</summary>
    public static TheoryData<Type, Type> ProhibitedDurableRoleBindings() => new()
    {
        {
            typeof(Meridian.Storage.AssetOperations.IAssetOperationsProjectionStore),
            typeof(Meridian.Storage.AssetOperations.InMemoryAssetOperationsProjectionStore)
        },
        {
            typeof(Meridian.FinancialOperations.OperationsContinuity.IOperationsContinuityRepository),
            typeof(Meridian.FinancialOperations.OperationsContinuity.InMemoryOperationsContinuityRepository)
        },
        {
            typeof(Meridian.FinancialOperations.OperationsContinuity.IOperationsWorkflowAuditStore),
            typeof(Meridian.FinancialOperations.OperationsContinuity.InMemoryOperationsWorkflowAuditStore)
        },
        {
            typeof(Meridian.Strategies.Services.IReconciliationRunRepository),
            typeof(Meridian.Strategies.Services.InMemoryReconciliationRunRepository)
        },
        {
            typeof(Meridian.FinancialOperations.Reconciliation.IStatementRunRecoveryRepository),
            typeof(Meridian.FinancialOperations.Reconciliation.InMemoryStatementRunRecoveryRepository)
        },
        {
            typeof(Meridian.Infrastructure.Reconciliation.IStatementRunMatchArtifactStore),
            typeof(Meridian.Infrastructure.Reconciliation.InMemoryStatementRunMatchArtifactStore)
        },
        {
            typeof(Meridian.FinancialOperations.Reconciliation.IStatementReconciliationCheckpointStore),
            typeof(Meridian.FinancialOperations.Reconciliation.InMemoryStatementReconciliationCheckpointStore)
        },
        {
            typeof(Meridian.Reporting.IReportingReconciliationEvidenceRetentionStore),
            typeof(Meridian.Ui.Shared.Services.InMemoryReportingReconciliationEvidenceStore)
        },
        {
            typeof(Meridian.Contracts.Ledger.IAccountingConfigurationStore),
            typeof(Meridian.Ui.Shared.Services.InMemoryAccountingConfigurationStore)
        },
        {
            typeof(Meridian.Contracts.Ledger.IManualJournalEntryDraftStore),
            typeof(Meridian.Ui.Shared.Services.InMemoryManualJournalEntryDraftStore)
        },
        {
            typeof(Meridian.Ui.Shared.Services.IAutomatedJournalScheduleStore),
            typeof(Meridian.Ui.Shared.Services.InMemoryAutomatedJournalScheduleStore)
        },
        {
            typeof(Meridian.Ledger.IAutomatedJournalPostingTarget),
            typeof(Meridian.Ledger.InMemoryAutomatedJournalPostingTarget)
        },
        {
            typeof(Meridian.Storage.FundStructure.IFundStructureStateStore),
            typeof(Meridian.Storage.FundStructure.InMemoryFundStructureStateStore)
        },
        {
            typeof(Meridian.Ui.Shared.Extensibility.IExtensibilityConfigurationStore),
            typeof(Meridian.Ui.Shared.Extensibility.InMemoryExtensibilityConfigurationStore)
        },
        {
            typeof(Meridian.Application.SecurityMaster.ISecurityMasterRevisionStore),
            typeof(Meridian.Application.SecurityMaster.InMemorySecurityMasterRevisionStore)
        }
    };

    /// <summary>
    /// Prohibited implementations whose service contract is not name-matched as a durable
    /// store but which still must never reach a production composition.
    /// </summary>
    public static TheoryData<Type, Type> ProhibitedNonDurableRoleBindings() => new()
    {
        {
            typeof(Meridian.Platform.FundOperationsPersistence.IDomainProjectionReconciliationJob),
            typeof(Meridian.Platform.FundOperationsPersistence.NoOpDomainProjectionReconciliationJob)
        }
    };

    public static TheoryData<Type, Type> AllProhibitedBindings()
    {
        var data = new TheoryData<Type, Type>();
        foreach (var row in ProhibitedDurableRoleBindings())
        {
            data.Add((Type)row[0]!, (Type)row[1]!);
        }

        foreach (var row in ProhibitedNonDurableRoleBindings())
        {
            data.Add((Type)row[0]!, (Type)row[1]!);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(AllProhibitedBindings))]
    public void Policy_ClassifiesRealImplementationAsNonProduction(Type serviceType, Type implementationType)
    {
        _ = serviceType;

        ProductionServiceRegistrationPolicy.IsNonProductionOnlyImplementation(implementationType)
            .Should().BeTrue(
                $"{implementationType.Name} is an in-memory/no-op implementation of a durable or governed role " +
                "and must carry a non-production classification (marker interface, attribute, or prohibited name prefix)");
    }

    [Theory]
    [MemberData(nameof(AllProhibitedBindings))]
    public void ProductionComposition_RejectsStartupForEachProhibitedBinding(Type serviceType, Type implementationType)
    {
        var services = new ServiceCollection();
        services.DeclareMeridianDeploymentPosture(MeridianDeploymentPosture.ProductionApi);
        services.AddSingleton(serviceType, implementationType);

        var validate = () => ProductionServiceRegistrationPolicy.Validate(services);

        validate.Should().Throw<InvalidOperationException>(
                $"a production composition must refuse startup when {serviceType.Name} is bound to {implementationType.Name}")
            .WithMessage($"*{implementationType.FullName}*");
    }

    [Theory]
    [MemberData(nameof(ProhibitedDurableRoleBindings))]
    public void SupportedLocalPosture_WhenDurabilityAsserted_RejectsInMemoryDurableBinding(
        Type serviceType, Type implementationType)
    {
        var services = new ServiceCollection();
        services.DeclareMeridianDeploymentPosture(MeridianDeploymentPosture.LocalWorkstation);
        services.AddSingleton(serviceType, implementationType);

        var validate = () => ProductionServiceRegistrationPolicy.ValidateSupportedLocal(
            services, requireDurableStores: true);

        validate.Should().Throw<InvalidOperationException>(
                "the supported local-workstation posture asserts durable money-path stores, so an in-memory " +
                $"binding for {serviceType.Name} must abort composition rather than silently fabricate data")
            .WithMessage($"*{implementationType.FullName ?? implementationType.Name}*");
    }

    [Theory]
    [MemberData(nameof(ProhibitedDurableRoleBindings))]
    public void SupportedLocalPosture_WithoutDurabilityAssertion_ForcesSimulatedProvenance(
        Type serviceType, Type implementationType)
    {
        var services = new ServiceCollection();
        services.DeclareMeridianDeploymentPosture(MeridianDeploymentPosture.LocalWorkstation);
        services.AddSingleton(serviceType, implementationType);

        var provenance = ProductionServiceRegistrationPolicy.ResolveComposedDataProvenance(services);

        provenance.Should().Be(Meridian.Contracts.Operations.DataProvenance.Simulated,
            "a local composition holding fabricated durable-store data must surface the persistent simulated " +
            "label instead of presenting as real");
    }
}

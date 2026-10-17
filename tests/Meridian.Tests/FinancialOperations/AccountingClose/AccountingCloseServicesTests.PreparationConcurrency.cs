using System.Reflection;
using FluentAssertions;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.AccountingClose;
using Xunit;

namespace Meridian.Tests.FinancialOperations.AccountingClose;

public sealed partial class AccountingCloseServicesTests
{
    [Fact]
    public async Task Scenario_PrepareNextPeriod_QueuedOrdinarySetupPreservesNewlyRetainedPreparation()
    {
        using var fixture = new PreparationFixture();
        var workflowId = Guid.NewGuid();
        await fixture.Workflows.StartPreparedWorkflowAsync(new OperationsStartWorkflowRequestDto(
            fixture.SourceWorkflow.FundAccountId, fixture.TargetPeriod.PeriodId.ToString("D"), null,
            "custodian", PreparationFixture.Actor, LedgerBookId: fixture.TargetBook.LedgerBookId), workflowId);
        var lineage = new ClosePlanPreparationLineageDto(Guid.NewGuid(), 2, fixture.SourceWorkflow.WorkflowId,
            fixture.TargetPeriod.PeriodId, fixture.TargetPeriod.StartDate, fixture.TargetPeriod.EndDate,
            DateTimeOffset.UtcNow, PreparationFixture.Actor,
            [new ClosePreparationHistoryDto("PlanCreated", DateTimeOffset.UtcNow, PreparationFixture.Actor, "Retained creation")]);
        var creationEvidence = $"close-plan-preparation:{workflowId:D}:book:{fixture.TargetBook.LedgerBookId:D}:preview:{Guid.NewGuid():D}";
        var preparedRequest = new UpsertClosePeriodPlanConfigurationRequestDto(workflowId,
            TaskConfigurations: [new CloseTaskConfigurationDto("reconciliation-review", Owner: "Prepared owner")],
            EvidenceLinks: [creationEvidence])
        { Preparation = lineage };
        var ordinaryRequest = new UpsertClosePeriodPlanConfigurationRequestDto(workflowId,
            TaskConfigurations: [new CloseTaskConfigurationDto("reconciliation-review", Owner: "Updated owner")],
            EvidenceLinks: [$"evidence:close-plan:{workflowId:D}:book:{fixture.TargetBook.LedgerBookId:D}:configuration-approval"]);

        // Queue both writes before either may retain state. Previously the second request captured
        // an empty configuration before waiting, then erased the first request's creation lineage.
        var writeGate = (SemaphoreSlim)typeof(AccountingCloseManagementService)
            .GetField("_writeGate", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(fixture.DestinationPlans)!;
        await writeGate.WaitAsync();
        Task<ClosePeriodPlanDto?> prepared;
        Task<ClosePeriodPlanDto?> ordinary;
        try
        {
            prepared = fixture.DestinationPlans.ConfigurePeriodPlanAsync(preparedRequest, PreparationFixture.Actor);
            ordinary = fixture.DestinationPlans.ConfigurePeriodPlanAsync(ordinaryRequest, PreparationFixture.Actor);
            prepared.IsCompleted.Should().BeFalse();
            ordinary.IsCompleted.Should().BeFalse();
        }
        finally
        {
            writeGate.Release();
        }
        await Task.WhenAll(prepared, ordinary);

        var retained = await fixture.DestinationPlans.GetPeriodPlanAsync(workflowId);
        retained!.Configuration!.Preparation.Should().BeEquivalentTo(lineage);
        retained.Configuration.EvidenceLinks.Should().Contain(creationEvidence);
        retained.Tasks.Single(task => task.TaskId == "reconciliation-review").Owner.Should().Be("Updated owner");
        retained.PeriodStart.Should().Be(fixture.TargetPeriod.StartDate);
        retained.PeriodEnd.Should().Be(fixture.TargetPeriod.EndDate);
    }
}

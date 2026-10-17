using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.OperationsContinuity;
using Meridian.Storage.SecurityMaster;
using Moq;
using Npgsql;

namespace Meridian.Tests.SecurityMaster;

public sealed partial class SecurityMasterGovernedConcurrencyPostgresTests
{
    [SecurityMasterDatabaseFact]
    public async Task MixedCaseSchema_MigrationAndGovernedEditUseTheSameTables()
    {
        var id = await SeedAsync();
        var options = new SecurityMasterOptions
        {
            ConnectionString = fixture.Options.ConnectionString,
            Schema = fixture.Options.Schema.ToUpperInvariant(),
            PreloadProjectionCache = false
        };
        await new SecurityMasterMigrationRunner(options).EnsureMigratedAsync();
        await Service(id, options: options).UpdateSecurityFieldAsync(Edit(id, "rating", "AA"));

        (await new PostgresOperatorOverridesStore(fixture.Options).GetAsync(id))!.Values["rating"].Should().Be("AA");
        (await new PostgresSecurityMasterRevisionStore(fixture.Options).ListBySecurityAsync(id)).Should().ContainSingle();
        (await GenerationAsync(id)).Should().Be(1);
    }

    [SecurityMasterDatabaseFact]
    public async Task Discard_ProvenanceSqlFailure_CommitsWithdrawalAndWorkflowRejection()
    {
        var id = await SeedAsync();
        var provenance = new PostgresSecurityFieldProvenanceStore(fixture.Options);
        await Service(id, fieldProvenance: provenance).UpdateSecurityFieldAsync(Edit(id, "rating", "AA"));
        var revisions = new PostgresSecurityMasterRevisionStore(fixture.Options);
        var revision = (await revisions.ListBySecurityAsync(id)).Single();
        var workflowId = Guid.NewGuid();
        await revisions.TransitionAsync(revision.RevisionId, SecurityMasterRevisionStateDto.Draft,
            SecurityMasterRevisionStateDto.Submitted, "maker", workflowIdForSubmit: workflowId);
        var workflow = new Mock<IOperationsContinuityWorkflowService>(MockBehavior.Strict);
        var workflowState = new OperationsContinuityWorkflowDto(workflowId, Guid.NewGuid(), "2026-06", null,
            "custodian", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 1,
            OperationsWorkflowStatusDto.ApprovalPending, OperationsBrokerIntakeStateDto.Complete,
            OperationsSecurityMasterStateDto.Complete, OperationsLedgerPostingStateDto.Complete,
            OperationsReconciliationStateDto.Complete, OperationsApprovalStateDto.Submitted,
            [], [], [], null, [], new OperationsReportPackReadinessDto(true, "rp-1", null, []), [], [], [], []);
        workflow.Setup(w => w.GetAsync(workflowId, It.IsAny<CancellationToken>())).ReturnsAsync(() => workflowState);
        workflow.Setup(w => w.RejectWorkflowAsync(workflowId, It.IsAny<OperationsRejectWorkflowRequestDto>(),
                It.IsAny<CancellationToken>()))
            .Callback(() => workflowState = workflowState with { ApprovalState = OperationsApprovalStateDto.Rejected })
            .ReturnsAsync(new OperationsTransitionResultDto(true, null, null, null, [], [], 2));
        (await provenance.GetAsync(id)).Should().ContainSingle();
        var generation = await GenerationAsync(id);
        var trigger = "fail_discard_provenance_" + id.ToString("N");
        var schema = fixture.Options.Schema;
        await ExecuteSqlAsync($$"""
            create function {{schema}}.{{trigger}}() returns trigger language plpgsql as $body$
            begin
                raise exception 'Fixture rejects optional provenance deletion' using errcode = '23514';
            end;
            $body$;
            create trigger {{trigger}} before delete on {{schema}}.security_field_provenance
                for each row when (old.security_id = '{{id:D}}'::uuid)
                execute function {{schema}}.{{trigger}}();
            """);
        try
        {
            var result = await Service(id, fieldProvenance: provenance, approvalWorkflow: workflow.Object)
                .DiscardRevisionAsync(new(id, revision.RevisionId, "checker", "Abandoned edit."));

            result.State.Should().Be(SecurityMasterRevisionStateDto.Rejected);
            workflowState.ApprovalState.Should().Be(OperationsApprovalStateDto.Rejected);
            (await revisions.GetAsync(revision.RevisionId))!.State.Should().Be(SecurityMasterRevisionStateDto.Rejected);
            (await new PostgresOperatorOverridesStore(fixture.Options).GetAsync(id))!.Values.Should().BeEmpty();
            (await GenerationAsync(id)).Should().Be(generation + 1);
            (await provenance.GetAsync(id)).Should().ContainSingle("the optional SQL statement failed");
        }
        finally
        {
            await ExecuteSqlAsync($"drop trigger {trigger} on {schema}.security_field_provenance; drop function {schema}.{trigger}();");
        }
        await Service(id, fieldProvenance: provenance).DiscardRevisionAsync(new(id, revision.RevisionId, "checker", "Reconcile lineage."));
        (await provenance.GetAsync(id)).Should().BeEmpty();
    }

    [SecurityMasterDatabaseFact]
    public async Task Discard_DecisionSqlFailure_PreservesRestoredPublishedValueAndAllowsReconciliation()
    {
        var id = await SeedAsync();
        var revisions = new PostgresSecurityMasterRevisionStore(fixture.Options);
        var published = await revisions.CreateDraftAsync(id, "maker", "rating", DateTimeOffset.UtcNow,
            "Published value", fieldValue: new SecurityMasterRevisionFieldValue("AA"));
        await revisions.TransitionAsync(published.RevisionId, SecurityMasterRevisionStateDto.Draft, SecurityMasterRevisionStateDto.Submitted, "maker");
        await revisions.TransitionAsync(published.RevisionId, SecurityMasterRevisionStateDto.Submitted, SecurityMasterRevisionStateDto.Approved, "checker");
        await revisions.TransitionAsync(published.RevisionId, SecurityMasterRevisionStateDto.Approved, SecurityMasterRevisionStateDto.Published, "checker");
        await Service(id).UpdateSecurityFieldAsync(Edit(id, "rating", "BBB"));
        var discarded = (await revisions.ListBySecurityAsync(id)).Single(r => r.State == SecurityMasterRevisionStateDto.Draft);
        var generation = await GenerationAsync(id);
        var table = fixture.Options.Schema + ".security_operator_overrides";
        var constraint = "reject_optional_decision_" + id.ToString("N");
        await ExecuteSqlAsync($"alter table {table} add constraint {constraint} check (security_id <> '{id:D}'::uuid or approval_status <> 'Approved');");
        try
        {
            await Service(id).DiscardRevisionAsync(new(id, discarded.RevisionId, "checker", "Restore published value."));
            (await revisions.GetAsync(discarded.RevisionId))!.State.Should().Be(SecurityMasterRevisionStateDto.Rejected);
            var overlay = (await new PostgresOperatorOverridesStore(fixture.Options).GetAsync(id))!;
            overlay.Values["rating"].Should().Be("AA");
            overlay.ApprovalStatus.Should().Be(SecurityOverrideApprovalStatusDto.Pending);
            overlay.AuditTrail.Should().NotContain(entry => entry.EventType == "Approved");
            (await GenerationAsync(id)).Should().Be(generation + 1);
        }
        finally
        {
            await ExecuteSqlAsync($"alter table {table} drop constraint {constraint};");
        }
        await Service(id).DiscardRevisionAsync(new(id, discarded.RevisionId, "checker", "Reconcile decision."));
        (await new PostgresOperatorOverridesStore(fixture.Options).GetAsync(id))!.ApprovalStatus.Should().Be(SecurityOverrideApprovalStatusDto.Approved);
    }

    [SecurityMasterDatabaseFact]
    public async Task OptionalSqlFailure_RollsBackOnlyOptionalWrites()
    {
        var id = await SeedAsync();
        var overlays = new PostgresOperatorOverridesStore(fixture.Options);
        Exception? reported = null;
        await PostgresSecurityMasterMutation.ExecuteAsync(fixture.Options, id, async () =>
        {
            await overlays.PatchAsync(id, Patch("rating", "AA"), "maker");
            await PostgresSecurityMasterMutation.ExecuteBestEffortAsync(fixture.Options, id, async () =>
            {
                await overlays.PatchAsync(id, Patch("rating", "BBB"), "optional");
                await using var lease = await PostgresSecurityMasterMutation.OpenConnectionAsync(fixture.Options, CancellationToken.None);
                await using var command = new NpgsqlCommand("select 1 / 0;", lease.Connection, lease.Transaction);
                await command.ExecuteScalarAsync();
            }, exception => reported = exception);
            (await overlays.GetAsync(id))!.Values["rating"].Should().Be("AA");
            return true;
        });
        reported.Should().BeOfType<PostgresException>().Which.SqlState.Should().Be(PostgresErrorCodes.DivisionByZero);
        var retained = (await new PostgresOperatorOverridesStore(fixture.Options).GetAsync(id))!;
        retained.Values["rating"].Should().Be("AA");
        retained.AuditTrail.Should().NotContain(entry => entry.Actor == "optional");
        (await GenerationAsync(id)).Should().Be(1);
    }
}

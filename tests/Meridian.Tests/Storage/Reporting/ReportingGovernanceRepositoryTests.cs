using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Meridian.Contracts.Workstation;
using Meridian.Reporting;
using Meridian.Storage.Reporting;
using Meridian.TestSupport;
using Npgsql;
using NpgsqlTypes;
using Xunit;

namespace Meridian.Tests.Storage.Reporting;

[Trait("Category", "Integration")]
public sealed class ReportingGovernanceRepositoryTests :
    IClassFixture<ReportingGovernanceDatabaseFixture>,
    IAsyncLifetime
{
    private readonly ReportingGovernanceDatabaseFixture _database;

    public ReportingGovernanceRepositoryTests(ReportingGovernanceDatabaseFixture database)
    {
        _database = database;
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => _database.ResetAsync();

    [ReportingDatabaseFact]
    public async Task Lifecycle_PersistsTenantBoundStateAndImmutableAuditAcrossRepositoryInstances()
    {
        var scenario = NewScenario();
        scenario.Creator.PrincipalIds.IsDefault.Should().BeTrue();
        var released = await CreateReleasedRunAsync(_database.Repository, scenario);

        var restarted = new PostgresReportingGovernanceRepository(_database.Options);
        var retained = await LoadRunAsync(restarted, scenario.TenantId, released.RunId);
        var crossTenant = await LoadRunAsync(restarted, $"other-{scenario.TenantId}", released.RunId);

        retained.Should().BeEquivalentTo(released);
        retained!.GovernanceState.Should().Be(GovernedReportingState.Released);
        retained.Release.Should().NotBeNull();
        retained.CreationAuthority.PrincipalIds.IsDefault.Should().BeFalse();
        retained.AuditTrail.Should().OnlyContain(entry => !entry.Authority.PrincipalIds.IsDefault);
        ReportingGovernanceAuditChain.Verify(retained.AuditTrail).Should().BeTrue();
        (await _database.CountAuditRowsAsync(
            scenario.TenantId,
            ReportingGovernanceAuditAggregateKind.Run,
            released.RunId)).Should().Be(released.Version);
        crossTenant.Should().BeNull();
    }

    [ReportingDatabaseFact]
    public async Task ReplaceRunAsync_RejectsAStaleDurableVersionWithoutAppendingAudit()
    {
        var scenario = NewScenario();
        var service = NewService(_database.Repository);
        var created = await service.CreateRunAsync(scenario.CreationRequest, scenario.Creator);
        var started = await service.BeginExecutionAsync(created.RunId, created.Version, scenario.Creator);

        Func<Task> staleWrite = async () =>
            await _database.Repository.ExecuteTransactionAsync(async (transaction, ct) =>
            {
                await transaction.ReplaceRunAsync(started, expectedVersion: created.Version, ct);
                return true;
            });

        await staleWrite.Should().ThrowAsync<ReportingGovernanceConcurrencyException>()
            .WithMessage("*version conflict*");

        var retained = await LoadRunAsync(_database.Repository, scenario.TenantId, created.RunId);
        retained!.Version.Should().Be(started.Version);
        retained.AuditTrail.Should().HaveCount(2);
        ReportingGovernanceAuditChain.Verify(retained.AuditTrail).Should().BeTrue();
    }

    [ReportingDatabaseFact]
    public async Task ApproveRestatementAsync_AtomicallyCreatesTheNextRevisionAndUpdatesTheRequest()
    {
        var scenario = NewScenario();
        var service = NewService(_database.Repository);
        var predecessor = await CreateReleasedRunAsync(service, scenario);
        var request = await service.RequestRestatementAsync(
            new ReportingRestatementRequestCommand(
                predecessor.RunId,
                predecessor.Version,
                "Late administrator correction",
                [new ReportingRestatementChangedLine("nav.total", "100", "101", ["evidence-change-1"])]),
            scenario.Creator);

        var replacementSnapshot = scenario.CreationRequest.Snapshot with
        {
            SnapshotId = $"snapshot-{Guid.NewGuid():N}",
            SnapshotHash = new string('f', 64),
            CapturedAtUtc = DateTimeOffset.UtcNow
        };
        var approved = await service.ApproveRestatementAsync(
            new ReportingRestatementApprovalCommand(
                request.RequestId,
                request.Version,
                replacementSnapshot,
                $"restated-run-{Guid.NewGuid():N}",
                request.ChangedLines),
            scenario.RestatementApprover);

        var retainedRequest = await LoadRestatementAsync(
            _database.Repository,
            scenario.TenantId,
            request.RequestId);
        var revisions = await ListSeriesAsync(
            _database.Repository,
            scenario.TenantId,
            predecessor.SeriesId);

        retainedRequest.Should().BeEquivalentTo(approved.Request);
        retainedRequest!.State.Should().Be(ReportingRestatementRequestState.Approved);
        retainedRequest.DraftRunId.Should().Be(approved.DraftRun.RunId);
        revisions.Select(static run => run.Revision).Should().Equal(1, 2);
        revisions[1].RestatementOfRunId.Should().Be(predecessor.RunId);
        revisions[1].GovernanceState.Should().Be(GovernedReportingState.Draft);
        ReportingGovernanceAuditChain.Verify(retainedRequest.AuditTrail).Should().BeTrue();
        ReportingGovernanceAuditChain.Verify(revisions[1].AuditTrail).Should().BeTrue();
    }

    [ReportingDatabaseFact]
    public async Task ApproveRestatementAsync_RollsBackRequestAndAuditWhenDraftRevisionInsertFails()
    {
        var scenario = NewScenario();
        var initialService = NewService(_database.Repository);
        var predecessor = await CreateReleasedRunAsync(initialService, scenario);
        var requestId = $"restatement-{Guid.NewGuid():N}";
        var conflictingService = NewService(
            _database.Repository,
            prefix => prefix == "restatement" ? requestId : predecessor.RunId);
        var request = await conflictingService.RequestRestatementAsync(
            new ReportingRestatementRequestCommand(
                predecessor.RunId,
                predecessor.Version,
                "Late correction requiring rollback proof",
                [new ReportingRestatementChangedLine("expense.total", "4", "5", ["evidence-change-2"])]),
            scenario.Creator);

        var replacementSnapshot = scenario.CreationRequest.Snapshot with
        {
            SnapshotId = $"snapshot-{Guid.NewGuid():N}",
            SnapshotHash = new string('f', 64),
            CapturedAtUtc = DateTimeOffset.UtcNow
        };

        Func<Task> approve = async () => await conflictingService.ApproveRestatementAsync(
            new ReportingRestatementApprovalCommand(
                request.RequestId,
                request.Version,
                replacementSnapshot,
                predecessor.RunId,
                request.ChangedLines),
            scenario.RestatementApprover);

        await approve.Should().ThrowAsync<ReportingGovernanceConcurrencyException>();

        var retainedRequest = await LoadRestatementAsync(
            _database.Repository,
            scenario.TenantId,
            request.RequestId);
        var revisions = await ListSeriesAsync(
            _database.Repository,
            scenario.TenantId,
            predecessor.SeriesId);

        retainedRequest!.State.Should().Be(ReportingRestatementRequestState.PendingApproval);
        retainedRequest.Version.Should().Be(1);
        retainedRequest.AuditTrail.Should().HaveCount(1);
        retainedRequest.DraftRunId.Should().BeNull();
        revisions.Should().ContainSingle();
    }

    [ReportingDatabaseFact]
    public async Task AuditTable_RejectsUpdateAndDelete()
    {
        var scenario = NewScenario();
        var service = NewService(_database.Repository);
        var created = await service.CreateRunAsync(scenario.CreationRequest, scenario.Creator);

        Func<Task> update = () => _database.ExecuteAuditMutationAsync(
            "set event_payload = event_payload",
            scenario.TenantId,
            ReportingGovernanceAuditAggregateKind.Run,
            created.RunId,
            delete: false);
        Func<Task> delete = () => _database.ExecuteAuditMutationAsync(
            string.Empty,
            scenario.TenantId,
            ReportingGovernanceAuditAggregateKind.Run,
            created.RunId,
            delete: true);

        (await update.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be("55000");
        (await delete.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be("55000");
        (await LoadRunAsync(_database.Repository, scenario.TenantId, created.RunId))!
            .AuditTrail.Should().ContainSingle();
    }

    [ReportingDatabaseFact]
    public async Task Read_FailsClosedWhenStatePayloadOrAuditChainIsCorrupt()
    {
        var stateScenario = NewScenario();
        var service = NewService(_database.Repository);
        var stateRun = await service.CreateRunAsync(stateScenario.CreationRequest, stateScenario.Creator);
        await _database.CorruptRunPayloadAsync(stateScenario.TenantId, stateRun.RunId);

        Func<Task> corruptStateRead = async () =>
            await LoadRunAsync(_database.Repository, stateScenario.TenantId, stateRun.RunId);
        await corruptStateRead.Should().ThrowAsync<ReportingGovernancePersistenceException>()
            .WithMessage("*SHA-256*");

        var auditScenario = NewScenario();
        var auditRun = await service.CreateRunAsync(auditScenario.CreationRequest, auditScenario.Creator);
        await _database.RemoveAuditChainAsync(
            auditScenario.TenantId,
            ReportingGovernanceAuditAggregateKind.Run,
            auditRun.RunId);

        Func<Task> corruptAuditRead = async () =>
            await LoadRunAsync(_database.Repository, auditScenario.TenantId, auditRun.RunId);
        await corruptAuditRead.Should().ThrowAsync<ReportingGovernancePersistenceException>()
            .WithMessage("*chain*");
    }

    [ReportingDatabaseFact]
    public async Task LegacyV1AndCanonicalV2_CoexistWithoutLegacyPoisoningCurrentAppendRestartOrExport()
    {
        var scenario = NewScenario();
        var service = NewService(_database.Repository);
        var legacy = await service.CreateRunAsync(scenario.CreationRequest, scenario.Creator);
        await _database.ConvertRunToLegacyV1Async(scenario.TenantId, legacy.RunId);

        var currentRequest = scenario.CreationRequest with
        {
            RunId = $"current-{Guid.NewGuid():N}",
            SeriesId = $"current-series-{Guid.NewGuid():N}"
        };
        var current = await service.CreateRunAsync(currentRequest, scenario.Creator);
        current = await service.BeginExecutionAsync(current.RunId, current.Version, scenario.Creator);

        var restarted = new PostgresReportingGovernanceRepository(_database.Options);
        var retainedCurrent = await LoadRunAsync(restarted, scenario.TenantId, current.RunId);
        var evidenceAuthority = scenario.Creator with
        {
            Permissions = scenario.Creator.Permissions.Add(
                ReportingGovernancePermission.ExportPersistenceEvidence)
        };
        var statuses = await restarted.ExecuteTransactionAsync(
            (transaction, ct) => transaction.ListPersistenceStatusAsync(evidenceAuthority, ct));
        var legacyStatus = statuses.Single(status => status.AggregateId == legacy.RunId);
        var currentStatus = statuses.Single(status => status.AggregateId == current.RunId);
        var export = await restarted.ExecuteTransactionAsync(
            (transaction, ct) => transaction.ExportPersistenceRecordAsync(
                evidenceAuthority,
                ReportingGovernanceAuditAggregateKind.Run,
                legacy.RunId,
                ct));

        retainedCurrent!.Version.Should().Be(2);
        retainedCurrent.ExecutionState.Should().Be(GovernedReportingExecutionState.Running);
        legacyStatus.Disposition.Should().Be(
            ReportingGovernancePersistenceDisposition.LegacyReadOnlyRecertificationRequired);
        legacyStatus.StateChecksumVerified.Should().BeTrue();
        legacyStatus.AuditChainVerified.Should().BeTrue();
        legacyStatus.AuditHashFormats.Should().OnlyContain(
            format => format == ReportingGovernancePersistenceFormat.LegacyV1);
        currentStatus.Disposition.Should().Be(ReportingGovernancePersistenceDisposition.Current);
        currentStatus.AggregateVersion.Should().Be(2);
        currentStatus.AuditHashFormats.Should().OnlyContain(
            format => format == ReportingGovernancePersistenceFormat.CanonicalV2);
        export.Should().NotBeNull();
        export!.Status.Should().BeEquivalentTo(legacyStatus);
        export.AuditEvents.Should().ContainSingle()
            .Which.HashFormat.Should().Be(ReportingGovernancePersistenceFormat.LegacyV1);

        Func<Task> readLegacy = async () =>
            await LoadRunAsync(restarted, scenario.TenantId, legacy.RunId);
        Func<Task> mutateLegacy = async () =>
            await service.BeginExecutionAsync(legacy.RunId, legacy.Version, scenario.Creator);
        await readLegacy.Should().ThrowAsync<ReportingGovernanceLegacyAggregateException>();
        await mutateLegacy.Should().ThrowAsync<ReportingGovernanceLegacyAggregateException>();
        Func<Task> unprivilegedInventory = async () =>
            await restarted.ExecuteTransactionAsync(
                (transaction, ct) => transaction.ListPersistenceStatusAsync(scenario.Creator, ct));
        await unprivilegedInventory.Should().ThrowAsync<ReportingGovernanceAuthorizationException>()
            .WithMessage("*ExportPersistenceEvidence*");

        var crossTenant = evidenceAuthority with { TenantId = $"other-{scenario.TenantId}" };
        (await restarted.ExecuteTransactionAsync(
                (transaction, ct) => transaction.ListPersistenceStatusAsync(crossTenant, ct)))
            .Should().BeEmpty();
        (await restarted.ExecuteTransactionAsync(
                (transaction, ct) => transaction.ExportPersistenceRecordAsync(
                    crossTenant,
                    ReportingGovernanceAuditAggregateKind.Run,
                    legacy.RunId,
                    ct)))
            .Should().BeNull();
    }

    [ReportingDatabaseFact]
    public async Task LegacyEvidenceInventoryAndExport_RequireExactOrganizationAndNullableCompanyScope()
    {
        var scenario = NewScenario();
        var service = NewService(_database.Repository);
        var scopedLegacy = await service.CreateRunAsync(scenario.CreationRequest, scenario.Creator);
        await _database.ConvertRunToLegacyV1Async(scenario.TenantId, scopedLegacy.RunId);

        var unscopedRequest = scenario.CreationRequest with
        {
            RunId = $"unscoped-legacy-{Guid.NewGuid():N}",
            SeriesId = $"unscoped-series-{Guid.NewGuid():N}"
        };
        var unscopedLegacy = await service.CreateRunAsync(unscopedRequest, scenario.Creator);
        await _database.ConvertRunToLegacyV1Async(scenario.TenantId, unscopedLegacy.RunId);
        await _database.RemoveLegacyRunCompanyScopeAsync(scenario.TenantId, unscopedLegacy.RunId);

        var scopedEvidenceAuthority = scenario.Creator with
        {
            Permissions = scenario.Creator.Permissions.Add(
                ReportingGovernancePermission.ExportPersistenceEvidence)
        };
        var scopedStatuses = await _database.Repository.ExecuteTransactionAsync(
            (transaction, ct) => transaction.ListPersistenceStatusAsync(scopedEvidenceAuthority, ct));
        scopedStatuses.Should().ContainSingle(status => status.AggregateId == scopedLegacy.RunId);
        scopedStatuses.Should().NotContain(status => status.AggregateId == unscopedLegacy.RunId);

        var crossCompany = scopedEvidenceAuthority with { CompanyId = "company-other" };
        (await _database.Repository.ExecuteTransactionAsync(
                (transaction, ct) => transaction.ListPersistenceStatusAsync(crossCompany, ct)))
            .Should().BeEmpty();
        (await _database.Repository.ExecuteTransactionAsync(
                (transaction, ct) => transaction.ExportPersistenceRecordAsync(
                    crossCompany,
                    ReportingGovernanceAuditAggregateKind.Run,
                    scopedLegacy.RunId,
                    ct)))
            .Should().BeNull();

        var crossOrganization = scopedEvidenceAuthority with { OrganizationId = "organization-other" };
        (await _database.Repository.ExecuteTransactionAsync(
                (transaction, ct) => transaction.ListPersistenceStatusAsync(crossOrganization, ct)))
            .Should().BeEmpty();
        (await _database.Repository.ExecuteTransactionAsync(
                (transaction, ct) => transaction.ExportPersistenceRecordAsync(
                    crossOrganization,
                    ReportingGovernanceAuditAggregateKind.Run,
                    scopedLegacy.RunId,
                    ct)))
            .Should().BeNull();

        var tenantWideRecovery = scopedEvidenceAuthority with { CompanyId = null };
        var recoveryStatuses = await _database.Repository.ExecuteTransactionAsync(
            (transaction, ct) => transaction.ListPersistenceStatusAsync(tenantWideRecovery, ct));
        recoveryStatuses.Should().ContainSingle(status => status.AggregateId == unscopedLegacy.RunId);
        recoveryStatuses.Should().NotContain(status => status.AggregateId == scopedLegacy.RunId);
        (await _database.Repository.ExecuteTransactionAsync(
                (transaction, ct) => transaction.ExportPersistenceRecordAsync(
                    tenantWideRecovery,
                    ReportingGovernanceAuditAggregateKind.Run,
                    unscopedLegacy.RunId,
                    ct)))
            .Should().NotBeNull();
        (await _database.Repository.ExecuteTransactionAsync(
                (transaction, ct) => transaction.ExportPersistenceRecordAsync(
                    scopedEvidenceAuthority,
                    ReportingGovernanceAuditAggregateKind.Run,
                    unscopedLegacy.RunId,
                    ct)))
            .Should().BeNull();

        var crossOrganizationRecovery = tenantWideRecovery with { OrganizationId = "organization-other" };
        (await _database.Repository.ExecuteTransactionAsync(
                (transaction, ct) => transaction.ExportPersistenceRecordAsync(
                    crossOrganizationRecovery,
                    ReportingGovernanceAuditAggregateKind.Run,
                    unscopedLegacy.RunId,
                    ct)))
            .Should().BeNull();
    }

    [ReportingDatabaseFact]
    public async Task TamperedLegacyV1_IsInventoryIntegrityFailureAndCannotBeReadOrExported()
    {
        var scenario = NewScenario();
        var service = NewService(_database.Repository);
        var legacy = await service.CreateRunAsync(scenario.CreationRequest, scenario.Creator);
        await _database.ConvertRunToLegacyV1Async(scenario.TenantId, legacy.RunId);
        await _database.CorruptRunPayloadAsync(scenario.TenantId, legacy.RunId);

        var evidenceAuthority = scenario.Creator with
        {
            Permissions = scenario.Creator.Permissions.Add(
                ReportingGovernancePermission.ExportPersistenceEvidence)
        };
        var statuses = await _database.Repository.ExecuteTransactionAsync(
            (transaction, ct) => transaction.ListPersistenceStatusAsync(evidenceAuthority, ct));
        var status = statuses.Single(item => item.AggregateId == legacy.RunId);
        status.Disposition.Should().Be(ReportingGovernancePersistenceDisposition.IntegrityFailure);
        status.StateChecksumVerified.Should().BeFalse();
        status.AuditChainVerified.Should().BeFalse();

        Func<Task> read = async () =>
            await LoadRunAsync(_database.Repository, scenario.TenantId, legacy.RunId);
        Func<Task> export = async () =>
            await _database.Repository.ExecuteTransactionAsync(
                (transaction, ct) => transaction.ExportPersistenceRecordAsync(
                    evidenceAuthority,
                    ReportingGovernanceAuditAggregateKind.Run,
                    legacy.RunId,
                    ct));
        await read.Should().ThrowAsync<ReportingGovernancePersistenceException>()
            .WithMessage("*SHA-256*");
        await export.Should().ThrowAsync<ReportingGovernancePersistenceException>()
            .WithMessage("*SHA-256*");
    }

    [ReportingDatabaseFact]
    public async Task Read_FailsClosedWhenChecksummedPayloadHasInvalidCanonicalScope()
    {
        var scenario = NewScenario();
        var service = NewService(_database.Repository);
        var run = await service.CreateRunAsync(scenario.CreationRequest, scenario.Creator);
        await _database.ReplaceRunCompanyWithNullAndRehashAsync(scenario.TenantId, run.RunId);

        Func<Task> read = async () =>
            await LoadRunAsync(_database.Repository, scenario.TenantId, run.RunId);

        await read.Should().ThrowAsync<ReportingGovernancePersistenceException>()
            .WithMessage("*canonical immutable state is invalid*");
    }

    [ReportingDatabaseFact]
    public async Task Read_FailsClosedWhenIndexedOrganizationOrCompanyDriftsFromChecksummedPayload()
    {
        foreach (var column in new[] { "organization_id", "company_id" })
        {
            var scenario = NewScenario();
            var service = NewService(_database.Repository);
            var run = await service.CreateRunAsync(scenario.CreationRequest, scenario.Creator);
            await _database.ReplaceRunIndexedScopeAsync(
                scenario.TenantId,
                run.RunId,
                column,
                $"tampered-{column}-{Guid.NewGuid():N}");

            Func<Task> read = async () =>
                await LoadRunAsync(_database.Repository, scenario.TenantId, run.RunId);

            await read.Should().ThrowAsync<ReportingGovernancePersistenceException>()
                .WithMessage("*identity or version columns do not match*");
        }
    }

    [ReportingDatabaseFact]
    public async Task Read_FailsClosedWhenChecksummedLifecycleReceiptEscapesRunScope()
    {
        var scenario = NewScenario();
        var released = await CreateReleasedRunAsync(_database.Repository, scenario);
        await _database.MutateRunPayloadAndRehashAsync(
            scenario.TenantId,
            released.RunId,
            root => RequiredObject(root, "Readiness", "readiness")[
                PropertyName(RequiredObject(root, "Readiness", "readiness"), "OrganizationId", "organizationId")]
                = "other-organization");

        Func<Task> read = async () =>
            await LoadRunAsync(_database.Repository, scenario.TenantId, released.RunId);

        await read.Should().ThrowAsync<ReportingGovernancePersistenceException>()
            .WithMessage("*canonical immutable state is invalid*exact run, tenant, organization, company*");
    }

    [ReportingDatabaseFact]
    public async Task Read_FailsClosedWhenChecksummedRestatementRequesterLosesHumanOrigin()
    {
        var scenario = NewScenario();
        var service = NewService(_database.Repository);
        var predecessor = await CreateReleasedRunAsync(service, scenario);
        var request = await service.RequestRestatementAsync(
            new ReportingRestatementRequestCommand(
                predecessor.RunId,
                predecessor.Version,
                "Correct retained source data",
                [new ReportingRestatementChangedLine("nav.total", "100", "101", ["evidence-change"])]),
            scenario.Creator);
        await _database.MutateRestatementPayloadAndRehashAsync(
            scenario.TenantId,
            request.RequestId,
            root => RequiredObject(root, "RequestedBy", "requestedBy")[
                PropertyName(RequiredObject(root, "RequestedBy", "requestedBy"), "Origin", "origin")]
                = (int)ReportingCommandOrigin.ServicePrincipal);

        Func<Task> read = async () =>
            await LoadRestatementAsync(_database.Repository, scenario.TenantId, request.RequestId);

        await read.Should().ThrowAsync<ReportingGovernancePersistenceException>()
            .WithMessage("*human operator*");
    }

    [ReportingDatabaseFact]
    public async Task Read_FailsClosedWhenChecksummedAuditPrincipalAudienceIsTampered()
    {
        var scenario = NewScenario();
        var service = NewService(_database.Repository);
        var created = await service.CreateRunAsync(scenario.CreationRequest, scenario.Creator);
        await _database.MutateFirstAuditPayloadAndRehashAsync(
            scenario.TenantId,
            ReportingGovernanceAuditAggregateKind.Run,
            created.RunId,
            root => RequiredObject(root, "Authority", "authority")[
                PropertyName(RequiredObject(root, "Authority", "authority"), "PrincipalIds", "principalIds")]
                = new JsonArray("injected-audience"));

        Func<Task> read = async () =>
            await LoadRunAsync(_database.Repository, scenario.TenantId, created.RunId);

        await read.Should().ThrowAsync<ReportingGovernancePersistenceException>()
            .WithMessage("*chain*invalid*");
    }

    [ReportingDatabaseFact]
    public async Task MigrationRunner_RecordsTheChecksummedGovernanceMigration()
    {
        await Task.WhenAll(Enumerable.Range(0, 3)
            .Select(_ => new ReportingMigrationRunner(_database.Options).EnsureMigratedAsync()));

        (await _database.ListMigrationFilesAsync()).Should().Contain("002_reporting_governance.sql");
        (await _database.ListMigrationFilesAsync()).Should().Contain("007_reporting_governance_scope_hardening.sql");
        (await _database.ListMigrationFilesAsync()).Should().Contain("008_reporting_governance_format_versions.sql");
    }

    [ReportingDatabaseFact]
    public async Task Migration_RejectsRollingV1WriterThatOmitsExplicitFormatVersion()
    {
        Func<Task> insertFromLegacyWriter = () => _database.InsertRunWithoutFormatVersionAsync();

        (await insertFromLegacyWriter.Should().ThrowAsync<PostgresException>())
            .Which.SqlState.Should().Be("23502");
    }

    [ReportingDatabaseFact]
    public async Task CreateRunAsync_RejectsMalformedCanonicalScopeAccessAndSnapshotEvidence()
    {
        var scenario = NewScenario();
        var service = NewService(_database.Repository);
        var invalidRequests = new ReportingRunCreationRequest[]
        {
            scenario.CreationRequest with
            {
                Scope = scenario.CreationRequest.Scope with { CompanyId = null }
            },
            scenario.CreationRequest with
            {
                Scope = scenario.CreationRequest.Scope with { FundId = null }
            },
            scenario.CreationRequest with
            {
                Access = scenario.CreationRequest.Access with
                {
                    Mode = (ReportingGovernanceAccessMode)999
                }
            },
            scenario.CreationRequest with
            {
                Access = scenario.CreationRequest.Access with
                {
                    PolicyHash = new string('A', 64)
                }
            },
            scenario.CreationRequest with
            {
                Snapshot = scenario.CreationRequest.Snapshot with
                {
                    SnapshotHash = "not-a-sha256"
                }
            },
            scenario.CreationRequest with
            {
                Snapshot = scenario.CreationRequest.Snapshot with
                {
                    CapturedAtUtc = new DateTimeOffset(2026, 7, 15, 12, 0, 0, TimeSpan.FromHours(1))
                }
            },
            scenario.CreationRequest with
            {
                Snapshot = scenario.CreationRequest.Snapshot with
                {
                    SourceCheckpointId = scenario.CreationRequest.Snapshot.ReconciliationCheckpointId
                }
            },
            scenario.CreationRequest with
            {
                Snapshot = scenario.CreationRequest.Snapshot with
                {
                    ParametersHash = new string('0', 64)
                }
            }
        };

        foreach (var invalidRequest in invalidRequests)
        {
            Func<Task> create = async () => await service.CreateRunAsync(invalidRequest, scenario.Creator);
            await create.Should().ThrowAsync<ReportingGovernanceException>();
        }
    }

    private static async Task<GovernedReportingRun> CreateReleasedRunAsync(
        PostgresReportingGovernanceRepository repository,
        GovernanceScenario scenario) =>
        await CreateReleasedRunAsync(NewService(repository), scenario);

    private static async Task<GovernedReportingRun> CreateReleasedRunAsync(
        ReportingGovernanceService service,
        GovernanceScenario scenario)
    {
        var run = await service.CreateRunAsync(scenario.CreationRequest, scenario.Creator);
        run = await service.BeginExecutionAsync(run.RunId, run.Version, scenario.Creator);
        run = await service.CompleteExecutionAsync(run.RunId, run.Version, scenario.Creator);
        var readiness = new ReportingReadinessReceipt(
            $"readiness-{Guid.NewGuid():N}",
            string.Empty,
            run.RunId,
            scenario.TenantId,
            run.Scope.OrganizationId,
            run.Scope.CompanyId,
            run.Snapshot.SnapshotId,
            run.Snapshot.SnapshotHash,
            DateTimeOffset.UtcNow,
            [new ReportingReadinessCheck("ledger-reconciled", true, ["evidence-ready-1"])]);
        readiness = readiness with
        {
            ReceiptHash = ReportingGovernanceCanonicalValidation.ComputeReadinessReceiptHash(readiness)
        };
        run = await service.ValidateAsync(
            run.RunId,
            run.Version,
            readiness,
            scenario.Creator);
        run = await service.SubmitAsync(run.RunId, run.Version, scenario.Creator);
        run = await service.ApproveAsync(run.RunId, run.Version, "Reviewed and approved", scenario.Approver);
        return await service.ReleaseAsync(
            run.RunId,
            run.Version,
            new ReportingReleaseEvidence(
                $"manifest-{Guid.NewGuid():N}",
                new string('8', 64),
                [new ReportingArtifactReference("artifact-1", new string('a', 64), 128)],
                ["evidence-release-1"]),
            scenario.Releaser);
    }

    private static ReportingGovernanceService NewService(
        PostgresReportingGovernanceRepository repository,
        Func<string, string>? idFactory = null) =>
        new(repository, idFactory: idFactory);

    private static JsonObject RequiredObject(
        JsonObject root,
        string pascalName,
        string camelName) =>
        (root[pascalName] ?? root[camelName])?.AsObject()
        ?? throw new InvalidOperationException($"The payload did not contain '{pascalName}'.");

    private static string PropertyName(
        JsonObject value,
        string pascalName,
        string camelName) =>
        value.ContainsKey(pascalName) ? pascalName : camelName;

    private static async Task<GovernedReportingRun?> LoadRunAsync(
        PostgresReportingGovernanceRepository repository,
        string tenantId,
        string runId) =>
        await repository.ExecuteTransactionAsync(
            (transaction, ct) => transaction.GetRunAsync(tenantId, runId, ct));

    private static async Task<ReportingRestatementRequest?> LoadRestatementAsync(
        PostgresReportingGovernanceRepository repository,
        string tenantId,
        string requestId) =>
        await repository.ExecuteTransactionAsync(
            (transaction, ct) => transaction.GetRestatementRequestAsync(tenantId, requestId, ct));

    private static async Task<IReadOnlyList<GovernedReportingRun>> ListSeriesAsync(
        PostgresReportingGovernanceRepository repository,
        string tenantId,
        string seriesId) =>
        await repository.ExecuteTransactionAsync(
            (transaction, ct) => transaction.ListRunsBySeriesAsync(tenantId, seriesId, ct));

    private static GovernanceScenario NewScenario()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var tenantId = $"tenant-{suffix}";
        var companyId = $"company-{suffix}";
        var scope = new ReportingOperationalScope(
            tenantId,
            $"organization-{suffix}",
            companyId,
            $"fund-{suffix}",
            $"book-{suffix}",
            $"period-{suffix}");
        var parametersJson = CanonicalParametersJson(scope, ReportingFinalityDto.Final);
        var parametersHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(parametersJson)))
            .ToLowerInvariant();
        var request = new ReportingRunCreationRequest(
            $"run-{suffix}",
            $"series-{suffix}",
            "template-monthly-financials",
            "1.0.0",
            scope,
            new ReportingAccessScope(
                $"policy-{suffix}",
                "1",
                ReportingGovernanceAccessMode.CompanyWide,
                OwnerPrincipalId: null,
                AllowOwnerAccess: false,
                Principals: [],
                PolicyHash: new string('b', 64)),
            new ReportingCertifiedSnapshotScope(
                tenantId,
                scope.OrganizationId,
                companyId,
                scope.FundId,
                scope.BookId,
                scope.PeriodId,
                $"snapshot-{suffix}",
                new string('c', 64),
                $"reconciliation-{suffix}",
                DateTimeOffset.UtcNow,
                SourceCheckpointId: $"source-{suffix}",
                SourceCheckpointHash: new string('d', 64),
                ReconciliationCheckpointHash: new string('e', 64),
                ParametersCanonicalJson: parametersJson,
                ParametersHash: parametersHash));

        return new GovernanceScenario(
            tenantId,
            request,
            Authority("creator", scope),
            Authority("approver", scope),
            Authority("releaser", scope),
            Authority("restatement-approver", scope));
    }

    private static ReportingAuthorityScope Authority(string actorPrefix, ReportingOperationalScope scope) =>
        new(
            $"{actorPrefix}-{Guid.NewGuid():N}",
            scope.TenantId,
            scope.OrganizationId,
            scope.CompanyId,
            Enum.GetValues<ReportingGovernancePermission>()
                .Where(static permission =>
                    permission != ReportingGovernancePermission.ExportPersistenceEvidence)
                .ToImmutableArray(),
            ReportingCommandOrigin.HumanOperator,
            $"correlation-{Guid.NewGuid():N}");

    private static string CanonicalParametersJson(
        ReportingOperationalScope scope,
        ReportingFinalityDto finality) =>
        JsonSerializer.Serialize(new
        {
            scope = new
            {
                fundProfileId = scope.FundId,
                entityScopeKind = ReportingEntityScopeKindDto.AllEntities.ToString(),
                entityId = (string?)null,
                portfolioId = (string?)null,
                investorId = (string?)null,
                dimensions = (object?)null
            },
            periodId = scope.PeriodId,
            asOfDate = "2026-07-15",
            ledgerBookId = (string?)null,
            ledgerBookCode = scope.BookId,
            accountingBasis = ReportingAccountingBasisDto.Gaap.ToString(),
            presentationCurrency = "USD",
            consolidationLevel = ReportingConsolidationLevelDto.Fund.ToString(),
            outputFormat = ReportingOutputFormatDto.Pdf.ToString(),
            finality = finality.ToString(),
            includeSupportingSchedules = true,
            includeEvidenceAppendix = finality == ReportingFinalityDto.Final,
            templateParameters = new Dictionary<string, string>()
        });

    private sealed record GovernanceScenario(
        string TenantId,
        ReportingRunCreationRequest CreationRequest,
        ReportingAuthorityScope Creator,
        ReportingAuthorityScope Approver,
        ReportingAuthorityScope Releaser,
        ReportingAuthorityScope RestatementApprover);
}

/// <summary>
/// Guards governed-report persistence when an identity provider supplies no additional principal
/// audiences and optional immutable collections therefore arrive in their default state.
/// </summary>
public sealed class ReportingGovernancePersistenceSerializationTests
{
    [Fact]
    public void RunPayload_DefaultImmutableCollections_RoundTripsAsCanonicalEmptyCollections()
    {
        var authority = NewAuthority();
        var audit = NewAudit(authority);
        var run = new GovernedReportingRun(
            "run-default-audiences",
            "series-default-audiences",
            Revision: 1,
            "template-monthly-financials",
            "1.0.0",
            NewScope(),
            new ReportingAccessScope(
                "policy-default-audiences",
                "1",
                ReportingGovernanceAccessMode.CompanyWide,
                OwnerPrincipalId: null,
                AllowOwnerAccess: false,
                Principals: default,
                PolicyHash: new string('b', 64)),
            NewSnapshot(),
            authority,
            FixedUtc,
            RestatementOfRunId: null,
            GovernedReportingExecutionState.Succeeded,
            GovernedReportingState.Released,
            Version: 1,
            new ReportingReadinessReceipt(
                "readiness-default-audiences",
                new string('c', 64),
                "run-default-audiences",
                "tenant-default-audiences",
                "organization-default-audiences",
                "company-default-audiences",
                "snapshot-default-audiences",
                new string('d', 64),
                FixedUtc,
                [new ReportingReadinessCheck("ledger-ready", true, default)]),
            new ReportingApprovalReceipt(authority, FixedUtc, "Approved"),
            new ReportingReleaseReceipt(
                authority,
                FixedUtc,
                "manifest-default-audiences",
                new string('e', 64),
                Artifacts: default,
                EvidenceIds: default),
            [audit]);

        var payload = PostgresReportingGovernanceRepository.SerializeRunForPersistence(run);
        var retained = PostgresReportingGovernanceRepository.DeserializeRunFromPersistence(payload);

        retained.Access.Principals.IsDefault.Should().BeFalse();
        retained.CreationAuthority.PrincipalIds.IsDefault.Should().BeFalse();
        retained.Readiness!.Checks.Single().EvidenceIds.IsDefault.Should().BeFalse();
        retained.Approval!.Authority.PrincipalIds.IsDefault.Should().BeFalse();
        retained.Release!.Authority.PrincipalIds.IsDefault.Should().BeFalse();
        retained.Release.Artifacts.IsDefault.Should().BeFalse();
        retained.Release.EvidenceIds.IsDefault.Should().BeFalse();
        retained.AuditTrail.Single().Authority.PrincipalIds.IsDefault.Should().BeFalse();
    }

    [Fact]
    public void RestatementPayload_DefaultImmutableCollections_RoundTripsAsCanonicalEmptyCollections()
    {
        var authority = NewAuthority();
        var request = new ReportingRestatementRequest(
            "restatement-default-audiences",
            "run-default-audiences",
            "series-default-audiences",
            PredecessorRevision: 1,
            PredecessorVersion: 1,
            "Correct retained source data",
            [new ReportingRestatementChangedLine("nav.total", "100", "101", default)],
            authority,
            FixedUtc,
            ReportingRestatementRequestState.Approved,
            Version: 2,
            authority,
            FixedUtc,
            "run-restated-default-audiences",
            AuditTrail: default,
            RequestedChangedLines:
            [
                new ReportingRestatementChangedLine(
                    "nav.total",
                    "100",
                    "101",
                    default)
            ]);

        var payload = PostgresReportingGovernanceRepository.SerializeRestatementForPersistence(request);
        var retained = PostgresReportingGovernanceRepository.DeserializeRestatementFromPersistence(payload);

        retained.ChangedLines.Single().EvidenceIds.IsDefault.Should().BeFalse();
        retained.RequestedBy.PrincipalIds.IsDefault.Should().BeFalse();
        retained.ApprovedBy!.PrincipalIds.IsDefault.Should().BeFalse();
        retained.AuditTrail.IsDefault.Should().BeFalse();
        retained.RequestedChangedLines.Single().EvidenceIds.IsDefault.Should().BeFalse();
    }

    [Fact]
    public void AuditPayload_DefaultPrincipalAudience_RoundTripsAsCanonicalEmptyCollection()
    {
        var payload = PostgresReportingGovernanceRepository.SerializeAuditForPersistence(
            NewAudit(NewAuthority()));

        var retained = PostgresReportingGovernanceRepository.DeserializeAuditFromPersistence(payload);

        retained.Authority.PrincipalIds.IsDefault.Should().BeFalse();
    }

    private static readonly DateTimeOffset FixedUtc =
        new(2026, 8, 4, 20, 0, 0, TimeSpan.Zero);

    private static ReportingOperationalScope NewScope() =>
        new(
            "tenant-default-audiences",
            "organization-default-audiences",
            "company-default-audiences",
            "fund-default-audiences",
            "book-default-audiences",
            "period-default-audiences");

    private static ReportingCertifiedSnapshotScope NewSnapshot() =>
        new(
            "tenant-default-audiences",
            "organization-default-audiences",
            "company-default-audiences",
            "fund-default-audiences",
            "book-default-audiences",
            "period-default-audiences",
            "snapshot-default-audiences",
            new string('d', 64),
            "reconciliation-default-audiences",
            FixedUtc);

    private static ReportingAuthorityScope NewAuthority() =>
        new(
            "operator-default-audiences",
            "tenant-default-audiences",
            "organization-default-audiences",
            "company-default-audiences",
            [ReportingGovernancePermission.CreateRun],
            ReportingCommandOrigin.HumanOperator,
            "correlation-default-audiences");

    private static ReportingGovernanceAuditEntry NewAudit(ReportingAuthorityScope authority) =>
        new(
            "event-default-audiences",
            ReportingGovernanceAuditAggregateKind.Run,
            "run-default-audiences",
            AggregateVersion: 1,
            FixedUtc,
            ReportingGovernanceAuditAction.RunCreated,
            authority,
            ReportingGovernancePermission.CreateRun,
            FromExecutionState: null,
            ToExecutionState: GovernedReportingExecutionState.Queued,
            FromGovernanceState: null,
            ToGovernanceState: GovernedReportingState.Draft,
            FromRestatementState: null,
            ToRestatementState: null,
            null,
            null,
            new string('f', 64));
}

public sealed class ReportingGovernanceDatabaseFixture : IAsyncLifetime
{
    private static JsonObject RequiredObject(
        JsonObject root,
        string pascalName,
        string camelName) =>
        (root[pascalName] ?? root[camelName])?.AsObject()
        ?? throw new InvalidOperationException($"The payload did not contain '{pascalName}'.");

    private static string PropertyName(
        JsonObject value,
        string pascalName,
        string camelName) =>
        value.ContainsKey(pascalName) ? pascalName : camelName;

    private const string ConnectionStringVariable = "MERIDIAN_REPORTING_CONNECTION_STRING";
    private PostgresTestServer? _server;

    public ReportingArtifactStoreOptions Options { get; private set; } = null!;

    public PostgresReportingGovernanceRepository Repository { get; private set; } = null!;

    public string QualifiedRunsTable => $"\"{Options.Schema}\".\"reporting_governed_runs\"";

    public string QualifiedRestatementTable =>
        $"\"{Options.Schema}\".\"reporting_restatement_requests\"";

    public string QualifiedAuditTable => $"\"{Options.Schema}\".\"reporting_governance_audit\"";

    public async Task InitializeAsync()
    {
        _server = await PostgresTestServer.CreateAsync(ConnectionStringVariable).ConfigureAwait(false);
        Options = new ReportingArtifactStoreOptions
        {
            ConnectionString = _server.ConnectionString,
            Schema = _server.CreateSchemaName("reporting_governance")
        };

        try
        {
            await new ReportingMigrationRunner(Options).EnsureMigratedAsync().ConfigureAwait(false);
            Repository = new PostgresReportingGovernanceRepository(Options);
        }
        catch
        {
            await _server.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public async Task DisposeAsync()
    {
        if (_server is null)
        {
            return;
        }

        await _server.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Recreates this class fixture's schema after every test method, including scenarios that
    /// deliberately corrupt retained payloads or disable database guards.
    /// </summary>
    public async Task ResetAsync()
    {
        if (_server is null)
        {
            throw new InvalidOperationException("The reporting governance fixture is not initialized.");
        }

        var migrationRunner = new ReportingMigrationRunner(Options);
        await migrationRunner.ResetSchemaAsync().ConfigureAwait(false);
        await migrationRunner.EnsureMigratedAsync().ConfigureAwait(false);
        Repository = new PostgresReportingGovernanceRepository(Options);
    }

    public async Task<long> CountAuditRowsAsync(
        string tenantId,
        ReportingGovernanceAuditAggregateKind kind,
        string aggregateId)
    {
        await using var connection = await OpenConnectionAsync().ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"select count(*) from {QualifiedAuditTable} where tenant_id = @tenant_id and aggregate_kind = @kind and aggregate_id = @aggregate_id;";
        AddAuditIdentity(command, tenantId, kind, aggregateId);
        return (long)(await command.ExecuteScalarAsync().ConfigureAwait(false) ?? 0L);
    }

    public async Task InsertRunWithoutFormatVersionAsync()
    {
        await using var connection = await OpenConnectionAsync().ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            insert into {QualifiedRunsTable} (
                tenant_id,
                run_id,
                series_id,
                revision,
                organization_id,
                company_id,
                aggregate_version,
                execution_state,
                governance_state,
                state_payload,
                state_hash_sha256,
                created_at_utc,
                updated_at_utc)
            values (
                @tenant_id,
                @run_id,
                @series_id,
                1,
                @organization_id,
                @company_id,
                1,
                0,
                0,
                @state_payload,
                @state_hash,
                @occurred_at_utc,
                @occurred_at_utc);
            """;
        command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Text, $"rolling-tenant-{Guid.NewGuid():N}");
        command.Parameters.AddWithValue("run_id", NpgsqlDbType.Text, $"rolling-run-{Guid.NewGuid():N}");
        command.Parameters.AddWithValue("series_id", NpgsqlDbType.Text, $"rolling-series-{Guid.NewGuid():N}");
        command.Parameters.AddWithValue("organization_id", NpgsqlDbType.Text, "rolling-organization");
        command.Parameters.AddWithValue("company_id", NpgsqlDbType.Text, "rolling-company");
        command.Parameters.AddWithValue("state_payload", NpgsqlDbType.Text, "{\"Scope\":{\"FundId\":\"rolling-fund\"}}");
        command.Parameters.AddWithValue("state_hash", NpgsqlDbType.Text, new string('a', 64));
        command.Parameters.AddWithValue("occurred_at_utc", NpgsqlDbType.TimestampTz, DateTimeOffset.UtcNow);
        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<string>> ListMigrationFilesAsync()
    {
        var names = new List<string>();
        await using var connection = await OpenConnectionAsync().ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"select filename from \"{Options.Schema}\".\"reporting_schema_migrations\" order by filename;";
        await using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }

    public async Task ExecuteAuditMutationAsync(
        string updateClause,
        string tenantId,
        ReportingGovernanceAuditAggregateKind kind,
        string aggregateId,
        bool delete)
    {
        await using var connection = await OpenConnectionAsync().ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = delete
            ? $"delete from {QualifiedAuditTable} where tenant_id = @tenant_id and aggregate_kind = @kind and aggregate_id = @aggregate_id;"
            : $"update {QualifiedAuditTable} {updateClause} where tenant_id = @tenant_id and aggregate_kind = @kind and aggregate_id = @aggregate_id;";
        AddAuditIdentity(command, tenantId, kind, aggregateId);
        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    public async Task CorruptRunPayloadAsync(string tenantId, string runId)
    {
        await using var connection = await OpenConnectionAsync().ConfigureAwait(false);
        await SetUserTriggersAsync(connection, QualifiedRunsTable, enabled: false).ConfigureAwait(false);
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                $"update {QualifiedRunsTable} set state_payload = state_payload || ' ' where tenant_id = @tenant_id and run_id = @run_id;";
            command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Text, tenantId);
            command.Parameters.AddWithValue("run_id", NpgsqlDbType.Text, runId);
            await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        }
        finally
        {
            await SetUserTriggersAsync(connection, QualifiedRunsTable, enabled: true).ConfigureAwait(false);
        }
    }

    public async Task ConvertRunToLegacyV1Async(string tenantId, string runId)
    {
        await using var connection = await OpenConnectionAsync().ConfigureAwait(false);
        string statePayload;
        string eventPayload;
        await using (var readState = connection.CreateCommand())
        {
            readState.CommandText =
                $"select state_payload from {QualifiedRunsTable} where tenant_id = @tenant_id and run_id = @run_id;";
            readState.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Text, tenantId);
            readState.Parameters.AddWithValue("run_id", NpgsqlDbType.Text, runId);
            statePayload = (string)(await readState.ExecuteScalarAsync().ConfigureAwait(false)
                ?? throw new InvalidOperationException("The reporting run payload was not found."));
        }
        await using (var readAudit = connection.CreateCommand())
        {
            readAudit.CommandText =
                $"select event_payload from {QualifiedAuditTable} where tenant_id = @tenant_id and aggregate_kind = @kind and aggregate_id = @aggregate_id and aggregate_version = 1;";
            AddAuditIdentity(
                readAudit,
                tenantId,
                ReportingGovernanceAuditAggregateKind.Run,
                runId);
            eventPayload = (string)(await readAudit.ExecuteScalarAsync().ConfigureAwait(false)
                ?? throw new InvalidOperationException("The reporting run audit payload was not found."));
        }

        var stateRoot = JsonNode.Parse(statePayload)?.AsObject()
            ?? throw new InvalidOperationException("The reporting run payload was not a JSON object.");
        var access = RequiredObject(stateRoot, "Access", "access");
        var principalsName = PropertyName(access, "Principals", "principals");
        var principalIds = new JsonArray();
        if (access[principalsName] is JsonArray principals)
        {
            foreach (var principal in principals.OfType<JsonObject>())
            {
                var principalId = principal[PropertyName(principal, "PrincipalId", "principalId")]
                    ?.GetValue<string>();
                if (!string.IsNullOrWhiteSpace(principalId))
                {
                    principalIds.Add(principalId);
                }
            }
        }
        access.Remove(PropertyName(access, "AllowOwnerAccess", "allowOwnerAccess"));
        access.Remove(principalsName);
        access[access.ContainsKey("PolicyId") ? "PrincipalIds" : "principalIds"] = principalIds;

        var snapshot = RequiredObject(stateRoot, "Snapshot", "snapshot");
        foreach (var (pascalName, camelName) in new[]
                 {
                     ("SourceCheckpointId", "sourceCheckpointId"),
                     ("SourceCheckpointHash", "sourceCheckpointHash"),
                     ("ReconciliationCheckpointHash", "reconciliationCheckpointHash"),
                     ("ParametersCanonicalJson", "parametersCanonicalJson"),
                     ("ParametersHash", "parametersHash")
                 })
        {
            snapshot.Remove(PropertyName(snapshot, pascalName, camelName));
        }
        stateRoot.Remove(stateRoot.ContainsKey("RestatementRequestId")
            ? "RestatementRequestId"
            : "restatementRequestId");
        statePayload = stateRoot.ToJsonString();
        var stateHash = ComputeSha256(statePayload);

        var eventRoot = JsonNode.Parse(eventPayload)?.AsObject()
            ?? throw new InvalidOperationException("The reporting audit payload was not a JSON object.");
        var seriesId = (stateRoot["SeriesId"] ?? stateRoot["seriesId"])!.GetValue<string>();
        var revision = (stateRoot["Revision"] ?? stateRoot["revision"])!.GetValue<int>();
        eventRoot[PropertyName(eventRoot, "Note", "note")] =
            $"series={seriesId};revision={revision}";
        var currentEntry = eventRoot.Deserialize<ReportingGovernanceAuditEntry>(new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? throw new InvalidOperationException("The reporting audit payload could not be deserialized.");
        var eventHash = ComputeLegacyAuditHash(currentEntry);
        eventRoot[PropertyName(eventRoot, "Hash", "hash")] = eventHash;
        eventPayload = eventRoot.ToJsonString();
        var eventPayloadHash = ComputeSha256(eventPayload);

        await SetUserTriggersAsync(connection, QualifiedRunsTable, enabled: false).ConfigureAwait(false);
        await SetUserTriggersAsync(connection, QualifiedAuditTable, enabled: false).ConfigureAwait(false);
        try
        {
            await using (var updateState = connection.CreateCommand())
            {
                updateState.CommandText =
                    $"update {QualifiedRunsTable} set state_payload = @payload, state_hash_sha256 = @hash, state_format_version = 1 where tenant_id = @tenant_id and run_id = @run_id;";
                updateState.Parameters.AddWithValue("payload", NpgsqlDbType.Text, statePayload);
                updateState.Parameters.AddWithValue("hash", NpgsqlDbType.Text, stateHash);
                updateState.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Text, tenantId);
                updateState.Parameters.AddWithValue("run_id", NpgsqlDbType.Text, runId);
                await updateState.ExecuteNonQueryAsync().ConfigureAwait(false);
            }
            await using (var updateAudit = connection.CreateCommand())
            {
                updateAudit.CommandText =
                    $"update {QualifiedAuditTable} set event_payload = @payload, payload_hash_sha256 = @payload_hash, event_hash = @event_hash, hash_format_version = 1 where tenant_id = @tenant_id and aggregate_kind = @kind and aggregate_id = @aggregate_id and aggregate_version = 1;";
                updateAudit.Parameters.AddWithValue("payload", NpgsqlDbType.Text, eventPayload);
                updateAudit.Parameters.AddWithValue("payload_hash", NpgsqlDbType.Text, eventPayloadHash);
                updateAudit.Parameters.AddWithValue("event_hash", NpgsqlDbType.Text, eventHash);
                AddAuditIdentity(
                    updateAudit,
                    tenantId,
                    ReportingGovernanceAuditAggregateKind.Run,
                    runId);
                await updateAudit.ExecuteNonQueryAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            await SetUserTriggersAsync(connection, QualifiedAuditTable, enabled: true).ConfigureAwait(false);
            await SetUserTriggersAsync(connection, QualifiedRunsTable, enabled: true).ConfigureAwait(false);
        }
    }

    public async Task RemoveLegacyRunCompanyScopeAsync(string tenantId, string runId)
    {
        await using var connection = await OpenConnectionAsync().ConfigureAwait(false);
        string payload;
        await using (var read = connection.CreateCommand())
        {
            read.CommandText =
                $"select state_payload from {QualifiedRunsTable} where tenant_id = @tenant_id and run_id = @run_id and state_format_version = 1;";
            read.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Text, tenantId);
            read.Parameters.AddWithValue("run_id", NpgsqlDbType.Text, runId);
            payload = (string)(await read.ExecuteScalarAsync().ConfigureAwait(false)
                ?? throw new InvalidOperationException("The legacy reporting run payload was not found."));
        }

        var root = JsonNode.Parse(payload)?.AsObject()
            ?? throw new InvalidOperationException("The legacy reporting run payload was not a JSON object.");
        foreach (var objectName in new[]
                 {
                     ("Scope", "scope"),
                     ("Snapshot", "snapshot"),
                     ("CreationAuthority", "creationAuthority")
                 })
        {
            var scopedObject = RequiredObject(root, objectName.Item1, objectName.Item2);
            scopedObject[PropertyName(scopedObject, "CompanyId", "companyId")] = null;
        }

        payload = root.ToJsonString();
        var hash = ComputeSha256(payload);
        await SetUserTriggersAsync(connection, QualifiedRunsTable, enabled: false).ConfigureAwait(false);
        try
        {
            await using var update = connection.CreateCommand();
            update.CommandText =
                $"update {QualifiedRunsTable} set company_id = null, state_payload = @payload, state_hash_sha256 = @hash where tenant_id = @tenant_id and run_id = @run_id and state_format_version = 1;";
            update.Parameters.AddWithValue("payload", NpgsqlDbType.Text, payload);
            update.Parameters.AddWithValue("hash", NpgsqlDbType.Text, hash);
            update.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Text, tenantId);
            update.Parameters.AddWithValue("run_id", NpgsqlDbType.Text, runId);
            (await update.ExecuteNonQueryAsync().ConfigureAwait(false)).Should().Be(1);
        }
        finally
        {
            await SetUserTriggersAsync(connection, QualifiedRunsTable, enabled: true).ConfigureAwait(false);
        }
    }

    public async Task ReplaceRunCompanyWithNullAndRehashAsync(string tenantId, string runId)
    {
        await using var connection = await OpenConnectionAsync().ConfigureAwait(false);
        string payload;
        await using (var read = connection.CreateCommand())
        {
            read.CommandText =
                $"select state_payload from {QualifiedRunsTable} where tenant_id = @tenant_id and run_id = @run_id;";
            read.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Text, tenantId);
            read.Parameters.AddWithValue("run_id", NpgsqlDbType.Text, runId);
            payload = (string)(await read.ExecuteScalarAsync().ConfigureAwait(false)
                ?? throw new InvalidOperationException("The reporting run payload was not found."));
        }

        var root = JsonNode.Parse(payload)?.AsObject()
            ?? throw new InvalidOperationException("The reporting run payload was not a JSON object.");
        var scope = (root["Scope"] ?? root["scope"])?.AsObject()
            ?? throw new InvalidOperationException("The reporting run payload did not contain scope data.");
        scope[scope.ContainsKey("CompanyId") ? "CompanyId" : "companyId"] = null;
        payload = root.ToJsonString();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)))
            .ToLowerInvariant();

        await SetUserTriggersAsync(connection, QualifiedRunsTable, enabled: false).ConfigureAwait(false);
        try
        {
            await using var update = connection.CreateCommand();
            update.CommandText =
                $"update {QualifiedRunsTable} set state_payload = @payload, state_hash_sha256 = @hash where tenant_id = @tenant_id and run_id = @run_id;";
            update.Parameters.AddWithValue("payload", NpgsqlDbType.Text, payload);
            update.Parameters.AddWithValue("hash", NpgsqlDbType.Text, hash);
            update.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Text, tenantId);
            update.Parameters.AddWithValue("run_id", NpgsqlDbType.Text, runId);
            await update.ExecuteNonQueryAsync().ConfigureAwait(false);
        }
        finally
        {
            await SetUserTriggersAsync(connection, QualifiedRunsTable, enabled: true).ConfigureAwait(false);
        }
    }

    public Task MutateRunPayloadAndRehashAsync(
        string tenantId,
        string runId,
        Action<JsonObject> mutate) =>
        MutateStatePayloadAndRehashAsync(
            QualifiedRunsTable,
            "run_id",
            tenantId,
            runId,
            mutate);

    public Task MutateRestatementPayloadAndRehashAsync(
        string tenantId,
        string requestId,
        Action<JsonObject> mutate) =>
        MutateStatePayloadAndRehashAsync(
            QualifiedRestatementTable,
            "request_id",
            tenantId,
            requestId,
            mutate);

    public async Task MutateFirstAuditPayloadAndRehashAsync(
        string tenantId,
        ReportingGovernanceAuditAggregateKind kind,
        string aggregateId,
        Action<JsonObject> mutate)
    {
        ArgumentNullException.ThrowIfNull(mutate);
        await using var connection = await OpenConnectionAsync().ConfigureAwait(false);
        long aggregateVersion;
        string payload;
        await using (var read = connection.CreateCommand())
        {
            read.CommandText =
                $"select aggregate_version, event_payload from {QualifiedAuditTable} where tenant_id = @tenant_id and aggregate_kind = @kind and aggregate_id = @aggregate_id order by aggregate_version limit 1;";
            AddAuditIdentity(read, tenantId, kind, aggregateId);
            await using var reader = await read.ExecuteReaderAsync().ConfigureAwait(false);
            if (!await reader.ReadAsync().ConfigureAwait(false))
            {
                throw new InvalidOperationException("The reporting governance audit payload was not found.");
            }

            aggregateVersion = reader.GetInt64(0);
            payload = reader.GetString(1);
        }

        var root = JsonNode.Parse(payload)?.AsObject()
            ?? throw new InvalidOperationException("The reporting governance audit payload was not a JSON object.");
        mutate(root);
        payload = root.ToJsonString();
        var payloadHash = ComputeSha256(payload);

        await SetUserTriggersAsync(connection, QualifiedAuditTable, enabled: false).ConfigureAwait(false);
        try
        {
            await using var update = connection.CreateCommand();
            update.CommandText =
                $"update {QualifiedAuditTable} set event_payload = @payload, payload_hash_sha256 = @hash where tenant_id = @tenant_id and aggregate_kind = @kind and aggregate_id = @aggregate_id and aggregate_version = @aggregate_version;";
            update.Parameters.AddWithValue("payload", NpgsqlDbType.Text, payload);
            update.Parameters.AddWithValue("hash", NpgsqlDbType.Text, payloadHash);
            update.Parameters.AddWithValue("aggregate_version", NpgsqlDbType.Bigint, aggregateVersion);
            AddAuditIdentity(update, tenantId, kind, aggregateId);
            if (await update.ExecuteNonQueryAsync().ConfigureAwait(false) != 1)
            {
                throw new InvalidOperationException("The reporting governance audit payload was not updated.");
            }
        }
        finally
        {
            await SetUserTriggersAsync(connection, QualifiedAuditTable, enabled: true).ConfigureAwait(false);
        }
    }

    public async Task ReplaceRunIndexedScopeAsync(
        string tenantId,
        string runId,
        string column,
        string value)
    {
        var safeColumn = column switch
        {
            "organization_id" => "organization_id",
            "company_id" => "company_id",
            _ => throw new ArgumentOutOfRangeException(nameof(column), column, "Unsupported indexed scope column.")
        };

        await using var connection = await OpenConnectionAsync().ConfigureAwait(false);
        await SetUserTriggersAsync(connection, QualifiedRunsTable, enabled: false).ConfigureAwait(false);
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                $"update {QualifiedRunsTable} set {safeColumn} = @value where tenant_id = @tenant_id and run_id = @run_id;";
            command.Parameters.AddWithValue("value", NpgsqlDbType.Text, value);
            command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Text, tenantId);
            command.Parameters.AddWithValue("run_id", NpgsqlDbType.Text, runId);
            await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        }
        finally
        {
            await SetUserTriggersAsync(connection, QualifiedRunsTable, enabled: true).ConfigureAwait(false);
        }
    }

    public async Task RemoveAuditChainAsync(
        string tenantId,
        ReportingGovernanceAuditAggregateKind kind,
        string aggregateId)
    {
        await using var connection = await OpenConnectionAsync().ConfigureAwait(false);
        await SetUserTriggersAsync(connection, QualifiedAuditTable, enabled: false).ConfigureAwait(false);
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                $"delete from {QualifiedAuditTable} where tenant_id = @tenant_id and aggregate_kind = @kind and aggregate_id = @aggregate_id;";
            AddAuditIdentity(command, tenantId, kind, aggregateId);
            await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        }
        finally
        {
            await SetUserTriggersAsync(connection, QualifiedAuditTable, enabled: true).ConfigureAwait(false);
        }
    }

    private async Task<NpgsqlConnection> OpenConnectionAsync()
    {
        var connection = new NpgsqlConnection(Options.ConnectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        return connection;
    }

    private async Task MutateStatePayloadAndRehashAsync(
        string table,
        string idColumn,
        string tenantId,
        string aggregateId,
        Action<JsonObject> mutate)
    {
        ArgumentNullException.ThrowIfNull(mutate);
        await using var connection = await OpenConnectionAsync().ConfigureAwait(false);
        string payload;
        await using (var read = connection.CreateCommand())
        {
            read.CommandText =
                $"select state_payload from {table} where tenant_id = @tenant_id and {idColumn} = @aggregate_id;";
            read.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Text, tenantId);
            read.Parameters.AddWithValue("aggregate_id", NpgsqlDbType.Text, aggregateId);
            payload = (string)(await read.ExecuteScalarAsync().ConfigureAwait(false)
                ?? throw new InvalidOperationException("The reporting governance state payload was not found."));
        }

        var root = JsonNode.Parse(payload)?.AsObject()
            ?? throw new InvalidOperationException("The reporting governance state payload was not a JSON object.");
        mutate(root);
        payload = root.ToJsonString();
        var payloadHash = ComputeSha256(payload);

        await SetUserTriggersAsync(connection, table, enabled: false).ConfigureAwait(false);
        try
        {
            await using var update = connection.CreateCommand();
            update.CommandText =
                $"update {table} set state_payload = @payload, state_hash_sha256 = @hash where tenant_id = @tenant_id and {idColumn} = @aggregate_id;";
            update.Parameters.AddWithValue("payload", NpgsqlDbType.Text, payload);
            update.Parameters.AddWithValue("hash", NpgsqlDbType.Text, payloadHash);
            update.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Text, tenantId);
            update.Parameters.AddWithValue("aggregate_id", NpgsqlDbType.Text, aggregateId);
            if (await update.ExecuteNonQueryAsync().ConfigureAwait(false) != 1)
            {
                throw new InvalidOperationException("The reporting governance state payload was not updated.");
            }
        }
        finally
        {
            await SetUserTriggersAsync(connection, table, enabled: true).ConfigureAwait(false);
        }
    }

    private static string ComputeLegacyAuditHash(ReportingGovernanceAuditEntry entry)
    {
        var canonical = new StringBuilder();
        AppendLegacyCanonical(canonical, entry.EventId);
        AppendLegacyCanonical(canonical, (int)entry.AggregateKind);
        AppendLegacyCanonical(canonical, entry.AggregateId);
        AppendLegacyCanonical(canonical, entry.AggregateVersion);
        AppendLegacyCanonical(
            canonical,
            entry.OccurredAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        AppendLegacyCanonical(canonical, (int)entry.Action);
        AppendLegacyCanonical(canonical, entry.Authority.ActorId);
        AppendLegacyCanonical(canonical, entry.Authority.TenantId);
        AppendLegacyCanonical(canonical, entry.Authority.OrganizationId);
        AppendLegacyCanonical(canonical, entry.Authority.CompanyId);
        foreach (var permission in entry.Authority.Permissions.OrderBy(static permission => permission))
        {
            AppendLegacyCanonical(canonical, (int)permission);
        }
        AppendLegacyCanonical(canonical, (int)entry.Authority.Origin);
        AppendLegacyCanonical(canonical, entry.Authority.CorrelationId);
        AppendLegacyCanonical(canonical, (int)entry.PermissionUsed);
        AppendLegacyCanonical(canonical, entry.FromExecutionState is null ? null : (int)entry.FromExecutionState.Value);
        AppendLegacyCanonical(canonical, entry.ToExecutionState is null ? null : (int)entry.ToExecutionState.Value);
        AppendLegacyCanonical(canonical, entry.FromGovernanceState is null ? null : (int)entry.FromGovernanceState.Value);
        AppendLegacyCanonical(canonical, entry.ToGovernanceState is null ? null : (int)entry.ToGovernanceState.Value);
        AppendLegacyCanonical(canonical, entry.FromRestatementState is null ? null : (int)entry.FromRestatementState.Value);
        AppendLegacyCanonical(canonical, entry.ToRestatementState is null ? null : (int)entry.ToRestatementState.Value);
        AppendLegacyCanonical(canonical, entry.Note);
        AppendLegacyCanonical(canonical, entry.PreviousHash);
        return ComputeSha256(canonical.ToString());
    }

    private static void AppendLegacyCanonical(StringBuilder target, object? value)
    {
        var text = value switch
        {
            null => null,
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString()
        };
        if (text is null)
        {
            target.Append("-1:");
            return;
        }
        target.Append(Encoding.UTF8.GetByteCount(text).ToString(CultureInfo.InvariantCulture));
        target.Append(':');
        target.Append(text);
    }

    private static string ComputeSha256(string payload) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)))
            .ToLowerInvariant();

    private static async Task SetUserTriggersAsync(
        NpgsqlConnection connection,
        string table,
        bool enabled)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"alter table {table} {(enabled ? "enable" : "disable")} trigger user;";
        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    private static void AddAuditIdentity(
        NpgsqlCommand command,
        string tenantId,
        ReportingGovernanceAuditAggregateKind kind,
        string aggregateId)
    {
        command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Text, tenantId);
        command.Parameters.AddWithValue("kind", NpgsqlDbType.Smallint, (short)kind);
        command.Parameters.AddWithValue("aggregate_id", NpgsqlDbType.Text, aggregateId);
    }
}

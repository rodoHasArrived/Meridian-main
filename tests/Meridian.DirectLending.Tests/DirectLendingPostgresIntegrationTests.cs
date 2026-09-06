using FluentAssertions;
using Meridian.Contracts.DirectLending;
using Meridian.Storage.DirectLending;
using Npgsql;

namespace Meridian.DirectLending.Tests;

[Trait("Category", "Integration")]
public sealed class DirectLendingPostgresIntegrationTests
{
    private const string WorkflowIdPrefix = "wf-";

    [DirectLendingDatabaseFact]
    public async Task CashFlowIdentityMigration_PreservesRetainedFlowsAndReconciliationReferences()
    {
        await using var db = await DirectLendingPostgresTestDatabase.CreateOrSkipAsync();
        if (db is null)
            return;
        var loan = await db.Service.CreateLoanAsync(BuildCreateRequest());
        await db.Service.ActivateLoanAsync(loan.LoanId, new ActivateLoanRequest(new DateOnly(2026, 3, 22)));
        await db.Service.BookDrawdownAsync(loan.LoanId, new BookDrawdownRequest(250_000m,
            new DateOnly(2026, 3, 22), new DateOnly(2026, 3, 22), "migration-retention"));
        var projection = await db.Service.RequestProjectionAsync(loan.LoanId, new DateOnly(2026, 6, 30));
        var reconciliation = await db.Service.ReconcileAsync(loan.LoanId);
        var flows = await db.Store.GetProjectedCashFlowsAsync(projection.ProjectionRunId);
        var results = await db.Store.GetReconciliationResultsAsync(reconciliation!.ReconciliationRunId);
        flows.Should().NotBeEmpty();
        results.Should().Contain(result => result.ProjectedCashFlowId != null);
        await using var connection = new NpgsqlConnection(db.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"drop trigger synchronize_cash_flow_identity on {db.Schema}.projected_cash_flow; drop trigger synchronize_cash_flow_identity on {db.Schema}.reconciliation_result; alter table {db.Schema}.projected_cash_flow drop column projected_cash_flow_id; alter table {db.Schema}.reconciliation_result drop column projected_cash_flow_id;";
        await command.ExecuteNonQueryAsync();
        var migration = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory,
            "DirectLending", "Migrations", "009_direct_lending_cash_flow_identity.sql"));
        command.CommandText = migration.Replace("__SCHEMA__", db.Schema, StringComparison.Ordinal);
        await command.ExecuteNonQueryAsync();
        await command.ExecuteNonQueryAsync();
        (await db.Store.GetProjectedCashFlowsAsync(projection.ProjectionRunId)).Should().BeEquivalentTo(flows);
        (await db.Store.GetReconciliationResultsAsync(reconciliation.ReconciliationRunId)).Should().BeEquivalentTo(results);
    }

    [DirectLendingDatabaseFact]
    public async Task ProjectionRetry_ConcurrentCommandsRetainOneRunAndOriginalFlowIdentities()
    {
        await using var db = await DirectLendingPostgresTestDatabase.CreateOrSkipAsync();
        if (db is null)
            return;
        var loan = await db.Service.CreateLoanAsync(BuildCreateRequest());
        await db.Service.ActivateLoanAsync(loan.LoanId, new ActivateLoanRequest(new DateOnly(2026, 3, 22)));
        await db.Service.BookDrawdownAsync(loan.LoanId, new BookDrawdownRequest(250_000m,
            new DateOnly(2026, 3, 22), new DateOnly(2026, 3, 22), "retry-flow"));
        var metadata = new DirectLendingCommandMetadataDto(Guid.NewGuid(), null, null, "integration-test", true);
        var asOf = new DateOnly(2026, 6, 30);
        var results = await Task.WhenAll(Enumerable.Range(0, 8)
            .Select(_ => db.CommandService.RequestProjectionAsync(loan.LoanId, asOf, metadata)));
        results.Should().OnlyContain(result => result.IsSuccess);
        results.Select(result => result.Value!.ProjectionRunId).Distinct().Should().ContainSingle();
        var retained = (await db.Store.GetProjectionRunsAsync(loan.LoanId)).Should().ContainSingle().Subject;
        var flows = await db.Store.GetProjectedCashFlowsAsync(retained.ProjectionRunId);
        flows.Should().NotBeEmpty();
        var replay = await db.CommandService.RequestProjectionAsync(loan.LoanId, asOf, metadata);
        replay.Value!.Should().Be(retained);
        (await db.Store.GetProjectedCashFlowsAsync(retained.ProjectionRunId))
            .Select(flow => flow.ProjectedCashFlowId).Should().Equal(flows.Select(flow => flow.ProjectedCashFlowId));
        var conflict = await db.CommandService.RequestProjectionAsync(loan.LoanId, asOf.AddDays(1), metadata);
        conflict.Error!.Code.Should().Be(DirectLendingErrorCode.Conflict);
        (await db.Store.GetProjectionRunsAsync(loan.LoanId)).Should().ContainSingle();
    }

    [DirectLendingDatabaseFact]
    public async Task ReconciliationRetry_RetainsCommittedResultsUnderConcurrentDelivery()
    {
        await using var db = await DirectLendingPostgresTestDatabase.CreateOrSkipAsync();
        if (db is null)
            return;
        var loan = await db.Service.CreateLoanAsync(BuildCreateRequest());
        await db.Service.ActivateLoanAsync(loan.LoanId, new ActivateLoanRequest(new DateOnly(2026, 3, 22)));
        await db.Service.BookDrawdownAsync(loan.LoanId, new BookDrawdownRequest(250_000m,
            new DateOnly(2026, 3, 22), new DateOnly(2026, 3, 22), "retry-reconciliation"));
        await db.CommandService.RequestProjectionAsync(loan.LoanId, new DateOnly(2026, 6, 30));
        var metadata = new DirectLendingCommandMetadataDto(Guid.NewGuid(), null, null, "integration-test", true);
        var deliveries = await Task.WhenAll(Enumerable.Range(0, 8)
            .Select(_ => db.CommandService.ReconcileAsync(loan.LoanId, metadata)));
        deliveries.Should().OnlyContain(result => result.IsSuccess);
        deliveries.Select(result => result.Value!.ReconciliationRunId).Distinct().Should().ContainSingle();
        var retained = (await db.Store.GetReconciliationRunsAsync(loan.LoanId)).Should().ContainSingle().Subject;
        var details = await db.Store.GetReconciliationResultsAsync(retained.ReconciliationRunId);
        details.Should().NotBeEmpty();
        var replay = await db.CommandService.ReconcileAsync(loan.LoanId, metadata);
        replay.Value!.Should().Be(retained);
        (await db.Store.GetReconciliationResultsAsync(retained.ReconciliationRunId))
            .Select(result => result.ReconciliationResultId).Should().Equal(details.Select(result => result.ReconciliationResultId));
    }

    [DirectLendingDatabaseFact]
    public async Task PostgresService_ShouldPersistSchemaVersionedHistoryAndSnapshots()
    {
        await using var db = await DirectLendingPostgresTestDatabase.CreateOrSkipAsync();
        if (db is null)
        {
            return;
        }

        var created = await db.Service.CreateLoanAsync(BuildCreateRequest());
        await db.Service.ActivateLoanAsync(created.LoanId, new ActivateLoanRequest(new DateOnly(2026, 3, 22)));
        await db.Service.BookDrawdownAsync(created.LoanId, new BookDrawdownRequest(250_000m, new DateOnly(2026, 3, 22), new DateOnly(2026, 3, 24), "wire-1"));

        var history = await db.Service.GetHistoryAsync(created.LoanId);
        var snapshotCount = await db.CountSnapshotsAsync(created.LoanId);
        var servicing = await db.Service.GetServicingProjectionAsync(created.LoanId);

        history.Should().HaveCount(3);
        history.Should().OnlyContain(static item => item.EventSchemaVersion == 1);
        snapshotCount.Should().BeGreaterThanOrEqualTo(2);
        servicing.Should().NotBeNull();
        servicing!.Balances.PrincipalOutstanding.Should().Be(250_000m);
    }

    [DirectLendingDatabaseFact]
    public async Task QueryService_ShouldRebuildFromHistory_WhenLiveStateRowIsMissing()
    {
        await using var db = await DirectLendingPostgresTestDatabase.CreateOrSkipAsync();
        if (db is null)
        {
            return;
        }

        var created = await db.Service.CreateLoanAsync(BuildCreateRequest());
        await db.Service.ActivateLoanAsync(created.LoanId, new ActivateLoanRequest(new DateOnly(2026, 3, 22)));
        await db.Service.BookDrawdownAsync(created.LoanId, new BookDrawdownRequest(150_000m, new DateOnly(2026, 3, 22), new DateOnly(2026, 3, 22), "wire-2"));
        await db.DeleteLiveStateAsync(created.LoanId);

        var rebuilt = await db.QueryService.LoadAggregateAsync(created.LoanId);

        rebuilt.Should().NotBeNull();
        rebuilt!.AggregateVersion.Should().Be(3);
        rebuilt.Servicing.Balances.PrincipalOutstanding.Should().Be(150_000m);
        rebuilt.Servicing.DrawdownLots.Should().ContainSingle();
    }

    [DirectLendingDatabaseFact]
    public async Task CommandService_ShouldTreatDuplicateCommandIdAsIdempotent()
    {
        await using var db = await DirectLendingPostgresTestDatabase.CreateOrSkipAsync();
        if (db is null)
        {
            return;
        }

        var created = await db.Service.CreateLoanAsync(BuildCreateRequest());
        await db.Service.ActivateLoanAsync(created.LoanId, new ActivateLoanRequest(new DateOnly(2026, 3, 22)));
        await db.Service.BookDrawdownAsync(
            created.LoanId,
            new BookDrawdownRequest(100_000m, new DateOnly(2026, 3, 22), new DateOnly(2026, 3, 24), "wire-idempotency-seed"));

        var commandId = Guid.NewGuid();
        var metadata = new DirectLendingCommandMetadataDto(
            CausationId: Guid.NewGuid(),
            CorrelationId: Guid.NewGuid(),
            CommandId: commandId,
            SourceSystem: "integration-tests",
            ReplayFlag: false);
        var request = new ApplyPrincipalPaymentRequest(50_000m, new DateOnly(2026, 4, 1), "wire-dup-1");

        var first = await db.CommandService.ApplyPrincipalPaymentAsync(created.LoanId, request, metadata);
        var second = await db.CommandService.ApplyPrincipalPaymentAsync(created.LoanId, request, metadata);

        first.Error.Should().BeNull();
        second.Error.Should().BeNull();

        var history = await db.Service.GetHistoryAsync(created.LoanId);
        history.Count(item => item.EventType == "loan.principal-payment-applied").Should().Be(1);
    }


    [DirectLendingDatabaseFact]
    public async Task CommandService_ShouldRejectDuplicateCommandId_WhenMutationDiffers()
    {
        await using var db = await DirectLendingPostgresTestDatabase.CreateOrSkipAsync();
        if (db is null)
        {
            return;
        }

        var created = await db.Service.CreateLoanAsync(BuildCreateRequest());
        await db.Service.ActivateLoanAsync(created.LoanId, new ActivateLoanRequest(new DateOnly(2026, 3, 22)));
        await db.Service.BookDrawdownAsync(
            created.LoanId,
            new BookDrawdownRequest(100_000m, new DateOnly(2026, 3, 22), new DateOnly(2026, 3, 24), "wire-conflict-seed"));

        var commandId = Guid.NewGuid();
        var metadata = new DirectLendingCommandMetadataDto(
            CausationId: Guid.NewGuid(),
            CorrelationId: Guid.NewGuid(),
            CommandId: commandId,
            SourceSystem: "integration-tests",
            ReplayFlag: false);

        var first = await db.CommandService.ApplyPrincipalPaymentAsync(
            created.LoanId,
            new ApplyPrincipalPaymentRequest(50_000m, new DateOnly(2026, 4, 1), "wire-dup-2"),
            metadata);
        Func<Task> act = () => db.CommandService.ApplyPrincipalPaymentAsync(
            created.LoanId,
            new ApplyPrincipalPaymentRequest(10_000m, new DateOnly(2026, 4, 2), "wire-dup-3"),
            metadata);

        first.Error.Should().BeNull();
        var exception = await act.Should().ThrowAsync<DirectLendingCommandException>();
        exception.Which.Error.Code.Should().Be(DirectLendingErrorCode.Conflict);

        var history = await db.Service.GetHistoryAsync(created.LoanId);
        history.Count(item => item.EventType == "loan.principal-payment-applied").Should().Be(1);
    }

    [DirectLendingDatabaseFact]
    public async Task AppendOperationsWorkflowAuditAsync_ShouldReturnLinearHashChainedStream()
    {
        await using var db = await DirectLendingPostgresTestDatabase.CreateOrSkipAsync();
        if (db is null)
        {
            return;
        }

        var workflowId = $"{WorkflowIdPrefix}{Guid.NewGuid():N}";
        var fundAccountId = Guid.NewGuid();
        var periodId = "2026-Q2";

        await db.Store.AppendOperationsWorkflowAuditAsync(
            BuildAuditAppendRequest(
                workflowId,
                fundAccountId,
                periodId,
                eventType: "state_transition",
                fromState: "draft",
                toState: "ready"));

        await db.Store.AppendOperationsWorkflowAuditAsync(
            BuildAuditAppendRequest(
                workflowId,
                fundAccountId,
                periodId,
                eventType: "gate_change",
                gate: "readiness",
                fromGateStatus: "pending",
                toGateStatus: "blocked"));

        await db.Store.AppendOperationsWorkflowAuditAsync(
            BuildAuditAppendRequest(
                workflowId,
                fundAccountId,
                periodId,
                eventType: "approval_action",
                fromState: "ready",
                toState: "approved"));

        var stream = await db.Store.GetOperationsWorkflowAuditAsync(workflowId);

        stream.Should().HaveCount(3);
        stream[0].PreviousHash.Should().BeNull();
        stream[0].Hash.Should().NotBeNullOrWhiteSpace();
        stream[1].PreviousHash.Should().Be(stream[0].Hash);
        stream[2].PreviousHash.Should().Be(stream[1].Hash);
        stream.Select(static entry => entry.Hash).Should().OnlyHaveUniqueItems();
        stream.Should().OnlyContain(static entry => IsValidSha256HexHash(entry.Hash));
    }

    [DirectLendingDatabaseFact]
    public async Task AppendOperationsWorkflowAuditAsync_ShouldSerializeConcurrentAppendsPerWorkflow()
    {
        await using var db = await DirectLendingPostgresTestDatabase.CreateOrSkipAsync();
        if (db is null)
        {
            return;
        }

        var workflowId = $"{WorkflowIdPrefix}{Guid.NewGuid():N}";
        var fundAccountId = Guid.NewGuid();
        var periodId = "2026-Q2";
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var requests = new[]
        {
            BuildAuditAppendRequest(workflowId, fundAccountId, periodId, eventType: "state_transition", fromState: "draft", toState: "ready"),
            BuildAuditAppendRequest(workflowId, fundAccountId, periodId, eventType: "gate_change", gate: "readiness", fromGateStatus: "pending", toGateStatus: "blocked"),
            BuildAuditAppendRequest(workflowId, fundAccountId, periodId, eventType: "approval_action", fromState: "ready", toState: "approved"),
            BuildAuditAppendRequest(workflowId, fundAccountId, periodId, eventType: "break_opened", gate: "reconciliation", fromGateStatus: "ready", toGateStatus: "review")
        };

        var appendTasks = requests
            .Select(request => Task.Run(async () =>
            {
                await start.Task.ConfigureAwait(false);
                return await db.Store.AppendOperationsWorkflowAuditAsync(request).ConfigureAwait(false);
            }))
            .ToArray();

        start.SetResult();
        await Task.WhenAll(appendTasks);

        var stream = await db.Store.GetOperationsWorkflowAuditAsync(workflowId);

        stream.Should().HaveCount(requests.Length);
        stream.Count(static entry => entry.PreviousHash is null).Should().Be(1);
        stream.Select(static entry => entry.Hash).Should().OnlyHaveUniqueItems();
        stream.Should().OnlyContain(static entry => IsValidSha256HexHash(entry.Hash));

        for (var index = 1; index < stream.Count; index++)
        {
            stream[index].PreviousHash.Should().Be(stream[index - 1].Hash);
        }
    }

    [DirectLendingDatabaseFact]
    public async Task AppendOperationsWorkflowAuditAsync_ShouldFollowHashChainWhenStorageTimestampsAreOutOfOrder()
    {
        await using var db = await DirectLendingPostgresTestDatabase.CreateOrSkipAsync();
        if (db is null)
        {
            return;
        }

        var workflowId = $"{WorkflowIdPrefix}{Guid.NewGuid():N}";
        var fundAccountId = Guid.NewGuid();
        const string periodId = "2026-Q2";

        var first = await db.Store.AppendOperationsWorkflowAuditAsync(
            BuildAuditAppendRequest(
                workflowId,
                fundAccountId,
                periodId,
                eventType: "state_transition",
                fromState: "draft",
                toState: "ready"));
        var second = await db.Store.AppendOperationsWorkflowAuditAsync(
            BuildAuditAppendRequest(
                workflowId,
                fundAccountId,
                periodId,
                eventType: "gate_change",
                gate: "readiness",
                fromGateStatus: "pending",
                toGateStatus: "ready"));

        await SetWorkflowAuditCreatedAtAsync(db, first.AuditId, new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero));
        await SetWorkflowAuditCreatedAtAsync(db, second.AuditId, new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero));

        var third = await db.Store.AppendOperationsWorkflowAuditAsync(
            BuildAuditAppendRequest(
                workflowId,
                fundAccountId,
                periodId,
                eventType: "approval_action",
                fromState: "ready",
                toState: "approved"));
        var stream = await db.Store.GetOperationsWorkflowAuditAsync(workflowId);

        third.PreviousHash.Should().Be(second.Hash);
        stream.Select(static entry => entry.AuditId).Should().Equal(first.AuditId, second.AuditId, third.AuditId);
        stream[1].PreviousHash.Should().Be(stream[0].Hash);
        stream[2].PreviousHash.Should().Be(stream[1].Hash);
    }

    [DirectLendingDatabaseFact]
    public async Task AppendOperationsWorkflowAuditAsync_ShouldRejectUnsupportedEventType()
    {
        await using var db = await DirectLendingPostgresTestDatabase.CreateOrSkipAsync();
        if (db is null)
        {
            return;
        }

        var act = async () =>
            await db.Store.AppendOperationsWorkflowAuditAsync(
                BuildAuditAppendRequest(
                    workflowId: $"{WorkflowIdPrefix}{Guid.NewGuid():N}",
                    fundAccountId: Guid.NewGuid(),
                    periodId: "2026-Q2",
                    eventType: "unknown_event"));

        var exception = await act.Should().ThrowAsync<PostgresException>();
        exception.Which.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
    }

    private static async Task SetWorkflowAuditCreatedAtAsync(
        DirectLendingPostgresTestDatabase db,
        Guid auditId,
        DateTimeOffset createdAt)
    {
        await using var connection = new NpgsqlConnection(db.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"update {db.Schema}.operations_workflow_audit set created_at = @created_at where audit_id = @audit_id;";
        command.Parameters.AddWithValue("created_at", createdAt.UtcDateTime);
        command.Parameters.AddWithValue("audit_id", auditId);

        var affected = await command.ExecuteNonQueryAsync();
        affected.Should().Be(1);
    }

    private static CreateLoanRequest BuildCreateRequest() =>
        new(
            LoanId: Guid.NewGuid(),
            FacilityName: "Fabrikam Senior Secured Loan",
            Borrower: new BorrowerInfoDto(Guid.NewGuid(), "Fabrikam Borrower", Guid.NewGuid()),
            EffectiveDate: new DateOnly(2026, 3, 22),
            Terms: new DirectLendingTermsDto(
                OriginationDate: new DateOnly(2026, 3, 22),
                MaturityDate: new DateOnly(2029, 3, 22),
                CommitmentAmount: 1_000_000m,
                BaseCurrency: CurrencyCode.USD,
                RateTypeKind: RateTypeKind.Fixed,
                FixedAnnualRate: 0.08m,
                InterestIndexName: null,
                SpreadBps: null,
                FloorRate: null,
                CapRate: null,
                DayCountBasis: DayCountBasis.Act360,
                PaymentFrequency: PaymentFrequency.Quarterly,
                AmortizationType: AmortizationType.InterestOnly,
                CommitmentFeeRate: 0.03m,
                DefaultRateSpreadBps: 200m,
                PrepaymentAllowed: true,
                CovenantsJson: "{\"leverage\": \"<= 4.5x\"}",
                SecurityMasterReference: new DirectLendingSecurityMasterReferenceDto(
                    DirectLendingPostgresTestDatabase.TestSecurityId,
                    DirectLendingPostgresTestDatabase.TestSecuritySymbol,
                    "integration-test-security-master",
                    "integration-test-approval",
                    "integration-test-ledger-map")));

    private static OperationsWorkflowAuditAppendRequest BuildAuditAppendRequest(
        string workflowId,
        Guid fundAccountId,
        string periodId,
        string eventType,
        string? fromState = null,
        string? toState = null,
        string? gate = null,
        string? fromGateStatus = null,
        string? toGateStatus = null) =>
        new(
            AuditId: Guid.NewGuid(),
            OccurredAtUtc: DateTimeOffset.UtcNow,
            WorkflowId: workflowId,
            FundAccountId: fundAccountId,
            PeriodId: periodId,
            EventType: eventType,
            FromState: fromState,
            ToState: toState,
            Gate: gate,
            FromGateStatus: fromGateStatus,
            ToGateStatus: toGateStatus,
            Actor: "integration-test",
            Rationale: "workflow audit verification",
            TraceId: null,
            RequestId: null,
            SessionId: null,
            RunId: null,
            BrokerReferenceId: null,
            SecurityReferenceId: null,
            LedgerReferenceId: null,
            ReconciliationReferenceId: null,
            EvidenceReferenceId: null,
            AuditReferenceId: null,
            Severity: "info",
            Tags: ["integration", "audit"]);

    private static bool IsValidSha256HexHash(string hash) =>
        hash.Length == 64 &&
        hash.All(static character =>
            (character >= '0' && character <= '9') || (character >= 'a' && character <= 'f'));
}

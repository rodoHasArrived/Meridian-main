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
                CovenantsJson: "{\"leverage\": \"<= 4.5x\"}"));

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

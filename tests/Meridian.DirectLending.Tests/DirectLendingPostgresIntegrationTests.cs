using FluentAssertions;
using Meridian.Contracts.DirectLending;
using Meridian.Storage.DirectLending;
using Npgsql;

namespace Meridian.DirectLending.Tests;

[Trait("Category", "Integration")]
public sealed class DirectLendingPostgresIntegrationTests
{
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
    public async Task AppendOperationsWorkflowAuditAsync_ShouldReturnLinearHashChainedStream()
    {
        await using var db = await DirectLendingPostgresTestDatabase.CreateOrSkipAsync();
        if (db is null)
        {
            return;
        }

        var workflowId = $"wf-{Guid.NewGuid():N}";
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
                    workflowId: $"wf-{Guid.NewGuid():N}",
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
}

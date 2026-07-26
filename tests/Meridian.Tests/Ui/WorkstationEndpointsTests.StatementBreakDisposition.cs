using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Meridian.Contracts.Api;
using Meridian.Contracts.Workstation;
using Meridian.Identity.Auth;
using Meridian.Ui.Shared.Contracts.Reconciliation;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Tests.Ui;

public sealed partial class WorkstationEndpointsTests
{
    [Fact]
    public async Task MapWorkstationEndpoints_StatementBreakDisposition_ShouldUseAuthenticatedActorAndIgnoreSpoofedActor()
    {
        var service = new StatementBreakDispositionEndpointStub();
        await using var app = await CreateAppAsync(services =>
            services.AddSingleton<IReconciliationApiService>(service));
        var client = app.GetTestClient();

        var response = await client.PostAsJsonAsync(
            StatementBreakDispositionRoute("break-42"),
            new
            {
                expectedVersion = 7L,
                commandId = "cmd-resolve-break-42",
                disposition = ReconciliationBreakDispositionDto.Resolved,
                rationale = "Custodian confirmed the corrected settlement amount.",
                evidenceLinks = new[] { "evidence://custodian/case-42" },
                actor = "browser-spoof"
            },
            ServerJsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        service.DispositionCallCount.Should().Be(1);
        service.CapturedBreakId.Should().Be("break-42");
        service.CapturedActor.Should().Be("ops-user");
        service.CapturedRequest.Should().NotBeNull();
        service.CapturedRequest!.ExpectedVersion.Should().Be(7);
        service.CapturedRequest.CommandId.Should().Be("cmd-resolve-break-42");
        service.CapturedRequest.Rationale.Should().Be("Custodian confirmed the corrected settlement amount.");
    }

    [Fact]
    public async Task MapWorkstationEndpoints_StatementBreakDisposition_ShouldRejectUnauthenticatedRequest()
    {
        var service = new StatementBreakDispositionEndpointStub();
        await using var app = await CreateAppAsync(
            services => services.AddSingleton<IReconciliationApiService>(service),
            currentUserName: string.Empty);
        var client = app.GetTestClient();

        var response = await client.PostAsJsonAsync(
            StatementBreakDispositionRoute("break-42"),
            BuildDispositionRequest(),
            ServerJsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        service.DispositionCallCount.Should().Be(0);
    }

    [Fact]
    public async Task MapWorkstationEndpoints_StatementBreakDisposition_ShouldRejectActorWithoutMutationPermission()
    {
        var service = new StatementBreakDispositionEndpointStub();
        await using var app = await CreateAppAsync(
            services => services.AddSingleton<IReconciliationApiService>(service),
            currentUserPermissions: UserPermission.ViewSecurityMaster);
        var client = app.GetTestClient();

        var response = await client.PostAsJsonAsync(
            StatementBreakDispositionRoute("break-42"),
            BuildDispositionRequest(),
            ServerJsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        service.DispositionCallCount.Should().Be(0);
    }

    [Theory]
    [InlineData(StatementBreakDispositionOutcomeDto.VersionConflict, HttpStatusCode.Conflict)]
    [InlineData(StatementBreakDispositionOutcomeDto.RecoveryPending, HttpStatusCode.ServiceUnavailable)]
    public async Task MapWorkstationEndpoints_StatementBreakDisposition_ShouldMapNonSuccessOutcome(
        StatementBreakDispositionOutcomeDto outcome,
        HttpStatusCode expectedStatusCode)
    {
        var service = new StatementBreakDispositionEndpointStub
        {
            DispositionResponse = BuildDispositionResult(outcome)
        };
        await using var app = await CreateAppAsync(services =>
            services.AddSingleton<IReconciliationApiService>(service));
        var client = app.GetTestClient();

        var response = await client.PostAsJsonAsync(
            StatementBreakDispositionRoute("break-42"),
            BuildDispositionRequest(),
            ServerJsonOptions);

        response.StatusCode.Should().Be(expectedStatusCode);
        var result = await response.Content.ReadFromJsonAsync<StatementBreakDispositionResultDto>(ServerJsonOptions);
        result.Should().NotBeNull();
        result!.Outcome.Should().Be(outcome);
    }

    [Fact]
    public async Task MapWorkstationEndpoints_StatementBreakAudit_ShouldReturnRetainedHistory()
    {
        var occurredAt = new DateTimeOffset(2026, 7, 21, 16, 30, 0, TimeSpan.Zero);
        var service = new StatementBreakDispositionEndpointStub
        {
            AuditHistory =
            [
                new StatementBreakDispositionAuditEntryDto(
                    AuditId: "audit-42",
                    Sequence: 12,
                    TransactionId: "txn-42",
                    CommandId: "cmd-resolve-break-42",
                    BreakId: "break-42",
                    CaseId: "case:break-42",
                    Version: 8,
                    Disposition: ReconciliationBreakDispositionDto.Resolved,
                    Actor: "ops-user",
                    Rationale: "Custodian confirmed the corrected settlement amount.",
                    EvidenceLinks: ["evidence://custodian/case-42"],
                    OccurredAtUtc: occurredAt,
                    PreviousHash: "previous-hash",
                    EntryHash: "entry-hash")
            ]
        };
        await using var app = await CreateAppAsync(services =>
            services.AddSingleton<IReconciliationApiService>(service));
        var client = app.GetTestClient();

        var response = await client.GetAsync(StatementBreakAuditRoute("break-42"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        service.AuditBreakIds.Should().Equal("break-42");
        var history = await response.Content.ReadFromJsonAsync<List<StatementBreakDispositionAuditEntryDto>>(
            ServerJsonOptions);
        history.Should().ContainSingle();
        history![0].Should().BeEquivalentTo(service.AuditHistory![0]);
    }

    private static StatementBreakDispositionRequestDto BuildDispositionRequest()
        => new(
            ExpectedVersion: 7,
            CommandId: "cmd-resolve-break-42",
            Disposition: ReconciliationBreakDispositionDto.Resolved,
            Rationale: "Custodian confirmed the corrected settlement amount.",
            EvidenceLinks: ["evidence://custodian/case-42"]);

    private static StatementBreakDispositionResultDto BuildDispositionResult(
        StatementBreakDispositionOutcomeDto outcome,
        string actor = "ops-user")
        => new(
            Outcome: outcome,
            BreakId: "break-42",
            CaseId: "case:break-42",
            TransactionId: "txn-42",
            CommandId: "cmd-resolve-break-42",
            Version: 8,
            Disposition: ReconciliationBreakDispositionDto.Resolved,
            Actor: actor,
            Rationale: "Custodian confirmed the corrected settlement amount.",
            EvidenceLinks: ["evidence://custodian/case-42"],
            DisposedAtUtc: new DateTimeOffset(2026, 7, 21, 16, 30, 0, TimeSpan.Zero),
            Break: null,
            Case: null,
            AuditHistory: [],
            Error: outcome is StatementBreakDispositionOutcomeDto.Applied ? null : "Disposition was not committed.");

    private static string StatementBreakDispositionRoute(string breakId)
        => UiApiRoutes.WithParam(
            UiApiRoutes.ReconciliationStatementBreakDisposition,
            "breakId",
            breakId);

    private static string StatementBreakAuditRoute(string breakId)
        => UiApiRoutes.WithParam(
            UiApiRoutes.ReconciliationStatementBreakAudit,
            "breakId",
            breakId);

    private sealed class StatementBreakDispositionEndpointStub : IReconciliationApiService
    {
        public StatementBreakDispositionResultDto? DispositionResponse { get; init; }
        public IReadOnlyList<StatementBreakDispositionAuditEntryDto>? AuditHistory { get; init; } = [];
        public int DispositionCallCount { get; private set; }
        public string? CapturedBreakId { get; private set; }
        public StatementBreakDispositionRequestDto? CapturedRequest { get; private set; }
        public string? CapturedActor { get; private set; }
        public List<string> AuditBreakIds { get; } = [];

        public Task<IReadOnlyList<StatementImportSummaryDto>> ListImportsAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<StatementImportSummaryDto>>([]);

        public Task<IReadOnlyList<StatementRunSummaryDto>> ListStatementRunsAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<StatementRunSummaryDto>>([]);

        public Task<StatementRunDto?> CreateStatementRunAsync(
            StatementRunCreateDto request,
            CancellationToken ct = default)
            => Task.FromResult<StatementRunDto?>(null);

        public Task<StatementRunDto?> GetStatementRunAsync(string runId, CancellationToken ct = default)
            => Task.FromResult<StatementRunDto?>(null);

        public Task<StatementRunValidationDto?> GetStatementRunValidationAsync(
            string runId,
            CancellationToken ct = default)
            => Task.FromResult<StatementRunValidationDto?>(null);

        public Task<IReadOnlyList<StatementRunBreakDto>?> ListStatementRunBreaksAsync(
            string runId,
            CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<StatementRunBreakDto>?>(null);

        public Task<StatementRunDto?> ReconcileStatementRunAsync(
            string runId,
            StatementRunReconcileRequestDto request,
            CancellationToken ct = default)
            => Task.FromResult<StatementRunDto?>(null);

        public Task<IReadOnlyList<StatementRunExceptionDto>> ListOpenExceptionsAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<StatementRunExceptionDto>>([]);

        public Task<StatementBreakDispositionResultDto> DispositionStatementBreakAsync(
            string breakId,
            StatementBreakDispositionRequestDto request,
            string authenticatedActor,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            DispositionCallCount++;
            CapturedBreakId = breakId;
            CapturedRequest = request;
            CapturedActor = authenticatedActor;
            return Task.FromResult(DispositionResponse ?? BuildDispositionResult(
                StatementBreakDispositionOutcomeDto.Applied,
                authenticatedActor));
        }

        public Task<IReadOnlyList<StatementBreakDispositionAuditEntryDto>?> GetStatementBreakAuditHistoryAsync(
            string breakId,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            AuditBreakIds.Add(breakId);
            return Task.FromResult(AuditHistory);
        }

        public Task<IReadOnlyList<ReconciliationCaseSummaryDto>> ListOpenCasesAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ReconciliationCaseSummaryDto>>([]);

        public Task<IReadOnlyList<ReconciliationQueueAccountStatusDto>> ListQueueStatusAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ReconciliationQueueAccountStatusDto>>([]);
    }
}

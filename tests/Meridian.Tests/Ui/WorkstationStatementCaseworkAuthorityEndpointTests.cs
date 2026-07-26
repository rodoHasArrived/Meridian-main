using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Api;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.OperationsContinuity;
using Meridian.Strategies.Services;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Tests.Ui;

public sealed partial class WorkstationEndpointsTests
{
    [Fact]
    public async Task ResolveReconciliationBreak_StatementSource_ShouldUseAuthoritativeCaseworkHandoff()
    {
        var item = BuildStatementQueueItem("statement-authority-route");
        var repository = new RecordingStatementQueueRepository(item);
        var handoff = new RecordingStatementCaseworkHandoffService(item);
        await using var app = await CreateAppAsync(services =>
        {
            services.AddSingleton<IReconciliationBreakQueueRepository>(repository);
            services.AddSingleton<IStatementReconciliationCaseworkHandoffService>(handoff);
        });

        var response = await app.GetTestClient().PostAsJsonAsync(
            UiApiRoutes.ReconciliationBreakResolve.Replace("{breakId}", item.BreakId, StringComparison.Ordinal),
            new ResolveReconciliationBreakRequest(
                item.BreakId,
                ReconciliationBreakQueueStatus.Resolved,
                ResolvedBy: "browser-supplied-actor",
                ResolutionNote: "Statement evidence reconciled.",
                OperatorRationale: "Reviewed retained statement and ledger evidence.",
                ActionOrigin: OperationsActionOriginDto.AutomationSuggestion));

        response.StatusCode.Should().Be(
            HttpStatusCode.OK,
            await response.Content.ReadAsStringAsync());
        repository.LegacyResolveCalls.Should().Be(0);
        handoff.Commands.Should().ContainSingle();
        var command = handoff.Commands[0];
        command.Action.Should().Be(ReconciliationCaseworkAction.Resolve);
        command.Actor.Should().Be("ops-user");
        command.Source.Should().Be("workstation-statement-legacy-resolve-adapter");
        command.ActionOrigin.Should().Be(OperationsActionOriginDto.HumanOperator);
        command.ExpectedVersion.Should().Be(item.Version);
        command.RootCauseCode.Should().Be(item.RootCauseCode);
        command.ResolutionCode.Should().Be("LegacyResolved");
    }

    [Fact]
    public async Task ResolveReconciliationBreak_StatementSourceWithoutHandoff_ShouldFailClosed()
    {
        var item = BuildStatementQueueItem("statement-authority-missing");
        var repository = new RecordingStatementQueueRepository(item);
        await using var app = await CreateAppAsync(services =>
            services.AddSingleton<IReconciliationBreakQueueRepository>(repository));

        var response = await app.GetTestClient().PostAsJsonAsync(
            UiApiRoutes.ReconciliationBreakResolve.Replace("{breakId}", item.BreakId, StringComparison.Ordinal),
            new ResolveReconciliationBreakRequest(
                item.BreakId,
                ReconciliationBreakQueueStatus.Resolved,
                ResolvedBy: "browser-supplied-actor",
                ResolutionNote: "Statement evidence reconciled.",
                OperatorRationale: "Reviewed retained statement and ledger evidence."));

        response.StatusCode.Should().Be(
            HttpStatusCode.ServiceUnavailable,
            await response.Content.ReadAsStringAsync());
        repository.LegacyResolveCalls.Should().Be(0);
        (await repository.GetByIdAsync(item.BreakId))!.Status
            .Should().Be(ReconciliationBreakQueueStatus.InReview);
    }

    [Fact]
    public async Task ResolveReconciliationBreak_CompletedStatementReplay_ShouldReconstructExactRetainedCommand()
    {
        var item = BuildStatementQueueItem("statement-authority-replay");
        var repository = new RecordingStatementQueueRepository(item);
        var handoff = new RecordingStatementCaseworkHandoffService(item);
        await using var app = await CreateAppAsync(services =>
        {
            services.AddSingleton<IReconciliationBreakQueueRepository>(repository);
            services.AddSingleton<IStatementReconciliationCaseworkHandoffService>(handoff);
        });
        var request = new ResolveReconciliationBreakRequest(
            item.BreakId,
            ReconciliationBreakQueueStatus.Resolved,
            ResolvedBy: "browser-supplied-actor",
            ResolutionNote: "Statement evidence reconciled.",
            OperatorRationale: "Reviewed retained statement and ledger evidence.");
        var route = UiApiRoutes.ReconciliationBreakResolve.Replace(
            "{breakId}",
            item.BreakId,
            StringComparison.Ordinal);

        var firstResponse = await app.GetTestClient().PostAsJsonAsync(route, request);
        firstResponse.StatusCode.Should().Be(
            HttpStatusCode.OK,
            await firstResponse.Content.ReadAsStringAsync());
        var retainedCommand = handoff.Commands.Should().ContainSingle().Which;
        repository.SetCurrent(
            item with
            {
                Status = ReconciliationBreakQueueStatus.Resolved,
                LifecycleState = ReconciliationCaseLifecycleState.Resolved,
                Version = item.Version + 2,
                EvidenceLinks =
                [
                    .. item.EvidenceLinks!,
                    StatementCaseworkHandoffObligation.CreateCompletedMarker(retainedCommand.CommandId)
                ]
            },
            new ReconciliationBreakQueueAuditEvent(
                EventId: "statement-authority-replay-audit",
                BreakId: item.BreakId,
                EventType: "ResolutionSet",
                PreviousStatus: item.Status,
                NewStatus: ReconciliationBreakQueueStatus.Resolved,
                PreviousLifecycleState: item.LifecycleState,
                NewLifecycleState: ReconciliationCaseLifecycleState.Resolved,
                OccurredAt: item.LastUpdatedAt,
                AssignedTo: item.AssignedTo,
                ReviewedBy: item.ReviewedBy,
                ResolvedBy: "ops-user",
                Note: request.ResolutionNote,
                BeforePayload: JsonSerializer.Serialize(item, ServerJsonOptions),
                CommandId: retainedCommand.CommandId));

        var replayResponse = await app.GetTestClient().PostAsJsonAsync(route, request);
        replayResponse.StatusCode.Should().Be(
            HttpStatusCode.OK,
            await replayResponse.Content.ReadAsStringAsync());

        handoff.Commands.Should().HaveCount(2);
        handoff.Commands[1].Should().BeEquivalentTo(
            retainedCommand,
            options => options.WithStrictOrdering());
    }

    private static ReconciliationBreakQueueItem BuildStatementQueueItem(string breakId)
        => BuildBreakQueueItem(breakId, ledgerBookId: null) with
        {
            SourceType = "statement",
            SourceSystem = "statement-reconciliation",
            SourceImportId = $"import-{breakId}",
            SourceBreakId = $"source-{breakId}",
            Status = ReconciliationBreakQueueStatus.InReview,
            LifecycleState = ReconciliationCaseLifecycleState.Investigating,
            RootCauseCode = "BrokerCashTiming",
            ResolutionCode = null,
            Version = 7,
            EvidenceLinks = ["statement:evidence-retained"]
        };

    private sealed class RecordingStatementQueueRepository(ReconciliationBreakQueueItem item)
        : IReconciliationBreakQueueRepository
    {
        private ReconciliationBreakQueueItem _item = item;
        private IReadOnlyList<ReconciliationBreakQueueAuditEvent> _audit = [];

        public int LegacyResolveCalls { get; private set; }

        public void SetCurrent(
            ReconciliationBreakQueueItem current,
            params ReconciliationBreakQueueAuditEvent[] audit)
        {
            _item = current;
            _audit = audit;
        }

        public Task<IReadOnlyList<ReconciliationBreakQueueItem>> GetAllAsync(
            ReconciliationBreakQueueStatus? status = null,
            CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ReconciliationBreakQueueItem>>(
                status is null || _item.Status == status ? [_item] : []);

        public Task<ReconciliationBreakQueueItem?> GetByIdAsync(
            string breakId,
            CancellationToken ct = default)
            => Task.FromResult<ReconciliationBreakQueueItem?>(
                string.Equals(_item.BreakId, breakId, StringComparison.OrdinalIgnoreCase) ? _item : null);

        public Task<bool> CreateIfMissingAsync(
            ReconciliationBreakQueueItem candidate,
            CancellationToken ct = default)
            => Task.FromResult(false);

        public Task SaveAsync(ReconciliationBreakQueueItem candidate, CancellationToken ct = default)
        {
            _item = candidate;
            return Task.CompletedTask;
        }

        public Task<bool> DeleteAsync(string breakId, CancellationToken ct = default)
            => Task.FromResult(false);

        public Task<ReconciliationBreakQueueTransitionResult> StartReviewAsync(
            ReviewReconciliationBreakRequest request,
            CancellationToken ct = default)
            => Task.FromResult(new ReconciliationBreakQueueTransitionResult(
                ReconciliationBreakQueueTransitionStatus.InvalidTransition,
                _item));

        public Task<ReconciliationBreakQueueTransitionResult> ResolveAsync(
            ResolveReconciliationBreakRequest request,
            CancellationToken ct = default)
        {
            LegacyResolveCalls++;
            return Task.FromResult(new ReconciliationBreakQueueTransitionResult(
                ReconciliationBreakQueueTransitionStatus.Success,
                _item));
        }

        public Task<IReadOnlyList<ReconciliationBreakQueueAuditEvent>> GetAuditHistoryAsync(
            string breakId,
            CancellationToken ct = default)
            => Task.FromResult(_audit);
    }

    private sealed class RecordingStatementCaseworkHandoffService(
        ReconciliationBreakQueueItem item) : IStatementReconciliationCaseworkHandoffService
    {
        public List<ReconciliationCaseworkCommand> Commands { get; } = [];

        public Task<ReconciliationBreakQueueTransitionResult> ApplyAsync(
            ReconciliationCaseworkCommand command,
            CancellationToken ct = default)
        {
            Commands.Add(command);
            return Task.FromResult(new ReconciliationBreakQueueTransitionResult(
                ReconciliationBreakQueueTransitionStatus.Success,
                item with
                {
                    Status = ReconciliationBreakQueueStatus.Resolved,
                    LifecycleState = ReconciliationCaseLifecycleState.Resolved
                }));
        }

        public Task<ReconciliationBulkCaseworkResult> ApplyBulkAsync(
            ReconciliationBulkCaseworkRequest request,
            CancellationToken ct = default)
            => throw new NotSupportedException();
    }
}

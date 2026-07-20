using FluentAssertions;
using Meridian.Contracts.Operations;
using Meridian.Storage.Operations;
using Meridian.Workflow.Runbooks;

namespace Meridian.Tests.Workflow;

public sealed class RunbookServicesTests : IDisposable
{
    private readonly string _dataRoot;

    public RunbookServicesTests()
    {
        _dataRoot = Path.Combine(Path.GetTempPath(), "Meridian.Tests", "Runbooks", Guid.NewGuid().ToString("N"));
    }

    [Fact]
    public async Task JsonRunbookStore_PersistsDefinitionsAndListsByName()
    {
        var store = new JsonRunbookStore(_dataRoot);
        var now = DateTimeOffset.Parse("2026-06-06T00:00:00Z");

        await store.SaveAsync(new RunbookDefinition(
            "z-close",
            "Close Package",
            "Prepare close package.",
            [new RunbookStep("close", "fund-1")],
            now,
            now));
        await store.SaveAsync(new RunbookDefinition(
            "a-readiness",
            "Accounting Readiness",
            "Validate retained evidence.",
            [new RunbookStep("readiness", "fund-1")],
            now,
            now.AddMinutes(1)));

        var listed = await store.ListAsync();
        var fetched = await store.GetAsync("z-close");

        listed.Select(x => x.Id).Should().Equal("a-readiness", "z-close");
        fetched.Should().NotBeNull();
        fetched!.Steps.Should().ContainSingle()
            .Which.Should().Be(new RunbookStep("close", "fund-1"));
    }

    [Fact]
    public async Task RunbookExecutor_EmitsDryRunStepMessages()
    {
        var executor = new RunbookExecutor(HistoryStore());
        var definition = new RunbookDefinition(
            "readiness",
            "Readiness",
            "Check readiness.",
            [new RunbookStep("readiness", "global")],
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);

        var result = await executor.ExecuteAsync(definition, dryRun: true);

        result.Success.Should().BeTrue();
        result.Outcome.State.Should().Be(OperationTerminalState.Succeeded);
        result.RunbookId.Should().Be("readiness");
        result.Messages.Should().Contain("Runbook 'Readiness' started (dry-run).");
        result.Messages.Should().Contain("Step 1: readiness inspected.");
        result.Messages.Should().Contain("Runbook dry-run completed; no steps were executed.");
        result.Messages.Should().NotContain(message => message.Contains("global", StringComparison.Ordinal));
        result.Outcome.Postconditions.Should().Contain(postcondition =>
            postcondition.Code == "steps-executed" &&
            postcondition.State == OperationPostconditionState.Satisfied);
        VerifiedOperationOutcomeValidator.Validate(result.Outcome).Should().BeEmpty();
        var history = await HistoryStore().ReadAsync(new OperationalCaseHistoryQuery
        {
            CaseId = result.Outcome.OperationId,
            CaseType = "runbook-execution"
        });
        history.Select(record => record.EventType).Should().Equal(
            "runbook.step.inspected",
            "runbook.terminal.succeeded");
    }

    [Fact]
    public async Task RunbookExecutor_WithoutRegisteredHandlers_BlocksWithoutExecutionClaim()
    {
        var executor = new RunbookExecutor(HistoryStore());
        var definition = new RunbookDefinition(
            "readiness",
            "Readiness",
            "Check readiness.",
            [new RunbookStep("readiness", "sensitive-payload")],
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);

        var result = await executor.ExecuteAsync(definition, dryRun: false);

        result.Success.Should().BeFalse();
        result.Outcome.State.Should().Be(OperationTerminalState.Blocked);
        result.Messages.Should().ContainSingle(message => message.Contains("No steps were executed", StringComparison.Ordinal));
        result.Messages.Should().NotContain(message => message.Contains("sensitive-payload", StringComparison.Ordinal));
        result.Outcome.Postconditions.Should().Contain(postcondition =>
            postcondition.Code == "handlers-available" &&
            postcondition.State == OperationPostconditionState.NotSatisfied);
        result.Outcome.Recovery.Should().ContainSingle(action => action.ActionId == "register-runbook-handlers");
        VerifiedOperationOutcomeValidator.Validate(result.Outcome).Should().BeEmpty();
    }

    [Fact]
    public async Task RunbookExecutor_WhenFinalHandlerFails_DoesNotClaimAllStepsExecuted()
    {
        var executor = new RunbookExecutor(HistoryStore(), [
            new StaticRunbookStepHandler("readiness", CreateChildOutcome(OperationTerminalState.Failed))
        ]);
        var definition = new RunbookDefinition(
            "readiness",
            "Readiness",
            "Check readiness.",
            [new RunbookStep("readiness", "sensitive-payload")],
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);

        var result = await executor.ExecuteAsync(definition, dryRun: false);

        result.Outcome.State.Should().Be(OperationTerminalState.Failed);
        result.Outcome.Postconditions.Should().ContainSingle(postcondition =>
            postcondition.Code == "steps-executed" &&
            postcondition.State == OperationPostconditionState.NotSatisfied);
        VerifiedOperationOutcomeValidator.Validate(result.Outcome).Should().BeEmpty();
    }

    [Fact]
    public async Task RunbookExecutor_PropagatesChildArtifactsWithCollisionSafeIdentifiers()
    {
        var executor = new RunbookExecutor(HistoryStore(), [
            new StaticRunbookStepHandler("first", CreateChildOutcome(OperationTerminalState.CompletedWithWarnings, includeArtifact: true)),
            new StaticRunbookStepHandler("second", CreateChildOutcome(OperationTerminalState.Succeeded, includeArtifact: true))
        ]);
        var definition = new RunbookDefinition(
            "evidence",
            "Evidence",
            "Retain step artifacts.",
            [new RunbookStep("first", "payload-one"), new RunbookStep("second", "payload-two")],
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);

        var result = await executor.ExecuteAsync(definition, dryRun: false);

        result.Outcome.State.Should().Be(OperationTerminalState.CompletedWithWarnings);
        result.Outcome.Artifacts.Select(artifact => artifact.ArtifactId).Should().Equal(
            "step-1:result",
            "step-2:result");
        result.Outcome.Postconditions.Single(postcondition => postcondition.Code == "steps-executed")
            .ArtifactIds.Should().BeEquivalentTo("step-1:result", "step-2:result");
        result.Outcome.Issues.Should().ContainSingle(issue =>
            issue.Code == "step-1:handler-warning" &&
            issue.Message == "Review the handler warning.");
        result.Outcome.Recovery.Should().ContainSingle(action =>
            action.ActionId == "step-1:review-handler" &&
            action.ArtifactIds.SequenceEqual(new[] { "step-1:result" }));
        VerifiedOperationOutcomeValidator.Validate(result.Outcome).Should().BeEmpty();
    }

    [Fact]
    public async Task RunbookExecutor_PersistsStepAndParentReceiptsReadableAfterRestart()
    {
        var executor = new RunbookExecutor(HistoryStore(), [
            new StaticRunbookStepHandler("readiness", CreateChildOutcome(OperationTerminalState.Succeeded, includeArtifact: true))
        ]);
        var definition = new RunbookDefinition(
            "readiness",
            "Readiness",
            "Check readiness.",
            [new RunbookStep("readiness", "sensitive-payload")],
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);

        var result = await executor.ExecuteAsync(definition, dryRun: false);
        var restartedStore = new FileOperationalCaseHistoryStore(_dataRoot);
        var history = await restartedStore.ReadAsync(new OperationalCaseHistoryQuery
        {
            CaseId = result.Outcome.OperationId,
            CaseType = "runbook-execution"
        });

        history.Select(record => record.EventType).Should().Equal(
            "runbook.step.terminal",
            "runbook.terminal.succeeded");
        history[0].TerminalOutcome!.OperationKind.Should().Be("runbook.step");
        history[0].Data.Should().ContainKey("stepInputHashSha256");
        history[0].Data.Values.Should().NotContain("sensitive-payload");
        history[^1].TerminalOutcome.Should().BeEquivalentTo(result.Outcome);
        VerifiedOperationOutcomeValidator.Validate(history[^1].TerminalOutcome!).Should().BeEmpty();
    }

    [Fact]
    public async Task RunbookExecutor_HonorsCancellationBeforeExecutingSteps()
    {
        var executor = new RunbookExecutor(HistoryStore());
        var definition = new RunbookDefinition(
            "readiness",
            "Readiness",
            "Check readiness.",
            [new RunbookStep("readiness", "global")],
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => executor.ExecuteAsync(definition, dryRun: false, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task RunbookExecutor_WhenCancelledAfterAdmission_ReturnsAndPersistsFailedReceipt()
    {
        using var cts = new CancellationTokenSource();
        var executor = new RunbookExecutor(HistoryStore(), [new CancellingRunbookStepHandler("readiness", cts)]);
        var definition = new RunbookDefinition(
            "readiness",
            "Readiness",
            "Check readiness.",
            [new RunbookStep("readiness", "global")],
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);

        var result = await executor.ExecuteAsync(definition, dryRun: false, cts.Token);

        result.Success.Should().BeFalse();
        result.Outcome.State.Should().Be(OperationTerminalState.Failed);
        result.Outcome.Issues.Should().ContainSingle(issue => issue.Code == "runbook-cancelled-after-admission");
        VerifiedOperationOutcomeValidator.Validate(result.Outcome).Should().BeEmpty();
        var history = await HistoryStore().ReadAsync(new OperationalCaseHistoryQuery
        {
            CaseId = result.Outcome.OperationId,
            CaseType = "runbook-execution"
        });
        history.Select(record => record.EventType).Should().Equal("runbook.terminal.failed");
        history[^1].TerminalOutcome.Should().BeEquivalentTo(result.Outcome);
    }

    [Fact]
    public async Task RunbookExecutor_WhenDryRunInspectionHistoryAppendFails_ReturnsValidatedFailureWithoutClaimingRetention()
    {
        var historyStore = new ThrowingOperationalCaseHistoryStore(_ => true);
        var executor = new RunbookExecutor(historyStore);
        var definition = CreateDefinition(new RunbookStep("readiness", "sensitive-payload"));

        var result = await executor.ExecuteAsync(definition, dryRun: true);

        result.Success.Should().BeFalse();
        result.Outcome.State.Should().Be(OperationTerminalState.Failed);
        result.Outcome.Issues.Should().ContainSingle(issue => issue.Code == "runbook-case-history-append-failed");
        result.Outcome.Evidence.Should().ContainSingle(evidence =>
            evidence.Kind == "case-history-append-failure" &&
            evidence.Description.Contains("attempted case-history record is not claimed as retained", StringComparison.Ordinal));
        result.Outcome.Recovery.Should().ContainSingle(action =>
            action.ActionId == "restore-case-history-and-reconcile-runbook");
        result.Messages.Should().Contain(message => message.Contains("attempted case-history record is not claimed as retained", StringComparison.Ordinal));
        VerifiedOperationOutcomeValidator.Validate(result.Outcome).Should().BeEmpty();
    }

    [Fact]
    public async Task RunbookExecutor_WhenMissingHandlerTerminalHistoryAppendFails_ReturnsPersistenceFailure()
    {
        var historyStore = new ThrowingOperationalCaseHistoryStore(_ => true);
        var executor = new RunbookExecutor(historyStore);

        var result = await executor.ExecuteAsync(CreateDefinition(new RunbookStep("readiness", "payload")), dryRun: false);

        result.Outcome.State.Should().Be(OperationTerminalState.Failed);
        result.Outcome.Issues.Should().ContainSingle(issue => issue.Code == "runbook-case-history-append-failed");
        result.Messages.Should().Contain(message => message.Contains("blocked terminalization", StringComparison.Ordinal));
        VerifiedOperationOutcomeValidator.Validate(result.Outcome).Should().BeEmpty();
    }

    [Fact]
    public async Task RunbookExecutor_WhenSuccessfulStepReceiptAppendFails_PreservesSideEffectAndReturnsPersistenceFailure()
    {
        var historyStore = new ThrowingOperationalCaseHistoryStore(_ => true);
        var handler = new StaticRunbookStepHandler("readiness", CreateChildOutcome(OperationTerminalState.Succeeded));
        var executor = new RunbookExecutor(historyStore, [handler]);

        var result = await executor.ExecuteAsync(CreateDefinition(new RunbookStep("readiness", "payload")), dryRun: false);

        handler.ExecutionCount.Should().Be(1);
        historyStore.AppendRequests.Should().ContainSingle(request => request.EventType == "runbook.step.terminal");
        result.Outcome.State.Should().Be(OperationTerminalState.Failed);
        result.Outcome.Issues.Should().ContainSingle(issue => issue.Code == "runbook-case-history-append-failed");
        result.Outcome.Issues.Should().NotContain(issue => issue.Code == "step-1-exception");
        result.Messages.Should().Contain(message => message.Contains("step 1 terminal receipt", StringComparison.Ordinal));
        VerifiedOperationOutcomeValidator.Validate(result.Outcome).Should().BeEmpty();
    }

    [Fact]
    public async Task RunbookExecutor_WhenFailedStepTerminalizationAppendFails_DoesNotMisclassifyPersistenceAsHandlerFailure()
    {
        var historyStore = new ThrowingOperationalCaseHistoryStore(_ => true);
        var executor = new RunbookExecutor(historyStore, [new ThrowingRunbookStepHandler("readiness")]);

        var result = await executor.ExecuteAsync(CreateDefinition(new RunbookStep("readiness", "payload")), dryRun: false);

        result.Outcome.State.Should().Be(OperationTerminalState.Failed);
        result.Outcome.Issues.Should().ContainSingle(issue => issue.Code == "runbook-case-history-append-failed");
        result.Messages.Should().Contain(message => message.Contains("failed step 1 terminalization", StringComparison.Ordinal));
        VerifiedOperationOutcomeValidator.Validate(result.Outcome).Should().BeEmpty();
    }

    [Fact]
    public async Task RunbookExecutor_WhenFinalHistoryAppendFails_ReturnsPersistenceFailureAfterRetainingStepReceipt()
    {
        var historyStore = new ThrowingOperationalCaseHistoryStore(request =>
            request.EventType.StartsWith("runbook.terminal.", StringComparison.Ordinal));
        var executor = new RunbookExecutor(historyStore, [
            new StaticRunbookStepHandler("readiness", CreateChildOutcome(OperationTerminalState.Succeeded))
        ]);

        var result = await executor.ExecuteAsync(CreateDefinition(new RunbookStep("readiness", "payload")), dryRun: false);

        historyStore.AppendRequests.Select(request => request.EventType).Should().Equal(
            "runbook.step.terminal",
            "runbook.terminal.succeeded");
        result.Outcome.State.Should().Be(OperationTerminalState.Failed);
        result.Outcome.Issues.Should().ContainSingle(issue => issue.Code == "runbook-case-history-append-failed");
        result.Messages.Should().Contain(message => message.Contains("runbook terminalization", StringComparison.Ordinal));
        VerifiedOperationOutcomeValidator.Validate(result.Outcome).Should().BeEmpty();
    }

    public void Dispose()
    {
        if (Directory.Exists(_dataRoot))
        {
            Directory.Delete(_dataRoot, recursive: true);
        }
    }

    private FileOperationalCaseHistoryStore HistoryStore() => new(_dataRoot);

    private static RunbookDefinition CreateDefinition(params RunbookStep[] steps) => new(
        "readiness",
        "Readiness",
        "Check readiness.",
        steps,
        DateTimeOffset.UtcNow,
        DateTimeOffset.UtcNow);

    private static VerifiedOperationOutcome CreateChildOutcome(
        OperationTerminalState state,
        bool includeArtifact = false)
    {
        var now = DateTimeOffset.UtcNow;
        var evidence = new OperationEvidenceReference(
            "child-evidence",
            "handler-result",
            "The handler terminal state was captured.",
            Uri: $"urn:sha256:{new string('B', 64)}",
            ContentHashSha256: new string('B', 64),
            CapturedAtUtc: now);
        var artifacts = includeArtifact
            ? new[]
            {
                new OperationArtifactReference(
                    "result",
                    "result.csv",
                    "text/csv",
                    4,
                    new string('A', 64),
                    "file:///retained/result.csv")
            }
            : [];
        var postcondition = new OperationPostcondition(
            "handler-completed",
            "The handler completed successfully.",
            state is OperationTerminalState.Succeeded or OperationTerminalState.CompletedWithWarnings
                ? OperationPostconditionState.Satisfied
                : OperationPostconditionState.NotSatisfied,
            Required: true,
            EvidenceIds: [evidence.EvidenceId])
        {
            ArtifactIds = artifacts.Select(artifact => artifact.ArtifactId).ToArray()
        };
        var issues = state switch
        {
            OperationTerminalState.CompletedWithWarnings =>
                new[] { new OperationIssue("handler-warning", "Review the handler warning.", OperationIssueSeverity.Warning, EvidenceId: evidence.EvidenceId) },
            OperationTerminalState.Failed =>
                [new OperationIssue("handler-failed", "The handler failed.", OperationIssueSeverity.Error, EvidenceId: evidence.EvidenceId)],
            OperationTerminalState.Blocked =>
                [new OperationIssue("handler-blocked", "The handler is blocked.", OperationIssueSeverity.Error, EvidenceId: evidence.EvidenceId) { IsBlocking = true }],
            _ => []
        };
        var recovery = state == OperationTerminalState.Succeeded
            ? []
            : new[]
            {
                new OperationRecoveryAction(
                    "review-handler",
                    "Review handler",
                    "Review retained evidence and retry when appropriate.",
                    Retryable: true,
                    RequiresHumanAction: true)
                {
                    EvidenceIds = [evidence.EvidenceId],
                    ArtifactIds = artifacts.Select(artifact => artifact.ArtifactId).ToArray()
                }
            };

        return VerifiedOperationOutcomeValidator.ValidateAndThrow(new VerifiedOperationOutcome(
            $"child:{state}:{Guid.NewGuid():N}",
            "runbook.step",
            state,
            now,
            now,
            1,
            "runbook-test",
            new string('B', 64),
            [postcondition],
            [evidence],
            artifacts,
            issues,
            recovery));
    }

    private sealed class StaticRunbookStepHandler(
        string kind,
        VerifiedOperationOutcome outcome) : IRunbookStepHandler
    {
        public string Kind { get; } = kind;

        public int ExecutionCount { get; private set; }

        public Task<VerifiedOperationOutcome> ExecuteAsync(RunbookStep step, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            ExecutionCount++;
            return Task.FromResult(outcome);
        }
    }

    private sealed class ThrowingRunbookStepHandler(string kind) : IRunbookStepHandler
    {
        public string Kind { get; } = kind;

        public Task<VerifiedOperationOutcome> ExecuteAsync(RunbookStep step, CancellationToken ct = default)
            => throw new InvalidOperationException("Handler side effect failed after admission.");
    }

    private sealed class CancellingRunbookStepHandler(
        string kind,
        CancellationTokenSource cancellation) : IRunbookStepHandler
    {
        public string Kind { get; } = kind;

        public Task<VerifiedOperationOutcome> ExecuteAsync(RunbookStep step, CancellationToken ct = default)
        {
            cancellation.Cancel();
            return Task.FromCanceled<VerifiedOperationOutcome>(ct);
        }
    }

    private sealed class ThrowingOperationalCaseHistoryStore(
        Func<OperationalCaseHistoryAppendRequest, bool> shouldThrow) : IOperationalCaseHistoryStore
    {
        public List<OperationalCaseHistoryAppendRequest> AppendRequests { get; } = [];

        public ValueTask<OperationalCaseHistoryRecord> AppendAsync(
            OperationalCaseHistoryAppendRequest request,
            CancellationToken cancellationToken = default)
        {
            AppendRequests.Add(request);
            if (shouldThrow(request))
                throw new InvalidOperationException("Case-history persistence is unavailable.");

            return ValueTask.FromResult(new OperationalCaseHistoryRecord
            {
                CaseId = request.CaseId,
                CaseType = request.CaseType,
                HistoryEventId = request.HistoryEventId,
                EventType = request.EventType,
                Sequence = AppendRequests.Count,
                RecordHashSha256 = new string('C', 64),
                OccurredAtUtc = request.OccurredAtUtc,
                PersistedAtUtc = request.OccurredAtUtc,
                ActorId = request.ActorId,
                Reason = request.Reason,
                CorrelationId = request.CorrelationId,
                InputHashSha256 = request.InputHashSha256,
                Data = request.Data,
                Transition = request.Transition,
                Exceptions = request.Exceptions,
                Evidence = request.Evidence,
                Artifacts = request.Artifacts,
                TerminalOutcome = request.TerminalOutcome
            });
        }

        public ValueTask<IReadOnlyList<OperationalCaseHistoryRecord>> ReadAsync(
            OperationalCaseHistoryQuery query,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<OperationalCaseHistoryRecord>>([]);
    }
}

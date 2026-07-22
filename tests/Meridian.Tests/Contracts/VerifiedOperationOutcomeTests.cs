using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Operations;

namespace Meridian.Tests.Contracts;

public sealed class VerifiedOperationOutcomeTests
{
    private const string Hash = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    [Theory]
    [InlineData(OperationTerminalState.Succeeded, true)]
    [InlineData(OperationTerminalState.CompletedWithWarnings, true)]
    [InlineData(OperationTerminalState.Failed, false)]
    [InlineData(OperationTerminalState.Blocked, false)]
    public void Validator_HonestTerminalState_AcceptsOutcome(
        OperationTerminalState state,
        bool expectedSuccessful)
    {
        var outcome = CreateOutcome(state);

        VerifiedOperationOutcomeValidator.Validate(outcome).Should().BeEmpty();
        outcome.IsSuccessful.Should().Be(expectedSuccessful);
    }

    [Fact]
    public void Validator_DefaultProvenance_IsRealAndAccepted()
    {
        var outcome = CreateOutcome(OperationTerminalState.Succeeded);

        outcome.Provenance.Should().Be(DataProvenance.Real);
        VerifiedOperationOutcomeValidator.Validate(outcome).Should().BeEmpty();
    }

    [Fact]
    public void Validator_SimulatedProvenance_IsRetainedAndAccepted()
    {
        var outcome = CreateOutcome(OperationTerminalState.Succeeded) with
        {
            Provenance = DataProvenance.Simulated
        };

        outcome.Provenance.Should().Be(DataProvenance.Simulated);
        VerifiedOperationOutcomeValidator.Validate(outcome).Should().BeEmpty();
    }

    [Fact]
    public void Validator_UndefinedProvenance_RejectsOutcome()
    {
        var outcome = CreateOutcome(OperationTerminalState.Succeeded) with
        {
            Provenance = (DataProvenance)200
        };

        VerifiedOperationOutcomeValidator.Validate(outcome)
            .Should().Contain(error => error.Contains("not a defined DataProvenance", StringComparison.Ordinal));
    }

    [Fact]
    public void Validator_UnknownSchemaVersion_RejectsOutcome()
    {
        var outcome = CreateOutcome(OperationTerminalState.Succeeded) with
        {
            SchemaVersion = "meridian.verified-operation-outcome.v999"
        };

        VerifiedOperationOutcomeValidator.Validate(outcome)
            .Should().Contain(error => error.Contains("Unsupported SchemaVersion", StringComparison.Ordinal));
    }

    [Fact]
    public void SourceGeneratedJson_OmittedTerminalState_FailsClosed()
    {
        var json = JsonSerializer.Serialize(
            CreateOutcome(OperationTerminalState.Succeeded),
            OperationsContractsJsonContext.Default.VerifiedOperationOutcome);
        var document = JsonNode.Parse(json)!.AsObject();
        document.Remove("state");

        var outcome = JsonSerializer.Deserialize(
            document.ToJsonString(),
            OperationsContractsJsonContext.Default.VerifiedOperationOutcome);

        outcome.Should().NotBeNull();
        outcome!.State.Should().Be(OperationTerminalState.Unknown);
        VerifiedOperationOutcomeValidator.Validate(outcome)
            .Should().Contain(error => error.Contains("Unsupported terminal state", StringComparison.Ordinal));
    }

    [Fact]
    public void SourceGeneratedJson_OmittedPostconditionState_FailsClosed()
    {
        var json = JsonSerializer.Serialize(
            CreateOutcome(OperationTerminalState.Succeeded),
            OperationsContractsJsonContext.Default.VerifiedOperationOutcome);
        var document = JsonNode.Parse(json)!.AsObject();
        document["postconditions"]![0]!.AsObject().Remove("state");

        var outcome = JsonSerializer.Deserialize(
            document.ToJsonString(),
            OperationsContractsJsonContext.Default.VerifiedOperationOutcome);

        outcome.Should().NotBeNull();
        VerifiedOperationOutcomeValidator.Validate(outcome!)
            .Should().Contain(error => error.Contains("unsupported state", StringComparison.Ordinal));
    }

    [Fact]
    public void SourceGeneratedJson_OmittedIssueSeverity_FailsClosed()
    {
        var json = JsonSerializer.Serialize(
            CreateOutcome(OperationTerminalState.CompletedWithWarnings),
            OperationsContractsJsonContext.Default.VerifiedOperationOutcome);
        var document = JsonNode.Parse(json)!.AsObject();
        document["issues"]![0]!.AsObject().Remove("severity");

        var outcome = JsonSerializer.Deserialize(
            document.ToJsonString(),
            OperationsContractsJsonContext.Default.VerifiedOperationOutcome);

        outcome.Should().NotBeNull();
        VerifiedOperationOutcomeValidator.Validate(outcome!)
            .Should().Contain(error => error.Contains("unsupported severity", StringComparison.Ordinal));
    }

    [Fact]
    public void Validator_SucceededWithUnmetPostcondition_RejectsFalseSuccess()
    {
        var outcome = CreateOutcome(OperationTerminalState.Succeeded) with
        {
            Postconditions = [RequiredPostcondition(OperationPostconditionState.NotSatisfied)]
        };

        VerifiedOperationOutcomeValidator.Validate(outcome)
            .Should().Contain(error => error.Contains("Succeeded requires", StringComparison.Ordinal));
    }

    [Fact]
    public void Validator_CompletedWithWarningsWithoutWarning_RejectsFalseWarningState()
    {
        var outcome = CreateOutcome(OperationTerminalState.CompletedWithWarnings) with { Issues = [] };

        VerifiedOperationOutcomeValidator.Validate(outcome)
            .Should().Contain(error => error.Contains("requires at least one warning", StringComparison.Ordinal));
    }

    [Fact]
    public void Validator_CompletedWithWarningsWithError_RejectsFalseWarningState()
    {
        var outcome = CreateOutcome(OperationTerminalState.CompletedWithWarnings) with
        {
            Issues = [ErrorIssue(isBlocking: false)]
        };

        VerifiedOperationOutcomeValidator.Validate(outcome)
            .Should().Contain(error => error.Contains("cannot contain error", StringComparison.Ordinal));
    }

    [Fact]
    public void Validator_FailedWithoutErrorOrUnmetPostcondition_RejectsFalseFailure()
    {
        var outcome = CreateOutcome(OperationTerminalState.Failed) with
        {
            Postconditions = [RequiredPostcondition(OperationPostconditionState.Satisfied)],
            Issues = []
        };

        var errors = VerifiedOperationOutcomeValidator.Validate(outcome);
        errors.Should().Contain(error => error.Contains("Failed requires an unmet", StringComparison.Ordinal));
        errors.Should().Contain(error => error.Contains("Failed requires at least one error", StringComparison.Ordinal));
    }

    [Fact]
    public void Validator_BlockedWithoutBlockingError_RejectsFalseBlockedState()
    {
        var outcome = CreateOutcome(OperationTerminalState.Blocked) with
        {
            Issues = [ErrorIssue(isBlocking: false)]
        };

        VerifiedOperationOutcomeValidator.Validate(outcome)
            .Should().Contain(error => error.Contains("blocking error issue", StringComparison.Ordinal));
    }

    [Fact]
    public void Validator_UndefinedOptionalPostconditionState_RejectsOutcome()
    {
        var outcome = CreateOutcome(OperationTerminalState.Succeeded) with
        {
            Postconditions =
            [
                RequiredPostcondition(OperationPostconditionState.Satisfied),
                new OperationPostcondition(
                    "optional-check",
                    "Optional diagnostic check.",
                    (OperationPostconditionState)99,
                    Required: false,
                    EvidenceIds: ["evidence-1"])
            ]
        };

        VerifiedOperationOutcomeValidator.Validate(outcome)
            .Should().Contain(error => error.Contains("unsupported state", StringComparison.Ordinal));
    }

    [Fact]
    public void Validator_UndefinedIssueSeverity_RejectsOutcome()
    {
        var outcome = CreateOutcome(OperationTerminalState.CompletedWithWarnings) with
        {
            Issues =
            [
                new OperationIssue(
                    "operation.unknown-severity",
                    "The issue severity is not recognized.",
                    (OperationIssueSeverity)99,
                    EvidenceId: "evidence-1")
            ]
        };

        VerifiedOperationOutcomeValidator.Validate(outcome)
            .Should().Contain(error => error.Contains("unsupported severity", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(true, AccountingConfigurationValidationSeverityDto.Info, OperationTerminalState.CompletedWithWarnings)]
    [InlineData(false, AccountingConfigurationValidationSeverityDto.Warning, OperationTerminalState.Blocked)]
    [InlineData(false, AccountingConfigurationValidationSeverityDto.Critical, OperationTerminalState.Failed)]
    public void ClosePeriodResult_DerivesHonestTerminalOutcome(
        bool isLocked,
        AccountingConfigurationValidationSeverityDto severity,
        OperationTerminalState expectedState)
    {
        var result = new ClosePeriodLockResultDto(
            isLocked,
            Plan: null,
            Transition: null,
            Issues:
            [
                new AccountingConfigurationValidationIssueDto(
                    "close-evidence",
                    severity,
                    "Close evidence requires operator attention.",
                    SuggestedAction: "Inspect evidence and retry.")
            ]);

        result.Outcome.State.Should().Be(expectedState);
        VerifiedOperationOutcomeValidator.Validate(result.Outcome).Should().BeEmpty();
    }

    [Fact]
    public void Validator_InvalidArtifactAndMissingLinks_RejectsUnverifiedReferences()
    {
        var outcome = CreateOutcome(OperationTerminalState.Succeeded) with
        {
            Postconditions =
            [
                RequiredPostcondition(OperationPostconditionState.Satisfied) with
                {
                    EvidenceIds = ["missing-evidence"],
                    ArtifactIds = ["missing-artifact"]
                }
            ],
            Artifacts =
            [
                new OperationArtifactReference(
                    "artifact-1",
                    "result.csv",
                    "text/csv",
                    0,
                    "bad-hash",
                    Uri: "/api/artifacts/result.csv")
            ]
        };

        var errors = VerifiedOperationOutcomeValidator.Validate(outcome);
        errors.Should().Contain(error => error.Contains("missing evidence", StringComparison.Ordinal));
        errors.Should().Contain(error => error.Contains("missing artifact", StringComparison.Ordinal));
        errors.Should().Contain(error => error.Contains("non-empty retained bytes", StringComparison.Ordinal));
        errors.Should().Contain(error => error.Contains("64-character SHA-256", StringComparison.Ordinal));
    }

    [Fact]
    public void Validator_DescriptionOnlyEvidence_RejectsMessageOnlySuccess()
    {
        var outcome = CreateOutcome(OperationTerminalState.Succeeded) with
        {
            Evidence =
            [
                Evidence() with
                {
                    Uri = null,
                    ContentHashSha256 = null
                }
            ]
        };

        VerifiedOperationOutcomeValidator.Validate(outcome)
            .Should().Contain(error => error.Contains("durably locatable", StringComparison.Ordinal));
    }

    [Fact]
    public void Validator_FutureEvidence_RejectsFalseSuccess()
    {
        var outcome = CreateOutcome(OperationTerminalState.Succeeded);
        outcome = outcome with
        {
            Evidence = [Evidence() with { CapturedAtUtc = outcome.CompletedAtUtc.AddTicks(1) }]
        };

        VerifiedOperationOutcomeValidator.Validate(outcome)
            .Should().Contain(error => error.Contains("within the operation interval", StringComparison.Ordinal));
    }

    [Fact]
    public void Validator_DuplicateEvidenceAndArtifactIds_RejectsAmbiguousLinks()
    {
        var evidence = Evidence();
        var artifact = Artifact();
        var outcome = CreateOutcome(OperationTerminalState.Succeeded) with
        {
            Evidence = [evidence, evidence],
            Artifacts = [artifact, artifact]
        };

        var errors = VerifiedOperationOutcomeValidator.Validate(outcome);
        errors.Should().Contain(error => error.Contains("Duplicate evidence id", StringComparison.Ordinal));
        errors.Should().Contain(error => error.Contains("Duplicate artifact id", StringComparison.Ordinal));
    }

    [Fact]
    public void CaseHistoryHash_DataInsertionOrder_IsCanonical()
    {
        var record = CreateHistoryRecord(CreateOutcome(OperationTerminalState.Succeeded));
        var reversedData = record.Data
            .Reverse()
            .ToDictionary(static item => item.Key, static item => item.Value, StringComparer.Ordinal);

        var reorderedRecord = record with
        {
            RecordHashSha256 = string.Empty,
            Data = reversedData
        };

        OperationalCaseHistoryHashing.ComputeRecordHashSha256(reorderedRecord)
            .Should().Be(record.RecordHashSha256);
    }

    [Fact]
    public void SourceGeneratedJson_OutcomeAndCaseHistoryRecord_RoundTrip()
    {
        var outcome = CreateOutcome(OperationTerminalState.CompletedWithWarnings);
        var record = CreateHistoryRecord(outcome);

        var outcomeJson = JsonSerializer.Serialize(
            outcome,
            OperationsContractsJsonContext.Default.VerifiedOperationOutcome);
        var outcomeRoundTrip = JsonSerializer.Deserialize(
            outcomeJson,
            OperationsContractsJsonContext.Default.VerifiedOperationOutcome);
        var recordJson = JsonSerializer.Serialize(
            record,
            OperationsContractsJsonContext.Default.OperationalCaseHistoryRecord);
        var recordRoundTrip = JsonSerializer.Deserialize(
            recordJson,
            OperationsContractsJsonContext.Default.OperationalCaseHistoryRecord);

        outcomeRoundTrip.Should().BeEquivalentTo(outcome);
        recordRoundTrip.Should().BeEquivalentTo(record);
        outcomeJson.Should().Contain("\"state\":\"CompletedWithWarnings\"");
        outcomeJson.Should().Contain("\"isSuccessful\":true");
        OperationalCaseHistoryHashing.HasValidRecordHash(recordRoundTrip!).Should().BeTrue();
    }

    private static VerifiedOperationOutcome CreateOutcome(OperationTerminalState state)
    {
        var now = new DateTimeOffset(2026, 7, 19, 12, 0, 0, TimeSpan.Zero);
        var issues = state switch
        {
            OperationTerminalState.CompletedWithWarnings =>
                (IReadOnlyList<OperationIssue>)[new OperationIssue(
                    "operation.warning",
                    "Review the retained warning evidence.",
                    OperationIssueSeverity.Warning,
                    EvidenceId: "evidence-1")],
            OperationTerminalState.Failed => [ErrorIssue(isBlocking: false)],
            OperationTerminalState.Blocked => [ErrorIssue(isBlocking: true)],
            _ => []
        };
        var postconditionState = state is OperationTerminalState.Succeeded or OperationTerminalState.CompletedWithWarnings
            ? OperationPostconditionState.Satisfied
            : OperationPostconditionState.NotSatisfied;
        var recovery = state == OperationTerminalState.Succeeded
            ? []
            : (IReadOnlyList<OperationRecoveryAction>)[new OperationRecoveryAction(
                "review-and-retry",
                "Review and retry",
                "Review the retained evidence, correct the cause, and retry when permitted.",
                Retryable: state is OperationTerminalState.Failed or OperationTerminalState.Blocked,
                RequiresHumanAction: true,
                Route: "/operations/cases/case-1")];

        return new VerifiedOperationOutcome(
            "operation-1",
            "test-operation",
            state,
            now.AddSeconds(-1),
            now,
            1,
            "correlation-1",
            Hash,
            [RequiredPostcondition(postconditionState)],
            [Evidence()],
            [Artifact()],
            issues,
            recovery);
    }

    private static OperationIssue ErrorIssue(bool isBlocking) =>
        new(
            "operation.error",
            "The operation did not satisfy its required postcondition.",
            OperationIssueSeverity.Error,
            "IOException",
            "evidence-1")
        {
            IsBlocking = isBlocking
        };

    private static OperationPostcondition RequiredPostcondition(OperationPostconditionState state) =>
        new(
            "output-verified",
            "The operation output was independently verified.",
            state,
            Required: true,
            EvidenceIds: ["evidence-1"])
        {
            ArtifactIds = ["artifact-1"]
        };

    private static OperationEvidenceReference Evidence() =>
        new(
            "evidence-1",
            "operation-receipt",
            "Retained terminal operation receipt.",
            Uri: "evidence://operations/operation-1/receipt",
            ContentHashSha256: Hash,
            CapturedAtUtc: new DateTimeOffset(2026, 7, 19, 12, 0, 0, TimeSpan.Zero));

    private static OperationArtifactReference Artifact() =>
        new(
            "artifact-1",
            "result.csv",
            "text/csv",
            42,
            Hash,
            Uri: "/api/operations/operation-1/artifacts/result.csv",
            PreviewUri: "/api/operations/operation-1/artifacts/result.csv/preview");

    private static OperationalCaseHistoryRecord CreateHistoryRecord(VerifiedOperationOutcome outcome)
    {
        var occurredAt = outcome.CompletedAtUtc;
        var emptyHashRecord = new OperationalCaseHistoryRecord
        {
            CaseId = "case-1",
            CaseType = "test-operation",
            HistoryEventId = "event-1",
            EventType = "terminal-outcome",
            Sequence = 1,
            PreviousRecordHashSha256 = null,
            RecordHashSha256 = string.Empty,
            OccurredAtUtc = occurredAt,
            PersistedAtUtc = occurredAt.AddMilliseconds(10),
            ActorId = "operator-1",
            Reason = "Retain the verified terminal receipt.",
            CorrelationId = outcome.CorrelationId!,
            InputHashSha256 = outcome.InputHashSha256!,
            Data = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["schemaVersion"] = "strategy-run.v1",
                ["strategyRunSnapshotJson"] = "{\"runId\":\"run-1\",\"status\":\"CompletedWithWarnings\"}"
            },
            Transition = new OperationalCaseStateTransition
            {
                PreviousState = "Running",
                CurrentState = "CompletedWithWarnings",
                TransitionedAtUtc = occurredAt
            },
            Assignment = new OperationalCaseAssignment
            {
                PreviousAssigneeId = null,
                AssigneeId = "operator-1",
                AssignedBy = "supervisor-1",
                AssignedAtUtc = occurredAt
            },
            Retries =
            [
                new OperationalCaseRetry
                {
                    Attempt = 1,
                    AttemptedAtUtc = occurredAt,
                    Reason = "Initial attempt"
                }
            ],
            Exceptions =
            [
                new OperationalCaseException
                {
                    ExceptionType = "ProviderWarning",
                    Message = "One optional provider response was unavailable.",
                    OccurredAtUtc = occurredAt,
                    EvidenceIds = ["evidence-1"]
                }
            ],
            Approvals =
            [
                new OperationalCaseApproval
                {
                    ApprovalId = "approval-1",
                    Decision = "ApprovedWithWarning",
                    DecidedBy = "controller-1",
                    DecidedAtUtc = occurredAt,
                    Reason = "Required outputs were verified.",
                    EvidenceIds = ["evidence-1"]
                }
            ],
            Artifacts = outcome.Artifacts,
            Evidence = outcome.Evidence,
            RecoveryAttempts =
            [
                new OperationalCaseRecoveryAttempt
                {
                    RecoveryActionId = "review-and-retry",
                    Attempt = 1,
                    StartedAtUtc = occurredAt,
                    CompletedAtUtc = occurredAt.AddSeconds(1),
                    Result = "Warning reviewed",
                    EvidenceIds = ["evidence-1"],
                    ArtifactIds = ["artifact-1"]
                }
            ],
            TerminalOutcome = outcome
        };

        return emptyHashRecord with
        {
            RecordHashSha256 = OperationalCaseHistoryHashing.ComputeRecordHashSha256(emptyHashRecord)
        };
    }
}

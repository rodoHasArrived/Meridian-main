#if WINDOWS
using Meridian.Contracts.Api;
using Meridian.Contracts.Operations;
using Meridian.Contracts.Workstation;
using Meridian.Wpf.Services;

namespace Meridian.Wpf.Tests.Services;

public sealed class WorkstationReconciliationApiClientTests
{
    [Fact]
    public void ToActionResult_CompletedWithWarnings_RemainsSuccessfulAndExposesOperatorDetail()
    {
        var startedAt = new DateTimeOffset(2026, 7, 24, 16, 0, 0, TimeSpan.Zero);
        var outcome = VerifiedOperationOutcomeValidator.ValidateAndThrow(new VerifiedOperationOutcome(
            OperationId: "reconciliation-casework:warning-1",
            OperationKind: "reconciliation.casework.resolve",
            State: OperationTerminalState.CompletedWithWarnings,
            StartedAtUtc: startedAt,
            CompletedAtUtc: startedAt.AddSeconds(2),
            AttemptNumber: 1,
            CorrelationId: "warning-correlation-1",
            InputHashSha256: new string('a', 64),
            Postconditions:
            [
                new OperationPostcondition(
                    "break-resolved",
                    "The selected reconciliation break reached a terminal state.",
                    OperationPostconditionState.Satisfied,
                    Required: true,
                    EvidenceIds: ["warning-evidence"])
            ],
            Evidence:
            [
                new OperationEvidenceReference(
                    "warning-evidence",
                    "reconciliation-casework",
                    "Retained warning receipt.",
                    Uri: "urn:reconciliation:warning-1",
                    ContentHashSha256: new string('b', 64),
                    CapturedAtUtc: startedAt.AddSeconds(2))
            ],
            Artifacts: [],
            Issues:
            [
                new OperationIssue(
                    "supporting-evidence-stale",
                    "Supporting evidence is older than the preferred review window.",
                    OperationIssueSeverity.Warning,
                    EvidenceId: "warning-evidence")
            ],
            Recovery:
            [
                new OperationRecoveryAction(
                    "refresh-support",
                    "Refresh supporting evidence",
                    "Attach a current source statement before close sign-off.",
                    Retryable: true,
                    RequiresHumanAction: true)
                {
                    EvidenceIds = ["warning-evidence"]
                }
            ]));
        var response = ApiResponse<ReconciliationCaseworkOperationResult>.Ok(
            new ReconciliationCaseworkOperationResult(
                "resolved-with-warning",
                Item: null,
                Outcome: outcome));

        var result = WorkstationReconciliationApiClient.ToActionResult(response);

        result.Success.Should().BeTrue();
        result.ErrorMessage.Should().BeNull();
        result.CompletedWithWarnings.Should().BeTrue();
        result.Outcome.Should().BeSameAs(outcome);
        result.OperatorMessage.Should().StartWith("Reconciliation action completed with warnings.");
        result.OperatorMessage.Should().Contain("supporting-evidence-stale");
        result.OperatorMessage.Should().Contain("Supporting evidence is older than the preferred review window.");
        result.OperatorMessage.Should().Contain("Refresh supporting evidence");
        result.OperatorMessage.Should().Contain("Attach a current source statement before close sign-off.");
    }
}
#endif

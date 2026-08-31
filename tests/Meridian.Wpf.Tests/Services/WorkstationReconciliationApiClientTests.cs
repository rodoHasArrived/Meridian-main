#if WINDOWS
using System.Net;
using System.Net.Http;
using Meridian.Contracts.Api;
using Meridian.Contracts.Operations;
using Meridian.Contracts.Workstation;
using Meridian.Ui.Services;
using Meridian.Wpf.Services;

namespace Meridian.Wpf.Tests.Services;

public sealed class WorkstationReconciliationApiClientTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DetailReads_NotFound_ReturnNull(bool latestRunRoute)
    {
        using var apiClientService = CreateApiClientService((_, _) => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("not found")
            }));
        var client = new WorkstationReconciliationApiClient(apiClientService);

        var detail = latestRunRoute
            ? await client.GetLatestRunDetailAsync("run-404")
            : await client.GetRunDetailAsync("reconciliation-404");

        detail.Should().BeNull();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DetailReads_ServerError_ThrowInsteadOfReturningMissing(bool latestRunRoute)
    {
        using var apiClientService = CreateApiClientService((_, _) => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent("server failed")
            }));
        var client = new WorkstationReconciliationApiClient(apiClientService);

        Func<Task> act = latestRunRoute
            ? async () => await client.GetLatestRunDetailAsync("run-500")
            : async () => await client.GetRunDetailAsync("reconciliation-500");

        var failure = await act.Should().ThrowAsync<HttpRequestException>();
        failure.Which.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        failure.Which.Message.Should().Contain("HTTP 500");
    }

    [Fact]
    public async Task GetLatestRunDetailAsync_ConnectionFailure_ThrowsInsteadOfReturningMissing()
    {
        using var apiClientService = CreateApiClientService((_, _) =>
            Task.FromException<HttpResponseMessage>(new HttpRequestException("connection refused")));
        var client = new WorkstationReconciliationApiClient(apiClientService);

        Func<Task> act = async () => await client.GetLatestRunDetailAsync("run-offline");

        var failure = await act.Should().ThrowAsync<HttpRequestException>();
        failure.Which.Message.Should().Contain("connection was unavailable");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SupportingReads_ServerError_ThrowInsteadOfReturningEmpty(bool calibrationRead)
    {
        using var apiClientService = CreateApiClientService((_, _) => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent("server failed")
            }));
        var client = new WorkstationReconciliationApiClient(apiClientService);

        Func<Task> act = calibrationRead
            ? async () => await client.GetCalibrationSummaryAsync()
            : async () => await client.GetBreakQueueAsync();

        var failure = await act.Should().ThrowAsync<HttpRequestException>();
        failure.Which.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        failure.Which.Message.Should().Contain("HTTP 500");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SupportingReads_ConnectionFailure_ThrowInsteadOfReturningEmpty(bool calibrationRead)
    {
        using var apiClientService = CreateApiClientService((_, _) =>
            Task.FromException<HttpResponseMessage>(new HttpRequestException("connection refused")));
        var client = new WorkstationReconciliationApiClient(apiClientService);

        Func<Task> act = calibrationRead
            ? async () => await client.GetCalibrationSummaryAsync()
            : async () => await client.GetBreakQueueAsync();

        var failure = await act.Should().ThrowAsync<HttpRequestException>();
        failure.Which.Message.Should().Contain("connection was unavailable");
    }

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

    private static ApiClientService CreateApiClientService(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder)
        => new(new StubHttpClientFactory(new StubHttpMessageHandler(responder)));

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
            => new(handler, disposeHandler: false);
    }

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => responder(request, cancellationToken);
    }
}
#endif

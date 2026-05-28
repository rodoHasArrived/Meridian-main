using System.Net;
using System.Text;
using FluentAssertions;
using Meridian.Contracts.Api;
using Meridian.Contracts.Workstation;

namespace Meridian.Tests.Contracts.Api;

public sealed class UiApiClientTests
{
    [Fact]
    public async Task GetOptionsTrackedUnderlyingsAsync_ParsesWrappedResponse()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler(
            HttpStatusCode.OK,
            """
            {
              "underlyings": ["AAPL", "SPY"],
              "count": 2,
              "timestamp": "2026-04-07T00:00:00Z"
            }
            """));
        var sut = new UiApiClient(httpClient, "http://localhost:8080");

        var underlyings = await sut.GetOptionsTrackedUnderlyingsAsync();

        underlyings.Should().Equal("AAPL", "SPY");
    }

    [Fact]
    public async Task GetOperationsContinuityWorkflowAsync_UsesWorkflowIdRoute()
    {
        using var handler = new RecordingStubHttpMessageHandler(
            _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    {
                      "workflowId": "e8b5f5d2-75fd-4c69-94ca-22a88e11fc10",
                      "fundAccountId": "6fcb8e88-c334-4bd0-8b8c-33f006f39cc3",
                      "periodId": "2026-04",
                      "securityMasterSnapshotId": null,
                      "brokerSource": "custodian",
                      "createdAtUtc": "2026-04-25T00:00:00Z",
                      "updatedAtUtc": "2026-04-25T00:05:00Z",
                      "version": 1,
                      "status": "ApprovalPending",
                      "brokerIntakeState": "Complete",
                      "securityMasterState": "Complete",
                      "ledgerPostingState": "Complete",
                      "reconciliationState": "Cleared",
                      "approvalState": "Submitted",
                      "gates": [],
                      "timeline": [],
                      "breakCases": [],
                      "ledgerPreview": null,
                      "approvals": [],
                      "reportPackReadiness": { "isReady": true, "reportPackId": "rp-001", "blockingReason": null, "evidenceLinks": [] },
                      "evidenceLinks": [],
                      "blockers": [],
                      "nextActions": []
                    }
                    """,
                    Encoding.UTF8,
                    "application/json")
            });
        using var httpClient = new HttpClient(handler);
        var sut = new UiApiClient(httpClient, "http://localhost:8080");
        var workflowId = Guid.Parse("e8b5f5d2-75fd-4c69-94ca-22a88e11fc10");

        var workflow = await sut.GetOperationsContinuityWorkflowAsync(workflowId);

        workflow.Should().NotBeNull();
        handler.LastRequestUri!.AbsolutePath.Should().Be($"/api/workstation/operations/continuity/{workflowId}");
    }

    [Fact]
    public async Task GetOperationsContinuityWorkflowsAsync_WithFilters_AppendsQuery()
    {
        using var handler = new RecordingStubHttpMessageHandler(
            _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[]", Encoding.UTF8, "application/json")
            });
        using var httpClient = new HttpClient(handler);
        var sut = new UiApiClient(httpClient, "http://localhost:8080");
        var fundAccountId = Guid.Parse("6fcb8e88-c334-4bd0-8b8c-33f006f39cc3");

        var workflows = await sut.GetOperationsContinuityWorkflowsAsync(
            fundAccountId: fundAccountId,
            periodId: "2026-04",
            status: Meridian.Contracts.Workstation.OperationsWorkflowStatusDto.ApprovalPending);

        workflows.Should().NotBeNull();
        handler.LastRequestUri.Should().NotBeNull();
        handler.LastRequestUri!.AbsolutePath.Should().Be("/api/workstation/operations/continuity");
        handler.LastRequestUri.Query.Should().Contain("fundAccountId=6fcb8e88-c334-4bd0-8b8c-33f006f39cc3");
        handler.LastRequestUri.Query.Should().Contain("periodId=2026-04");
        handler.LastRequestUri.Query.Should().Contain("status=ApprovalPending");
    }

    [Fact]
    public async Task ReconciliationApiMethods_UseSharedRouteConstants()
    {
        using var handler = new RecordingStubHttpMessageHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/review", StringComparison.Ordinal))
            {
                return JsonResponse("""{"breakId":"break-1","runId":"run-1","strategyName":"Carry","category":0,"status":1,"variance":10,"reason":"review","assignedTo":"ops","detectedAt":"2026-05-27T12:00:00Z","lastUpdatedAt":"2026-05-27T12:01:00Z"}""");
            }

            if (path.EndsWith("/resolve", StringComparison.Ordinal))
            {
                return JsonResponse("""{"breakId":"break-1","runId":"run-1","strategyName":"Carry","category":0,"status":2,"variance":10,"reason":"resolved","assignedTo":"ops","detectedAt":"2026-05-27T12:00:00Z","lastUpdatedAt":"2026-05-27T12:02:00Z"}""");
            }

            if (path.EndsWith("/calibration-summary", StringComparison.Ordinal))
            {
                return JsonResponse("""{"asOf":"2026-05-27T12:00:00Z","status":"Ready","summary":"ready","totalBreakCount":0,"activeBreakCount":0,"openBreakCount":0,"inReviewBreakCount":0,"resolvedBreakCount":0,"dismissedBreakCount":0,"criticalOpenBreakCount":0,"pendingSignoffCount":0,"signedOffCount":0,"missingCalibrationMetadataCount":0,"profiles":[]}""");
            }

            if (path.EndsWith("/bulk/dry-run", StringComparison.Ordinal) || path.EndsWith("/bulk/execute", StringComparison.Ordinal) || path.EndsWith("/bulk/bulk-1/result", StringComparison.Ordinal))
            {
                return JsonResponse("""{"bulkActionId":"bulk-1","idempotencyKey":"idem-1","dryRun":true,"requestedCount":1,"succeededCount":1,"failedCount":0,"results":[]}""");
            }

            return JsonResponse("""{"summary":{"reconciliationRunId":"recon-1","runId":"run-1","createdAt":"2026-05-27T12:00:00Z","portfolioAsOf":null,"ledgerAsOf":null,"matchCount":0,"breakCount":0,"openBreakCount":0,"hasTimingDrift":false,"amountTolerance":0.01,"maxAsOfDriftMinutes":5,"securityIssueCount":0,"hasSecurityCoverageIssues":false},"matches":[],"breaks":[],"securityCoverageIssues":[]}""");
        });
        using var httpClient = new HttpClient(handler);
        var sut = new UiApiClient(httpClient, "http://localhost:8080");

        (await sut.GetReconciliationCalibrationSummaryAsync()).Should().NotBeNull();
        handler.RequestPaths.Should().Contain("/api/workstation/reconciliation/calibration-summary");

        (await sut.GetLatestRunReconciliationAsync("run-1")).Should().NotBeNull();
        handler.RequestPaths.Should().Contain("/api/workstation/runs/run-1/reconciliation");

        (await sut.GetReconciliationRunAsync("recon-1")).Should().NotBeNull();
        handler.RequestPaths.Should().Contain("/api/workstation/reconciliation/runs/recon-1");

        var review = await sut.ReviewReconciliationBreakAsync(
            "break-1",
            new ReviewReconciliationBreakRequest("break-1", "ops", "ops", "review"));
        review.Success.Should().BeTrue();
        handler.RequestPaths.Should().Contain("/api/workstation/reconciliation/break-queue/break-1/review");

        var resolve = await sut.ResolveReconciliationBreakAsync(
            "break-1",
            new ResolveReconciliationBreakRequest("break-1", ReconciliationBreakQueueStatus.Resolved, "ops", "done", "evidence"));
        resolve.Success.Should().BeTrue();
        handler.RequestPaths.Should().Contain("/api/workstation/reconciliation/break-queue/break-1/resolve");

        var bulkRequest = new ReconciliationBulkCaseworkRequest(
            BreakIds: ["break-1"],
            Action: ReconciliationCaseworkAction.ChangePriority,
            Actor: "ops",
            CommandId: "bulk-1",
            CorrelationId: "corr-1",
            Source: "unit-test",
            IdempotencyKey: "idem-1",
            DryRun: true,
            AllowPartialSuccess: true,
            Priority: ReconciliationCasePriority.High);
        (await sut.DryRunReconciliationBulkCaseworkAsync(bulkRequest)).Success.Should().BeTrue();
        handler.RequestPaths.Should().Contain("/api/workstation/reconciliation/break-queue/bulk/dry-run");

        (await sut.ExecuteReconciliationBulkCaseworkAsync(bulkRequest with { DryRun = false })).Success.Should().BeTrue();
        handler.RequestPaths.Should().Contain("/api/workstation/reconciliation/break-queue/bulk/execute");

        (await sut.GetReconciliationBulkCaseworkResultAsync("bulk-1")).Should().NotBeNull();
        handler.RequestPaths.Should().Contain("/api/workstation/reconciliation/break-queue/bulk/bulk-1/result");
    }

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly string _content;

        public StubHttpMessageHandler(HttpStatusCode statusCode, string content)
        {
            _statusCode = statusCode;
            _content = content;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_content, Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class RecordingStubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responseFactory;

        public RecordingStubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        {
            _responseFactory = responseFactory;
        }

        public Uri? LastRequestUri { get; private set; }
        public List<string> RequestPaths { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastRequestUri = request.RequestUri;
            RequestPaths.Add(request.RequestUri!.AbsolutePath);
            return Task.FromResult(_responseFactory(request));
        }
    }
}

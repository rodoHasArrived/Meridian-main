using System.Net;
using System.Text;
using FluentAssertions;
using Meridian.Contracts.Api;

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

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastRequestUri = request.RequestUri;
            return Task.FromResult(_responseFactory(request));
        }
    }
}

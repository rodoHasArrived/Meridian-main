using System.Net;
using System.Text;
using System.Text.Json;
using Meridian.Contracts.Workstation;
using Meridian.Storage.SecurityMaster;
using Meridian.Ui.Services;
using Meridian.Ui.Services.Services.SecurityMaster;
using Meridian.Ui.Shared.Endpoints;
using AppSecurityMaster = Meridian.Application.SecurityMaster;

namespace Meridian.Ui.Tests.Services;

public sealed class SecurityMasterReviewQueueApiClientTests
{
    private static readonly Guid SecurityId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid RevisionId = Guid.Parse("d1111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task PendingRevisionsAskForTheNamedStatesAndReadTheSharedRows()
    {
        using var api = new ApiClientService(new Factory((request, _) =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("/api/security-master/workbench/revisions", request.RequestUri!.AbsolutePath);
            Assert.Equal("?state=Draft%2CSubmitted%2CApproved", request.RequestUri.Query);
            return Json(HttpStatusCode.OK, $$"""
                [{"revisionId":"{{RevisionId:D}}","securityId":"{{SecurityId:D}}","state":"Submitted","actor":"alice.ops",
                  "createdAt":"2026-11-20T09:00:00+00:00","updatedAt":"2026-11-21T09:00:00+00:00","fieldPath":"EconomicDefinition.Coupon",
                  "fieldValue":"5.25","justification":"Prospectus","fundProfileId":null,"baseVersion":7,"ageHours":49.5}]
                """);
        }));

        var response = await new SecurityMasterReviewQueueApiClient(api).ListPendingRevisionsAsync(SecurityMasterReviewQueuePresentation.InboxStates);

        Assert.True(response.Success, response.ErrorMessage);
        var row = Assert.Single(response.Data!);
        Assert.Equal(SecurityMasterRevisionStateDto.Submitted, row.State);
        Assert.Equal("alice.ops", row.Actor);
        Assert.Equal(7, row.BaseVersion);
        Assert.Equal(49.5, row.AgeHours);
    }

    [Fact]
    public async Task DiscardRevisionPostsToTheSecurityRouteWithoutAnActorAndRefusesAnotherRevision()
    {
        var echoed = RevisionId;
        using var api = new ApiClientService(new Factory(async (request, ct) =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal($"/api/security-master/{SecurityId:D}/workbench/discard", request.RequestUri!.AbsolutePath);
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            Assert.Equal(RevisionId, body.RootElement.GetProperty("revisionId").GetGuid());
            Assert.Equal(string.Empty, body.RootElement.GetProperty("actor").GetString());
            Assert.Equal(echoed == RevisionId ? "Discarded from the review inbox." : null, body.RootElement.GetProperty("reason").GetString());
            return await Json(HttpStatusCode.OK, EditResultJson(echoed));
        }));
        var client = new SecurityMasterReviewQueueApiClient(api);

        var discarded = await client.DiscardRevisionAsync(SecurityId, RevisionId, " Discarded from the review inbox. ");
        Assert.True(discarded.Success, discarded.ErrorMessage);
        Assert.Equal(SecurityMasterRevisionStateDto.Rejected, discarded.Data!.State);

        echoed = Guid.NewGuid();
        var mismatched = await client.DiscardRevisionAsync(SecurityId, RevisionId, null);
        Assert.False(mismatched.Success);
        Assert.Equal(502, mismatched.StatusCode);
    }

    [Fact]
    public async Task HeldAmendmentsFilterBySecurityAndStatus_AndDiscardAcceptsNoContent()
    {
        var heldId = Guid.Parse("b1111111-1111-1111-1111-111111111111");
        using var api = new ApiClientService(new Factory((request, _) =>
        {
            if (request.Method == HttpMethod.Get)
            {
                Assert.Equal("/api/security-master/held-amendments", request.RequestUri!.AbsolutePath);
                Assert.Equal($"?securityId={SecurityId:D}&status=Held", request.RequestUri.Query);
                return Json(HttpStatusCode.OK, $$"""
                    [{"heldAmendmentId":"{{heldId:D}}","securityId":"{{SecurityId:D}}","sourceSystem":"vendor-b","requestJson":"{}",
                      "dominatedFieldPaths":["EconomicDefinition.Coupon"],"conflictIds":["{{Guid.NewGuid():D}}"],"heldAtVersion":7,
                      "heldAt":"2026-11-24T09:30:00+00:00","status":"Held","closedBy":null,"closedAt":null}]
                    """);
            }

            Assert.Equal($"/api/security-master/held-amendments/{heldId:D}/discard", request.RequestUri!.AbsolutePath);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        }));
        var client = new SecurityMasterReviewQueueApiClient(api);

        var rows = await client.ListHeldAmendmentsAsync(SecurityId, "Held");
        Assert.True(rows.Success, rows.ErrorMessage);
        Assert.Equal("vendor-b", Assert.Single(rows.Data!).SourceSystem);

        var discarded = await client.DiscardHeldAmendmentAsync(heldId);
        Assert.True(discarded.Success, discarded.ErrorMessage);
        Assert.Equal(204, discarded.Data!.StatusCode);
    }

    [Fact]
    public async Task ExceptionCasesListSyncAndAssignUseTheSharedRoutes()
    {
        var caseId = Guid.Parse("c1111111-1111-1111-1111-111111111111");
        var calls = new List<string>();
        using var api = new ApiClientService(new Factory(async (request, ct) =>
        {
            calls.Add($"{request.Method} {request.RequestUri!.PathAndQuery}");
            return request.RequestUri.AbsolutePath switch
            {
                "/api/security-master/exception-cases" => await Json(HttpStatusCode.OK, $$"""
                    [{"row":{"caseId":"{{caseId:D}}","caseKey":"k","category":"Conflict","ruleCode":"SM-CONFLICT","securityId":null,
                      "severity":"High","title":"t","detail":"d","status":"Open","assignee":null,"openedAt":"2026-11-23T06:00:00+00:00",
                      "lastSeenAt":"2026-11-24T06:00:00+00:00","slaDueAt":"2026-11-24T12:00:00+00:00","closedAt":null,"closedReason":null},
                      "slaBreached":true,"ageHours":30.2}]
                    """),
                "/api/security-master/exception-cases/sync" => await Json(HttpStatusCode.OK, """{"opened":1,"refreshed":2,"closed":3}"""),
                _ => await AssignAsync(request, ct)
            };
        }));
        var client = new SecurityMasterReviewQueueApiClient(api);

        var cases = await client.ListExceptionCasesAsync("Open");
        var item = Assert.Single(cases.Data!);
        Assert.True(item.SlaBreached);
        Assert.Null(item.Row.SecurityId);
        Assert.Equal(new SecurityMasterExceptionSyncResultDto(1, 2, 3), (await client.SyncExceptionCasesAsync()).Data);
        Assert.True((await client.AssignExceptionCaseAsync(caseId, " carol.ops ")).Success);
        Assert.Equal(
            ["GET /api/security-master/exception-cases?status=Open", "POST /api/security-master/exception-cases/sync",
             $"POST /api/security-master/exception-cases/{caseId:D}/assign"],
            calls);

        static async Task<HttpResponseMessage> AssignAsync(HttpRequestMessage request, CancellationToken ct)
        {
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            Assert.Equal("carol.ops", body.RootElement.GetProperty("assignee").GetString());
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        }
    }

    [Fact]
    public async Task QueueFailuresKeepTheStatusAndServerBody()
    {
        using var api = new ApiClientService(new Factory((_, _) => Json(HttpStatusCode.Conflict, """{"error":"Case 'x' is not open."}""")));

        var response = await new SecurityMasterReviewQueueApiClient(api).AssignExceptionCaseAsync(Guid.NewGuid(), "carol.ops");

        Assert.False(response.Success);
        Assert.Equal(409, response.StatusCode);
        Assert.Equal("Case 'x' is not open.", SecurityMasterWorkbenchFailure.Parse(response.StatusCode, response.ErrorMessage).DescribeQueueAction("assign cases"));
    }

    [Fact]
    public void ServerProjectionRoundTripsIntoTheSharedContracts()
    {
        var web = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var conflictId = Guid.NewGuid();
        var held = new AppSecurityMaster.SecurityMasterHeldAmendment(Guid.NewGuid(), SecurityId, "vendor-b", "{\"a\":1}",
            ["EconomicDefinition.Coupon"], [conflictId], 7, new DateTimeOffset(2026, 11, 24, 9, 30, 0, TimeSpan.Zero), "Held");
        var caseRow = new SecurityMasterExceptionCase(Guid.NewGuid(), "k", "Conflict", "SM-CONFLICT", SecurityId, "High", "t", "d", "Open", null,
            new DateTimeOffset(2026, 11, 23, 6, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 11, 24, 6, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 11, 24, 12, 0, 0, TimeSpan.Zero), null, null);
        var asOf = new DateTimeOffset(2026, 11, 24, 12, 15, 0, TimeSpan.Zero);

        // The previous wire shapes (the Application record and the anonymous {row, slaBreached, ageHours} wrapper)
        // must read identically through the shared contracts.
        var legacyHeld = JsonSerializer.Serialize(held, web);
        var legacyCase = JsonSerializer.Serialize(new { row = caseRow, slaBreached = caseRow.IsBreached(asOf), ageHours = Math.Round((asOf - caseRow.OpenedAt).TotalHours, 1) }, web);

        var heldDto = JsonSerializer.Deserialize<SecurityMasterHeldAmendmentDto>(legacyHeld, web)!;
        Assert.Equal(held.HeldAmendmentId, heldDto.HeldAmendmentId);
        Assert.Equal("vendor-b", heldDto.SourceSystem);
        Assert.Equal(["EconomicDefinition.Coupon"], heldDto.DominatedFieldPaths);
        Assert.Equal([conflictId], heldDto.ConflictIds);
        Assert.Equal(held.HeldAt, heldDto.HeldAt);
        Assert.Equal(legacyHeld, JsonSerializer.Serialize(SecurityMasterReviewQueueProjection.ToDto(held), web));

        var item = SecurityMasterReviewQueueProjection.ToQueueItem(caseRow, asOf);
        Assert.True(item.SlaBreached);
        Assert.Equal(30.2, item.AgeHours);
        Assert.Equal(legacyCase, JsonSerializer.Serialize(item, web));
    }

    [Theory]
    [InlineData(0.2, "under 1h")]
    [InlineData(1.5, "2h")]
    [InlineData(47.4, "47h")]
    [InlineData(48, "2d")]
    [InlineData(84, "4d")]
    public void AgesMatchTheBrowserInbox(double hours, string expected)
        => Assert.Equal(expected, SecurityMasterReviewQueuePresentation.FormatAge(hours));

    [Theory]
    [InlineData(SecurityMasterRevisionStateDto.Draft, true)]
    [InlineData(SecurityMasterRevisionStateDto.Submitted, true)]
    [InlineData(SecurityMasterRevisionStateDto.Approved, false)]
    [InlineData(SecurityMasterRevisionStateDto.Published, false)]
    [InlineData(SecurityMasterRevisionStateDto.Rejected, false)]
    public void DiscardEligibilityMatchesTheBrowserInbox(SecurityMasterRevisionStateDto state, bool expected)
        => Assert.Equal(expected, SecurityMasterReviewQueuePresentation.CanDiscard(state));

    [Fact]
    public void FieldAlreadyStagedNamesTheFirstStager()
    {
        var failure = SecurityMasterWorkbenchFailure.Parse(409, $$"""
            {"error":"field-already-staged","message":"m","fieldPath":"EconomicDefinition.Coupon","revisionId":"{{RevisionId:D}}","stagedBy":"alice.ops","state":"Draft"}
            """);

        Assert.True(failure.IsFieldAlreadyStaged);
        Assert.Equal(RevisionId, failure.StagedRevisionId);
        Assert.Equal("alice.ops already has an open revision on EconomicDefinition.Coupon. Publish or discard it before staging another value.",
            failure.DescribeWorkbenchWrite());

        var anonymous = SecurityMasterWorkbenchFailure.Parse(409, """{"error":"field-already-staged","message":"Another revision stages this field."}""");
        Assert.Equal("Another revision stages this field.", anonymous.DescribeWorkbenchWrite());
    }

    [Fact]
    public void WorkbenchMessagesKeepTheExistingClassification()
    {
        Assert.Contains("v9", SecurityMasterWorkbenchFailure.Parse(409, """{"error":"version-conflict","currentVersion":9}""").DescribeWorkbenchWrite());
        Assert.Equal("not approved", SecurityMasterWorkbenchFailure.Parse(409, """{"error":"revision-state-conflict","message":"not approved"}""").DescribeWorkbenchWrite());
        Assert.Contains("approval workflow", SecurityMasterWorkbenchFailure.Parse(422, """{"error":"workflow-required"}""").DescribeWorkbenchWrite());
        Assert.Contains("permission", SecurityMasterWorkbenchFailure.Parse(403, null).DescribeWorkbenchWrite());
        Assert.Equal("An assignee is required.", SecurityMasterWorkbenchFailure.Parse(400, """{"title":"Bad","detail":"An assignee is required."}""").DescribeQueueAction("assign"));
        Assert.Equal("plain text", SecurityMasterWorkbenchFailure.Parse(500, "plain text").DescribeWorkbenchWrite());
    }

    [Fact]
    public void SlaCellShowsTheServerVerdict()
    {
        var row = new SecurityMasterExceptionCaseDto(Guid.NewGuid(), "k", "Conflict", "R", null, "High", "t", "d", "Open", null,
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, new DateTimeOffset(2026, 11, 24, 12, 0, 0, TimeSpan.FromHours(-5)), null, null);

        Assert.Equal("Breached (due 2026-11-24 17:00 UTC)", SecurityMasterReviewQueuePresentation.DescribeSla(new(row, true, 1)));
        Assert.Equal("Due 2026-11-24 17:00 UTC", SecurityMasterReviewQueuePresentation.DescribeSla(new(row, false, 1)));
    }

    private static string EditResultJson(Guid revisionId) => $$$"""
        {"securityId":"{{{SecurityId:D}}}","revisionId":"{{{revisionId:D}}}","newVersion":7,"state":"Rejected",
         "changeEntry":{"changeId":"c","streamVersion":7,"eventType":"RevisionDiscarded","changedAtUtc":"2026-11-25T09:00:00+00:00",
           "effectiveAtUtc":"2026-11-25T09:00:00+00:00","actor":"alice.ops","origin":"Operator","sourceSystem":"operator","sourceRecordId":null,
           "reason":"r","summary":"s","changedFields":[],"changedFieldsSummary":""}}
        """;

    private static Task<HttpResponseMessage> Json(HttpStatusCode status, string body)
        => Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });

    private sealed class Factory(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new Handler(responder));
    }

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => responder(request, cancellationToken);
    }
}

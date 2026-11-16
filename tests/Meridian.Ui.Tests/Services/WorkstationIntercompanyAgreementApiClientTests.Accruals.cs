using System.Net;
using System.Text.Json;
using Meridian.Contracts.Ledger;
using Meridian.Ui.Services;
using Meridian.Ui.Services.Services.Accounting;

namespace Meridian.Ui.Tests.Services;

public sealed partial class WorkstationIntercompanyAgreementApiClientTests
{
    private static readonly IntercompanyAccrualRequestDto AccrualRequest = new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new(2026, 6, 1), new(2026, 7, 1));

    [Fact]
    public async Task AccrualPreviewPostsSelectionsOnlyAndKeepsExactDecimals()
    {
        using var api = new ApiClientService(new Factory(async (request, ct) =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/api/ledger/intercompany/accruals/preview", request.RequestUri!.AbsolutePath);
            var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct)).RootElement;
            Assert.Equal(["sourceBookId", "interestArrangementId", "principalArrangementId", "accrualFrom", "accrualTo"], body.EnumerateObject().Select(x => x.Name));
            Assert.Equal("2026-07-01", body.GetProperty("accrualTo").GetString());
            return Response(IntercompanyInterestAccrualWorkspaceTests.Ready(AccrualRequest));
        }));
        var response = await new WorkstationIntercompanyAgreementApiClient(api).PreviewInterestAccrualAsync(AccrualRequest);
        Assert.True(response.Success, response.ErrorMessage);
        Assert.Equal(4166.666666666666666666666667m, Assert.Single(response.Data!.Computation.Slices).UnroundedAmount);
        Assert.True(response.Data.IsReady);
    }

    [Fact]
    public async Task AccrualPreviewForAnotherWindowOrBookIsRefused()
    {
        foreach (var other in new[] { AccrualRequest with { AccrualTo = new(2026, 6, 16) }, AccrualRequest with { SourceBookId = Guid.NewGuid() },
            AccrualRequest with { PrincipalArrangementId = Guid.NewGuid() } })
        {
            using var api = new ApiClientService(new Factory((_, _) => Task.FromResult(Response(IntercompanyInterestAccrualWorkspaceTests.Ready(other)))));
            var response = await new WorkstationIntercompanyAgreementApiClient(api).PreviewInterestAccrualAsync(AccrualRequest);
            Assert.False(response.Success);
            Assert.Equal(502, response.StatusCode);
        }
    }

    [Fact]
    public async Task AccrualDraftSendsThePreviewedDigestAndRefusesADraftOfAnotherComputation()
    {
        var digest = new string('d', 64);
        var sent = new List<string?>();
        using var api = new ApiClientService(new Factory(async (request, ct) =>
        {
            Assert.Equal("/api/ledger/intercompany/accruals/drafts", request.RequestUri!.AbsolutePath);
            var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct)).RootElement;
            sent.Add(body.GetProperty("expectedEvidenceDigest").GetString());
            Assert.Equal(AccrualRequest.SourceBookId, body.GetProperty("accrual").GetProperty("sourceBookId").GetGuid());
            Assert.False(body.TryGetProperty("actor", out _));
            return Response(IntercompanyInterestAccrualWorkspaceTests.Draft(AccrualRequest.SourceBookId, 1));
        }));
        var client = new WorkstationIntercompanyAgreementApiClient(api);
        var retained = await client.CreateInterestAccrualDraftAsync(new(AccrualRequest, digest));
        var other = await client.CreateInterestAccrualDraftAsync(new(AccrualRequest, new string('e', 64)));
        Assert.Equal([digest, new string('e', 64)], sent);
        Assert.True(retained.Success, retained.ErrorMessage);
        Assert.Equal(1, retained.Data!.Version);
        Assert.False(other.Success);
        Assert.Equal(502, other.StatusCode);
    }

    [Fact]
    public async Task AccrualDraftConflictSurfacesTheServerRefusal()
    {
        using var api = new ApiClientService(new Factory((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Conflict) { Content = new StringContent("Inputs changed") })));
        var response = await new WorkstationIntercompanyAgreementApiClient(api).CreateInterestAccrualDraftAsync(new(AccrualRequest, new string('d', 64)));
        Assert.False(response.Success);
        Assert.Equal(409, response.StatusCode);
    }
}

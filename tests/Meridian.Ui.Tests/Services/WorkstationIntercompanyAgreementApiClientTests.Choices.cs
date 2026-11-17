using System.Net;
using System.Text;
using Meridian.Ui.Services;
using Meridian.Ui.Services.Services.Accounting;

namespace Meridian.Ui.Tests.Services;

public sealed partial class WorkstationIntercompanyAgreementApiClientTests
{
    [Fact]
    public async Task LedgerBooksAreReadFromTheSharedBookRouteTheBrowserUses()
    {
        var book = Guid.NewGuid();
        var entity = Guid.NewGuid();
        var json = $$"""
            [{"ledgerBookId":"{{book:D}}","fundProfileId":"lender-fund","fundStructureNodeId":"{{entity:D}}","fundStructureNodeKind":"Entity",
            "displayName":"Lender book","baseCurrency":"USD","createdAt":"2026-05-01T00:00:00+00:00","updatedAt":"2026-05-01T00:00:00+00:00"}]
            """;
        using var api = new ApiClientService(new Factory((request, _) =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("/api/ledger/books", request.RequestUri!.AbsolutePath);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
        }));

        var response = await new WorkstationIntercompanyAgreementApiClient(api).GetLedgerBooksAsync();

        Assert.True(response.Success, response.ErrorMessage);
        var read = Assert.Single(response.Data!);
        Assert.Equal((book, entity, "Lender book"), (read.LedgerBookId, read.FundStructureNodeId, read.DisplayName));
    }

    [Fact]
    public async Task LedgerBookRefusalIsReturnedRatherThanAnEmptyList()
    {
        using var api = new ApiClientService(new Factory((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = new StringContent("", Encoding.UTF8, "application/json") })));

        var response = await new WorkstationIntercompanyAgreementApiClient(api).GetLedgerBooksAsync();

        Assert.False(response.Success);
        Assert.Null(response.Data);
    }
}

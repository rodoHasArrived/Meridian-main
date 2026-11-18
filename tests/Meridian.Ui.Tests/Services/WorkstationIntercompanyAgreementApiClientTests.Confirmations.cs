using System.Net;
using System.Text;
using Meridian.Contracts.Ledger;
using Meridian.Ui.Services;
using Meridian.Ui.Services.Services.Accounting;

namespace Meridian.Ui.Tests.Services;

public sealed partial class WorkstationIntercompanyAgreementApiClientTests
{
    private static readonly Guid ConfirmationBook = Guid.Parse("a2222222-2222-2222-2222-222222222222");
    private static readonly Guid ConfirmationCounterpartyBook = Guid.Parse("b2222222-2222-2222-2222-222222222222");

    [Fact]
    public async Task ConfirmationSummaryReadsExactDecimalStringsForTheRequestedBookAndDate()
    {
        using var api = new ApiClientService(new Factory((request, _) =>
        {
            Assert.Equal("/api/ledger/intercompany/confirmations", request.RequestUri!.AbsolutePath);
            Assert.Equal($"?ledgerBookId={ConfirmationBook:D}&asOf=2026-06-30", request.RequestUri.Query);
            return ConfirmationResponse(SummaryJson(ConfirmationBook));
        }));

        var response = await new WorkstationIntercompanyAgreementApiClient(api).GetBalanceConfirmationsAsync(ConfirmationBook, new(2026, 6, 30));

        Assert.True(response.Success, response.ErrorMessage);
        var confirmation = Assert.Single(response.Data!.Confirmations);
        Assert.Equal(12_345_678_901_234_567.89m, confirmation.PartyOne.TransactionSignedBalance);
        Assert.Equal(IntercompanyBalanceConfirmationStatusDto.ConfirmedByPartyOne, confirmation.Status);
    }

    [Fact]
    public async Task ConfirmationSummaryForAnotherBookIsRefused()
    {
        using var api = new ApiClientService(new Factory((_, _) => ConfirmationResponse(SummaryJson(Guid.NewGuid()))));

        var response = await new WorkstationIntercompanyAgreementApiClient(api).GetBalanceConfirmationsAsync(ConfirmationBook, new(2026, 6, 30));

        Assert.False(response.Success);
        Assert.Contains("different book or date", response.ErrorMessage);
    }

    [Fact]
    public async Task ConfirmationActionPostsToTheConfirmationAndMustRecordTheRequest()
    {
        var confirmation = Guid.Parse("d1111111-1111-1111-1111-111111111111");
        var request = new IntercompanyBalanceConfirmationActionRequest(Guid.NewGuid(), IntercompanyBalanceConfirmationActionDto.Confirm,
            IntercompanyBalanceConfirmationPartyDto.PartyOne, 1, "digest-1", null);
        var recorded = true;
        using var api = new ApiClientService(new Factory((message, _) =>
        {
            Assert.Equal(HttpMethod.Post, message.Method);
            Assert.Equal($"/api/ledger/intercompany/confirmations/{confirmation:D}/actions", message.RequestUri!.AbsolutePath);
            return ConfirmationResponse(ConfirmationJson(ConfirmationBook, recorded ? request.RequestId : Guid.NewGuid()));
        }));
        var client = new WorkstationIntercompanyAgreementApiClient(api);

        Assert.True((await client.ActOnBalanceConfirmationAsync(confirmation, request)).Success);
        recorded = false;
        Assert.Contains("does not record this request", (await client.ActOnBalanceConfirmationAsync(confirmation, request)).ErrorMessage);
    }

    private static Task<HttpResponseMessage> ConfirmationResponse(string body)
        => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });

    private static string SummaryJson(Guid book)
        => $$"""{"ledgerBookId":"{{book:D}}","asOf":"2026-06-30","confirmations":[{{ConfirmationJson(book, Guid.NewGuid())}}],"findings":[]}""";

    private static string ConfirmationJson(Guid book, Guid requestId) => $$"""
        {"confirmationId":"d1111111-1111-1111-1111-111111111111","tenantId":"tenant-a","companyId":"company-a",
        "accountingArrangementId":"c1111111-1111-1111-1111-111111111111","intercompanyAgreementId":"{{Guid.NewGuid():D}}","asOf":"2026-06-30",
        "version":2,"status":"ConfirmedByPartyOne",
        "partyOne":{"entityId":"{{Guid.NewGuid():D}}","ledgerBookId":"{{book:D}}","transactionCurrency":"USD","transactionSignedBalance":"12345678901234567.89",
          "functionalCurrency":"USD","functionalSignedBalance":"12345678901234567.89","scheduleContentHash":"hash","journalEntryIds":[]},
        "partyTwo":{"entityId":"{{Guid.NewGuid():D}}","ledgerBookId":"{{ConfirmationCounterpartyBook:D}}","transactionCurrency":"USD","transactionSignedBalance":"-12345678901234567.89",
          "functionalCurrency":"USD","functionalSignedBalance":"-12345678901234567.89","scheduleContentHash":"hash","journalEntryIds":[]},
        "netTransactionBalance":"0.00","balanceDigest":"digest-1",
        "events":[{"eventId":"{{Guid.NewGuid():D}}","requestId":"{{requestId:D}}","action":"Confirm","party":"PartyOne","actor":"lender-controller",
          "recordedAt":"2026-07-02T09:00:00+00:00","balanceDigest":"digest-1","reason":null}]}
        """;
}

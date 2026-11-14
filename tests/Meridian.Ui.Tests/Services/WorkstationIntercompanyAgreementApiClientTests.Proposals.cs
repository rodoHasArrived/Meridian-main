using System.Net;
using System.Text;
using Meridian.Contracts.Ledger;
using Meridian.Ui.Services;
using Meridian.Ui.Services.Services.Accounting;

namespace Meridian.Ui.Tests.Services;

public sealed partial class WorkstationIntercompanyAgreementApiClientTests
{
    [Fact]
    public async Task CounterpartProposals_ReadExactServerDecimalsForTheRequestedBook()
    {
        var book = Guid.NewGuid();
        var proposal = Guid.NewGuid();
        // Raw host JSON: a 28-digit amount that a binary floating-point round trip would corrupt.
        var json = $$"""
            [{"source":{"proposalId":"{{proposal:D}}","tenantId":"tenant","companyId":"company","sourceJournalId":"{{Guid.NewGuid():D}}",
            "sourceBookId":"{{book:D}}","sourceEntityId":"{{Guid.NewGuid():D}}","counterpartyEntityId":"{{Guid.NewGuid():D}}",
            "accountingArrangementId":"{{Guid.NewGuid():D}}","accountingArrangementRevisionId":"{{Guid.NewGuid():D}}","transactionId":"TX-1",
            "effectiveDate":"2026-09-30","sourceFunctionalCurrency":"EUR","lines":[{"lineId":"{{Guid.NewGuid():D}}","accountName":"Due from affiliate",
            "accountType":"Asset","chartAccountNodeId":"node","debit":1234567890123456.789012345678,"credit":0,"currency":"EUR",
            "transactionDebit":0.000000000000000000000000001,"transactionCredit":0}],"ruleVersion":"intercompany-mirror-v1",
            "intercompanyAgreementId":"{{Guid.NewGuid():D}}","intercompanyAgreementVersion":3,"intercompanyAgreementPayloadHash":"hash"},
            "sourceDigest":"digest","status":"NeedsFix","attempts":2,"recordedAt":"2026-10-01T00:00:00+00:00","lastError":"blocked"}]
            """;
        using var api = new ApiClientService(new Factory((request, _) =>
        {
            Assert.Equal("/api/ledger/intercompany/proposals", request.RequestUri!.AbsolutePath);
            Assert.Equal($"?sourceBookId={book:D}", request.RequestUri.Query);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
        }));
        var response = await new WorkstationIntercompanyAgreementApiClient(api).GetCounterpartProposalsAsync(book);
        Assert.True(response.Success, response.ErrorMessage);
        var row = new IntercompanyCounterpartProposalRow(Assert.Single(response.Data!));
        Assert.Equal(IntercompanyProposalStatusDto.NeedsFix, row.Status);
        var line = Assert.Single(row.Lines);
        Assert.Equal("1234567890123456.789012345678", line.FunctionalDebit);
        Assert.Equal("0.000000000000000000000000001", line.TransactionDebit);
        Assert.Contains("Approved legal agreement version 3", row.Provenance);
    }

    [Fact]
    public async Task CounterpartProposalsFromAnotherBookAreRefused()
    {
        var source = new IntercompanyProposalSourceDto(Guid.NewGuid(), "tenant", null, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "TX", new(2026, 9, 30), "USD", []);
        using var api = new ApiClientService(new Factory((_, _) => Task.FromResult(Response(new[] { new IntercompanyProposalDto(source, "digest", IntercompanyProposalStatusDto.Pending, 0, DateTimeOffset.UnixEpoch) }))));
        var response = await new WorkstationIntercompanyAgreementApiClient(api).GetCounterpartProposalsAsync(Guid.NewGuid());
        Assert.False(response.Success);
        Assert.Equal(502, response.StatusCode);
    }

    [Fact]
    public async Task CounterpartBreaks_CarryExplicitBookAndCutoff()
    {
        var book = Guid.NewGuid();
        var item = new IntercompanyBreakDto(Guid.NewGuid(), Guid.NewGuid(), "TX", "MissingCounterpart", new(2026, 10, 1), 44, "Contractual due date", "Missing", Guid.NewGuid(), null);
        using var api = new ApiClientService(new Factory((request, _) =>
        {
            Assert.Equal("/api/ledger/intercompany/breaks", request.RequestUri!.AbsolutePath);
            Assert.Equal($"?sourceBookId={book:D}&asOf=2026-11-14", request.RequestUri.Query);
            return Task.FromResult(Response(new[] { item }));
        }));
        var response = await new WorkstationIntercompanyAgreementApiClient(api).GetCounterpartBreaksAsync(book, new(2026, 11, 14));
        Assert.True(response.Success);
        Assert.Equal(item, Assert.Single(response.Data!));
    }

    [Fact]
    public async Task RetryPostsTheReasonedRequestAndRefusesAnotherProposalsReceipt()
    {
        var proposal = Guid.NewGuid();
        var request = new RetryIntercompanyProposalRequest(Guid.NewGuid(), "Period reopened");
        var other = new IntercompanyProposalSourceDto(Guid.NewGuid(), "tenant", null, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "TX", new(2026, 9, 30), "USD", []);
        using var api = new ApiClientService(new Factory(async (message, ct) =>
        {
            Assert.Equal(HttpMethod.Post, message.Method);
            Assert.Equal($"/api/ledger/intercompany/proposals/{proposal:D}/retry", message.RequestUri!.AbsolutePath);
            var body = await message.Content!.ReadAsStringAsync(ct);
            Assert.Contains(request.RequestId.ToString("D"), body);
            Assert.Contains("Period reopened", body);
            return Response(new IntercompanyProposalDto(other, "digest", IntercompanyProposalStatusDto.Pending, 0, DateTimeOffset.UnixEpoch));
        }));
        var response = await new WorkstationIntercompanyAgreementApiClient(api).RetryCounterpartProposalAsync(proposal, request);
        Assert.False(response.Success);
        Assert.Equal(502, response.StatusCode);
    }

    [Fact]
    public async Task LinkIsSuccessfulOnlyWhenTheRequestedTargetIsRetained()
    {
        var proposal = Guid.NewGuid();
        var request = new LinkIntercompanyCounterpartRequest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Booked manually");
        var source = new IntercompanyProposalSourceDto(proposal, "tenant", null, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "TX", new(2026, 9, 30), "USD", []);
        var unlinked = new IntercompanyProposalDto(source, "digest", IntercompanyProposalStatusDto.Pending, 0, DateTimeOffset.UnixEpoch);
        var result = unlinked;
        using var api = new ApiClientService(new Factory((message, _) =>
        {
            Assert.Equal($"/api/ledger/intercompany/proposals/{proposal:D}/link", message.RequestUri!.AbsolutePath);
            return Task.FromResult(Response(result));
        }));
        var client = new WorkstationIntercompanyAgreementApiClient(api);
        Assert.Equal(502, (await client.LinkCounterpartProposalAsync(proposal, request)).StatusCode);
        result = unlinked with { LatestLink = new("tenant", request.RequestId, proposal, request.TargetBookId, Guid.NewGuid(), "controller", request.Reason, DateTimeOffset.UnixEpoch) };
        Assert.Equal(502, (await client.LinkCounterpartProposalAsync(proposal, request)).StatusCode);
        result = unlinked with { LatestLink = new("tenant", request.RequestId, proposal, request.TargetBookId, request.TargetJournalId, "controller", request.Reason, DateTimeOffset.UnixEpoch) };
        var linked = await client.LinkCounterpartProposalAsync(proposal, request);
        Assert.True(linked.Success, linked.ErrorMessage);
        Assert.Equal(request.TargetJournalId, linked.Data!.ResolvedTargetJournalId);
    }

    [Fact]
    public async Task RepricingPreviewKeepsStringEncodedFxDecimalsExact()
    {
        var proposal = Guid.NewGuid();
        var journal = Guid.NewGuid();
        var draft = new ManualJournalEntryDraftDto(journal, ManualJournalEntryStatusDto.Draft, "profile", Guid.NewGuid(), default, new(2026, 9, 30), null, null, null, "USD", "memo", "system",
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, 4,
            [new("line-1", AccountingTemplateLineSideDto.Debit, 123456789012.345678901234m, "USD", "Expense") { TransactionCurrency = new("EUR", 91.000000000000000001m, 1356.668010123456789025m) }], [], [])
        { IntercompanyProposalId = proposal, IntercompanyFx = new(123456789012.345678901234m, "USD", 1356.668010123456789025m, null, false, Guid.NewGuid(), 2, "policy", "1", "hash") };
        using var api = new ApiClientService(new Factory((message, _) =>
        {
            Assert.Equal($"/api/ledger/intercompany/proposals/{proposal:D}/refresh-preview", message.RequestUri!.AbsolutePath);
            return Task.FromResult(Response(new IntercompanyRefreshPreviewDto(draft, "fingerprint", draft with { Version = 4 })));
        }));
        var response = await new WorkstationIntercompanyAgreementApiClient(api).PreviewCounterpartRefreshAsync(proposal);
        Assert.True(response.Success, response.ErrorMessage);
        var line = Assert.Single(response.Data!.Draft.Lines);
        Assert.Equal(123456789012.345678901234m, line.Amount);
        Assert.Equal(91.000000000000000001m, line.TransactionCurrency!.Amount);
        Assert.Equal(1356.668010123456789025m, response.Data.Draft.IntercompanyFx!.Rate);
        Assert.Equal("91.000000000000000001 EUR · rate 1356.668010123456789025", new IntercompanyCounterpartRepricingRow(line, line).OriginalEvidence);
    }

    [Fact]
    public async Task RepricingPreviewForADifferentDraftAndForeignRefreshReceiptAreRefused()
    {
        var proposal = Guid.NewGuid();
        ManualJournalEntryDraftDto Draft(Guid journal) => new(journal, ManualJournalEntryStatusDto.Draft, "profile", Guid.NewGuid(), default, new(2026, 9, 30), null, null, null, "USD", "memo", "system",
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, 4, [], [], [])
        { IntercompanyProposalId = proposal };
        object payload = new IntercompanyRefreshPreviewDto(Draft(Guid.NewGuid()), "fingerprint", Draft(Guid.NewGuid()));
        using var api = new ApiClientService(new Factory((_, _) => Task.FromResult(payload is IntercompanyRefreshPreviewDto preview ? Response(preview) : Response((ManualJournalEntryDraftDto)payload))));
        var client = new WorkstationIntercompanyAgreementApiClient(api);
        Assert.Equal(502, (await client.PreviewCounterpartRefreshAsync(proposal)).StatusCode);
        var request = new RefreshIntercompanyProposalRequest(Guid.NewGuid(), 4, "fingerprint", "New rate");
        payload = Draft(Guid.NewGuid()) with { IntercompanyRefreshRequestId = Guid.NewGuid() };
        Assert.Equal(502, (await client.RefreshCounterpartProposalAsync(proposal, request)).StatusCode);
        payload = Draft(Guid.NewGuid()) with { IntercompanyRefreshRequestId = request.RequestId };
        Assert.True((await client.RefreshCounterpartProposalAsync(proposal, request)).Success);
    }

    [Fact]
    public async Task CapabilitiesAreReadFromTheHostAnswer()
    {
        using var api = new ApiClientService(new Factory((request, _) =>
        {
            Assert.Equal("/api/ledger/intercompany/capabilities", request.RequestUri!.AbsolutePath);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent("""{"canManageAgreements":false,"canManageLifecycle":false,"canManageLedger":true,"canViewLedger":true}""", Encoding.UTF8, "application/json") });
        }));
        var response = await new WorkstationIntercompanyAgreementApiClient(api).GetIntercompanyCapabilitiesAsync();
        Assert.True(response.Success);
        Assert.Equal(new IntercompanyOperationCapabilities(false, false, true, true), response.Data);
    }
}

using System.Text.Json;
using Meridian.Contracts.Ledger;
using Meridian.Ui.Services;
using Meridian.Ui.Services.Services.Accounting;

namespace Meridian.Ui.Tests.Services;

public sealed partial class WorkstationIntercompanyAgreementApiClientTests
{
    [Fact]
    public async Task WorkQueueUsesExplicitScopeAndPreservesExactGuaranteeCap()
    {
        var book = Guid.NewGuid();
        var date = new DateOnly(2026, 11, 23);
        var queue = new IntercompanyAgreementWorkQueueDto(date, 45, DateTimeOffset.UtcNow, "Current", true, "source", [],
            [new(Guid.NewGuid(), "G-1", "Guarantee", 2, "hash", Guid.NewGuid(), "Guarantor", Guid.NewGuid(), "Obligor", "USD", "bank-reference",
                12345678901234567890.12345678m, new(2026, 1, 1), new(2027, 1, 1), null,
                Meridian.Contracts.FundStructure.IntercompanyAgreementStatusDto.Active, null, "Unavailable", "Underlying exposure is not mapped.")], [])
        { LedgerBookId = book, FundProfileId = "fund/a", CanManage = true };
        using var api = new ApiClientService(new Factory((request, _) =>
        {
            Assert.Equal("/api/ledger/intercompany/work-queue", request.RequestUri!.AbsolutePath);
            Assert.Contains($"ledgerBookId={book:D}", request.RequestUri.Query);
            Assert.Contains("fundProfileId=fund%2Fa", request.RequestUri.Query);
            Assert.Contains("asOf=2026-11-23", request.RequestUri.Query);
            Assert.Contains("expiryHorizonDays=45", request.RequestUri.Query);
            return Task.FromResult(Response(queue));
        }));
        var response = await new WorkstationIntercompanyAgreementApiClient(api).GetAgreementWorkQueueAsync(new("fund/a", book, date, 45));
        Assert.True(response.Success);
        Assert.Equal(12345678901234567890.12345678m, Assert.Single(response.Data!.Guarantees).LimitAmount);
        Assert.Null(response.Data.Guarantees[0].CurrentExposure);
    }

    [Fact]
    public async Task WorkQueueRejectsForeignBookEvenWhenResultHasNoCases()
    {
        var date = new DateOnly(2026, 11, 23);
        var queue = new IntercompanyAgreementWorkQueueDto(date, 30, DateTimeOffset.UtcNow, "Current", true, "source", [], [], [])
        { LedgerBookId = Guid.NewGuid() };
        using var api = new ApiClientService(new Factory((_, _) => Task.FromResult(Response(queue))));
        var response = await new WorkstationIntercompanyAgreementApiClient(api).GetAgreementWorkQueueAsync(new(null, Guid.NewGuid(), date));
        Assert.False(response.Success);
        Assert.Equal(502, response.StatusCode);
    }

    [Fact]
    public async Task WorkQueueUpdateSendsExpectedAuthorityAndCannotAcceptAnotherCommandReceipt()
    {
        var book = Guid.NewGuid();
        var date = new DateOnly(2026, 11, 23);
        var command = new IntercompanyAgreementWorkQueueUpdateRequestDto(Guid.NewGuid(), "case-a", IntercompanyAgreementWorkItemActionDto.AssignOwner,
            7, "record-7", "source-current", "Assign follow-up", "operator-b");
        var item = new IntercompanyAgreementWorkItemDto("case-a", book, Guid.NewGuid(), 2, "expiring", "Warning", "Expiry", "Amend", null, date,
            true, true, IntercompanyAgreementWorkItemStateDto.Open, "operator-b", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 0, null,
            "source-current", 8, "record-8", [new(Guid.NewGuid(), 8, "record-8", "AssignOwner", IntercompanyAgreementWorkItemStateDto.Open,
                "actor", DateTimeOffset.UtcNow, "Assign follow-up", "operator-b", null, [], date, "source-current")]);
        using var api = new ApiClientService(new Factory(async (request, ct) =>
        {
            Assert.Equal("/api/ledger/intercompany/work-queue/update", request.RequestUri!.AbsolutePath);
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            Assert.Equal(command.CommandId, json.RootElement.GetProperty("commandId").GetGuid());
            Assert.Equal(7, json.RootElement.GetProperty("expectedVersion").GetInt64());
            Assert.Equal("record-7", json.RootElement.GetProperty("expectedRecordHash").GetString());
            Assert.Equal("source-current", json.RootElement.GetProperty("expectedSourceFingerprint").GetString());
            Assert.False(json.RootElement.TryGetProperty("actor", out _));
            Assert.False(json.RootElement.TryGetProperty("tenantId", out _));
            return Response(item);
        }));
        var result = await new WorkstationIntercompanyAgreementApiClient(api).UpdateAgreementWorkQueueAsync(new(null, book, date), command);
        Assert.False(result.Success);
        Assert.Equal(502, result.StatusCode);
    }
}

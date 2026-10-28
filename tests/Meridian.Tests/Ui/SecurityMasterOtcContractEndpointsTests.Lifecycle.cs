using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Meridian.Contracts.SecurityMaster;
using Meridian.Identity.Auth;
using Microsoft.AspNetCore.TestHost;
using NSubstitute;

namespace Meridian.Tests.Ui;

public sealed partial class SecurityMasterOtcContractEndpointsTests
{
    [Fact]
    public async Task Lifecycle_WriteStampsSessionActor_AndHistoryReadsTheTypedEvent()
    {
        var current = Contract(Guid.NewGuid()) with { Status = OtcContractStatus.Confirmed };
        var store = Substitute.For<IOtcContractStore>();
        store.GetContractAsync(current.ContractId, Arg.Any<CancellationToken>()).Returns(current);
        AppendOtcContractLifecycleRequest? captured = null;
        var request = new AppendOtcContractLifecycleRequest(Guid.NewGuid(), current.ContractId, current.Version,
            Trade, "CONF-1", new OtcRateResetData("receive", "SOFR", Trade, 0m), "untrusted.body.actor");
        var item = OtcContractLifecycleRules.Create(current, request with { Actor = "casey.doyle" }, DateTimeOffset.UtcNow, target: new(current.SecurityId, 1, "Swap", "receive", "SOFR"));
        store.AppendLifecycleEventAsync(Arg.Do<AppendOtcContractLifecycleRequest>(r => captured = r), Arg.Any<CancellationToken>()).Returns(item);
        store.GetLifecycleEventsAsync(current.ContractId, Arg.Any<CancellationToken>()).Returns(new[] { item });
        await using var app = await CreateAppAsync(Substitute.For<ISecurityMasterQueryService>(), store);
        var client = app.GetTestClient();
        using var response = await client.PostAsJsonAsync($"/api/security-master/otc/contracts/{current.ContractId}/lifecycle", request, Json);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        captured!.Actor.Should().Be("casey.doyle");
        var history = await client.GetFromJsonAsync<List<OtcContractLifecycleEventDto>>($"/api/security-master/otc/contracts/{current.ContractId}/lifecycle", Json);
        history.Should().ContainSingle();
        history![0].Data.Should().BeOfType<OtcRateResetData>();
    }

    [Fact]
    public async Task Lifecycle_RefusesUnauthorizedWriteAndConflictingVersion()
    {
        var current = Contract(Guid.NewGuid()) with { Status = OtcContractStatus.Confirmed };
        var request = new AppendOtcContractLifecycleRequest(Guid.NewGuid(), current.ContractId, current.Version,
            Trade, "CONF-1", new OtcRateResetData("receive", "SOFR", Trade, 0m), "body");
        var store = Substitute.For<IOtcContractStore>();
        await using (var denied = await CreateAppAsync(Substitute.For<ISecurityMasterQueryService>(), store, UserPermission.ViewSecurityMaster))
        {
            using var response = await denied.GetTestClient().PostAsJsonAsync($"/api/security-master/otc/contracts/{current.ContractId}/lifecycle", request, Json);
            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            await store.DidNotReceive().AppendLifecycleEventAsync(Arg.Any<AppendOtcContractLifecycleRequest>(), Arg.Any<CancellationToken>());
        }
        store.AppendLifecycleEventAsync(Arg.Any<AppendOtcContractLifecycleRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<OtcContractLifecycleEventDto>(new InvalidOperationException("The contract version changed.")));
        await using var app = await CreateAppAsync(Substitute.For<ISecurityMasterQueryService>(), store);
        using var conflict = await app.GetTestClient().PostAsJsonAsync($"/api/security-master/otc/contracts/{current.ContractId}/lifecycle", request, Json);
        conflict.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }
    [Theory]
    [InlineData("compression", "remainingNotional")]
    [InlineData("rateReset", "indexRate")]
    [InlineData("creditEvent", "creditEvent")]
    [InlineData("auctionSettlement", "finalPricePercent")]
    public async Task Lifecycle_MissingEconomicWireMember_IsBadRequestWithoutAStoreWrite(string type, string member)
    {
        var current = Contract(Guid.NewGuid());
        var store = Substitute.For<IOtcContractStore>();
        var json = JsonSerializer.SerializeToNode(new AppendOtcContractLifecycleRequest(Guid.NewGuid(), current.ContractId,
            1, Trade, "SOURCE", new OtcCompressionData("C-1", 0m)), Json)!;
        var data = type switch
        {
            "compression" => "{\"type\":\"compression\",\"compressionReference\":\"C-1\",\"remainingNotional\":0}",
            "rateReset" => "{\"type\":\"rateReset\",\"legId\":\"receive\",\"indexName\":\"SOFR\",\"fixingDate\":\"2026-03-02\",\"indexRate\":0}",
            "creditEvent" => "{\"type\":\"creditEvent\",\"creditEvent\":\"Bankruptcy\",\"referenceEntity\":\"Co\",\"determinationReference\":\"DC-1\"}",
            _ => "{\"type\":\"auctionSettlement\",\"creditEventId\":\"11111111-1111-1111-1111-111111111111\",\"auctionDate\":\"2026-03-02\",\"finalPricePercent\":0,\"settlementAmount\":1000000,\"currency\":\"USD\",\"settlementScale\":2,\"auctionReference\":\"A-1\"}"
        };
        json["data"] = JsonNode.Parse(data);
        json["data"]!.AsObject().Remove(member).Should().BeTrue();
        await using var app = await CreateAppAsync(Substitute.For<ISecurityMasterQueryService>(), store);
        using var body = new StringContent(json.ToJsonString(), System.Text.Encoding.UTF8, "application/json");
        using var response = await app.GetTestClient().PostAsync($"/api/security-master/otc/contracts/{current.ContractId}/lifecycle", body);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await store.DidNotReceive().AppendLifecycleEventAsync(Arg.Any<AppendOtcContractLifecycleRequest>(), Arg.Any<CancellationToken>());
    }

}

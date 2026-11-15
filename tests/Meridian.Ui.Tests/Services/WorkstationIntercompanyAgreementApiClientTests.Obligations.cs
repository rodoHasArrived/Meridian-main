using System.Net;
using System.Text;
using System.Text.Json;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Ui.Services;
using Meridian.Ui.Services.Services.Accounting;

namespace Meridian.Ui.Tests.Services;

public sealed partial class WorkstationIntercompanyAgreementApiClientTests
{
    [Fact]
    public async Task ObligationMonitor_PreservesExplicitBookDateAndHorizonWithoutRekeyingProfile()
    {
        var book = Guid.NewGuid();
        var date = new DateOnly(2026, 11, 13);
        var result = new IntercompanyObligationMonitorDto(date, 45, DateTimeOffset.UtcNow, "Partial", false, "retained-source", [], [], null, []);
        using var api = new ApiClientService(new Factory((request, _) =>
        {
            Assert.Equal("/api/ledger/intercompany/obligations", request.RequestUri!.AbsolutePath);
            Assert.Contains($"ledgerBookId={book:D}", request.RequestUri.Query);
            Assert.Contains("asOf=2026-11-13", request.RequestUri.Query);
            Assert.Contains("expiryHorizonDays=45", request.RequestUri.Query);
            Assert.DoesNotContain("fundProfileId", request.RequestUri.Query);
            return Task.FromResult(Response(result));
        }));
        var response = await new WorkstationIntercompanyAgreementApiClient(api).GetObligationMonitorAsync(new(null, book, date, 45));
        Assert.True(response.Success);
        Assert.Equal("Partial", response.Data!.Status);
        Assert.False(response.Data.IsComplete);
    }

    [Fact]
    public async Task MonitorRejectsDifferentCutoffRatherThanShowingStaleScopeAsCurrent()
    {
        var date = new DateOnly(2026, 11, 13);
        var result = new IntercompanyObligationMonitorDto(date.AddDays(-1), 30, DateTimeOffset.UtcNow, "Current", true, "source", [], [], null, []);
        using var api = new ApiClientService(new Factory((_, _) => Task.FromResult(Response(result))));
        var response = await new WorkstationIntercompanyAgreementApiClient(api).GetObligationMonitorAsync(new(null, Guid.NewGuid(), date));
        Assert.False(response.Success);
        Assert.Equal(502, response.StatusCode);
    }

    [Fact]
    public async Task DatePreview_UsesExactRetainedProposalIdentity()
    {
        var agreement = Guid.NewGuid();
        var proposal = Guid.NewGuid();
        using var api = new ApiClientService(new Factory((request, _) =>
        {
            Assert.EndsWith($"/{agreement:D}/schedule", request.RequestUri!.AbsolutePath);
            Assert.Contains($"proposalId={proposal:D}", request.RequestUri.Query);
            Assert.DoesNotContain("version=", request.RequestUri.Query);
            return Task.FromResult(Response(new IntercompanyStructuredTermsPreviewDto("Unavailable", "StructuredV1", "subject", "schedule-v1", "Missing", null, [], ["Unknown calendar"])));
        }));
        var response = await new WorkstationIntercompanyAgreementApiClient(api).PreviewStructuredTermsAsync(agreement, new(2026, 11, 13), proposalId: proposal);
        Assert.True(response.Success);
        Assert.Equal("Unavailable", response.Data!.Status);
        Assert.Empty(response.Data.Periods);
    }

    [Fact]
    public async Task ExternalAttestationLeavesAuthenticatedActorToServerAndBindsSubmittedHash()
    {
        var command = new IntercompanyAgreementCommandDto(Guid.NewGuid(), Guid.NewGuid(), 4, Guid.NewGuid(), SubmittedHash: "frozen-hash")
        { ExternalReview = new("External counsel", "File-42", new(2026, 11, 12), new("evidence://evidence-vault/review", "document-2"), "Reviewed frozen terms", "frozen-hash") };
        using var api = new ApiClientService(new Factory(async (request, ct) =>
        {
            using var document = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            var body = document.RootElement;
            Assert.Equal("External counsel", body.GetProperty("externalReview").GetProperty("externalReviewer").GetString());
            Assert.Equal("frozen-hash", body.GetProperty("externalReview").GetProperty("submittedHash").GetString());
            Assert.False(body.TryGetProperty("actor", out _));
            Assert.False(body.TryGetProperty("tenantId", out _));
            return new HttpResponseMessage(HttpStatusCode.Conflict) { Content = new StringContent("Retained source changed", Encoding.UTF8, "text/plain") };
        }));
        var response = await new WorkstationIntercompanyAgreementApiClient(api).ExecuteAsync("approve", command);
        Assert.Equal(409, response.StatusCode);
    }
    [Fact]
    public async Task MonitorExactDecimalStringsRetainAllTwentyEightDigits()
    {
        var book = Guid.NewGuid();
        var date = new DateOnly(2026, 11, 13);
        const decimal amount = 12345678901234567890.12345678m;
        var row = new IntercompanyBalanceScheduleRowDto(book, Guid.NewGuid(), Guid.NewGuid(), null, null, null, null,
            IntercompanyAccountRoleDto.Receivable, "USD", "USD", amount, amount, true, [], []);
        var schedule = new IntercompanyBalanceScheduleDto("tenant", "company", "fund", book, date, true, [row], [], "schedule-hash");
        var result = new IntercompanyObligationMonitorDto(date, 30, DateTimeOffset.UtcNow, "Current", true, "source", [], [], schedule, []);
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new ExactMonitorDecimalConverter());
        var json = JsonSerializer.Serialize(result, options);
        Assert.Contains("\"transactionSignedBalance\":\"12345678901234567890.12345678\"", json);
        Assert.Contains("\"expiryHorizonDays\":30", json);
        using var api = new ApiClientService(new Factory((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        { Content = new StringContent(json, Encoding.UTF8, "application/json") })));
        var response = await new WorkstationIntercompanyAgreementApiClient(api).GetObligationMonitorAsync(new(null, book, date));
        Assert.True(response.Success);
        Assert.Equal(amount, Assert.Single(response.Data!.BalanceSchedule!.Rows).TransactionSignedBalance);
    }

    private sealed class ExactMonitorDecimalConverter : System.Text.Json.Serialization.JsonConverter<decimal>
    {
        public override decimal Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) => reader.GetDecimal();
        public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options)
            => writer.WriteStringValue(value.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }
}

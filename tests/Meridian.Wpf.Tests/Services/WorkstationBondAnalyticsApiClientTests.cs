using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Contracts.FixedIncome;
using Meridian.Ui.Services;
using Meridian.Wpf.Services;

namespace Meridian.Wpf.Tests.Services;

public sealed class WorkstationBondAnalyticsApiClientTests
{
    private static readonly Guid SecurityId = Guid.Parse("5d1e7c3a-9b2f-4e6a-8c1d-3f4a5b6c7d8e");
    private static readonly DateTimeOffset AsOf = new(2026, 2, 15, 0, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(null)]
    [InlineData("bond-analytics-v1")]
    [InlineData("bond-analytics-v2")]
    [InlineData("bond-analytics-v3")]
    [InlineData("unsupported-model")]
    [InlineData("")]
    public async Task Calculate_UsesExistingReadRouteAndPreservesExplicitModelSelection(string? version)
    {
        string? query = null;
        using var api = new ApiClientService(new Factory(new Handler(message =>
        {
            message.Method.Should().Be(HttpMethod.Get);
            message.RequestUri!.AbsolutePath.Should().Be($"/api/reference-data/bonds/{SecurityId:D}/analytics");
            query = Uri.UnescapeDataString(message.RequestUri.Query);
            return Json(Result());
        })));

        await new WorkstationSecurityMasterApiClient(api).GetBondAnalyticsAsync(new BondAnalyticsRequest(
            SecurityId, new BondCalculationContext(AsOf, new DateOnly(2026, 2, 15)), Yield: 0.03m,
            YieldConvention: BondYieldConvention.NominalPeriodic) { CalculationVersion = version });

        query.Should().Contain("asOf=2026-02-15T00:00:00.0000000+00:00")
            .And.Contain("settlementDate=2026-02-15").And.Contain("yield=0.03").And.Contain("yieldConvention=NominalPeriodic");
        if (version is null)
            query.Should().NotContain("calculationVersion=");
        else
            query.Should().Contain("calculationVersion=" + version, "even an unsupported explicit model must reach the server unchanged");
    }

    [Fact]
    public async Task Calculate_DeserializesFloatingEvidenceAndSpreadSensitivityWithoutRounding()
    {
        using var api = new ApiClientService(new Factory(new Handler(_ => Json(Result()))));

        var response = await new WorkstationSecurityMasterApiClient(api).GetBondAnalyticsAsync(new BondAnalyticsRequest(
            SecurityId, new BondCalculationContext(AsOf), Yield: 0.03m) { CalculationVersion = "bond-analytics-v3" });

        response.Success.Should().BeTrue();
        response.Data!.SpreadDv01.Should().Be(0.019512345678m);
        var coupon = response.Data.CashFlows.Should().ContainSingle().Which.FloatingCoupon;
        coupon.Should().NotBeNull();
        coupon!.IsObserved.Should().BeFalse();
        coupon.ResetDate.Should().Be(new DateOnly(2026, 7, 15));
        coupon.IndexRate.Should().Be(0.0212345678901234567890123456m);
        coupon.SpreadBps.Should().Be(100m);
        coupon.Source.Should().Be("synthetic-sofr-forward");
    }

    private static BondAnalyticsResult Result() => new(SecurityId, "USD", "bond-analytics-v3", new BondCalculationContext(AsOf),
        BondYieldConvention.NominalPeriodic, 100m, 100.333333333333m, 0.333333333333m, 0.03m, 0.04m, 1.5m, 1.4m, 3m, 0.014m, 0.03m,
        [new(Guid.NewGuid(), new DateOnly(2027, 1, 15), new DateOnly(2026, 7, 15), new DateOnly(2027, 1, 15), 1.5m, 0m, 0.5m)
        {
            FloatingCoupon = new(new DateOnly(2026, 7, 15), false, "SOFR", 0.0212345678901234567890123456m, 100m,
                0.0312345678901234567890123456m, "synthetic-sofr-forward")
        }], [], [], [], [], []) { SpreadDv01 = 0.019512345678m };

    private static HttpResponseMessage Json<T>(T value) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(value, new JsonSerializerOptions(DesktopJsonOptions.Api)
        { NumberHandling = JsonNumberHandling.WriteAsString }), Encoding.UTF8, "application/json")
    };

    private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(respond(request));
    }
}

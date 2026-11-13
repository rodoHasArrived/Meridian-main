using System.Net;
using System.Net.Http.Headers;
using System.Text;
using FluentAssertions;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.SecurityMaster;
using Meridian.Core.Exceptions;
using Meridian.Infrastructure.Adapters.Fred;
using Meridian.Infrastructure.Http;
using Meridian.Tests.TestHelpers;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Tests.Infrastructure.Providers;

public sealed class FredTypedHttpRetryTests
{
    private const string Observations = """
        {"count":1,"observations":[{"date":"2026-11-10","value":"-0.2","realtime_start":"2026-11-12","realtime_end":"9999-12-31"}]}
        """;
    private const string Metadata = """{"seriess":[{"units":"Percent"}]}""";
    private const string ClientName = "fred-typed-retry-test";

    [Theory]
    [InlineData("economic")]
    [InlineData("rates")]
    public async Task TypedObservationPaths_ChargeEverySendWithoutMultiplyingNamedClientRetries(string path)
    {
        var sends = new Dictionary<string, int>(StringComparer.Ordinal);
        var requests = new List<string>();
        var retryOwnership = new List<bool>();
        using var handler = new StubHttpMessageHandler(request =>
        {
            var endpoint = request.RequestUri!.AbsolutePath;
            sends[endpoint] = sends.GetValueOrDefault(endpoint) + 1;
            requests.Add(request.RequestUri.Query);
            retryOwnership.Add(request.Options.TryGetValue(SharedResiliencePolicies.ProviderOwnsRetries, out var owned) && owned);
            return sends[endpoint] == 1
                ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("transient") }
                : Json(endpoint.EndsWith("/series", StringComparison.Ordinal) ? Metadata : Observations);
        });
        using var services = CreateServices(handler);
        using var client = services.GetRequiredService<IHttpClientFactory>().CreateClient(ClientName);
        using var provider = new FredHistoricalDataProvider("synthetic-key", client);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        if (path == "economic")
        {
            var result = await provider.GetEconomicObservationsAsync(Definition(), new(2026, 11, 10),
                new(2026, 11, 10), new(2026, 11, 12), cancellation.Token);
            var row = result.Observations.Should().ContainSingle().Subject;
            row.Value.Should().Be(-0.2m);
            row.Unit.Should().Be(BondMarketValueUnit.Percent);
            row.SourceVintageDate.Should().Be(new DateOnly(2026, 11, 12));
            result.RawResponseJson.Should().Be(Observations);
            result.ResponseSha256.Should().Be(Sha256Digest.ComputeUtf8(Observations));
            requests.Should().OnlyContain(query => query.Contains("realtime_start=2026-11-12", StringComparison.Ordinal)
                && query.Contains("realtime_end=2026-11-12", StringComparison.Ordinal));
        }
        else
        {
            var rows = await provider.GetRateObservationsAsync("SOFR", new(2026, 11, 10), new(2026, 11, 10), cancellation.Token);
            var row = rows.Should().ContainSingle().Subject;
            row.Value.Should().Be(-0.2m);
            row.OriginalValue.Should().Be("-0.2");
            row.Unit.Should().Be(SecurityRateUnit.Percent);
            row.Kind.Should().Be(SecurityRateObservationKind.ObservedFixing);
            row.PublishedAt.Should().BeNull();
            row.VintageDate.Should().Be(new DateOnly(2026, 11, 12));
            requests.Count(query => query.Contains("units=lin", StringComparison.Ordinal)).Should().Be(2);
        }

        var expectedSends = path == "economic" ? 2 : 4;
        handler.CallCount.Should().Be(expectedSends, "each endpoint has exactly one provider-owned retry");
        provider.GetRateLimitInfo().RequestsMade.Should().Be(expectedSends, "quota is charged for actual network sends, including retries");
        retryOwnership.Should().HaveCount(expectedSends).And.OnlyContain(owned => owned);
        sends.Values.Should().OnlyContain(count => count == 2);
    }

    [Theory]
    [InlineData("economic", false)]
    [InlineData("economic", true)]
    [InlineData("rates", false)]
    [InlineData("rates", true)]
    public async Task TypedObservationPaths_429ReturnsOnceAndRetainsProviderCooldown(string path, bool absoluteRetryDate)
    {
        var retryOwnership = new List<bool>();
        using var handler = new StubHttpMessageHandler(request =>
        {
            retryOwnership.Add(request.Options.TryGetValue(SharedResiliencePolicies.ProviderOwnsRetries, out var owned) && owned);
            if (request.RequestUri!.AbsolutePath.EndsWith("/series", StringComparison.Ordinal))
                return Json(Metadata);
            var limited = new HttpResponseMessage(HttpStatusCode.TooManyRequests) { Content = new StringContent("limited") };
            limited.Headers.RetryAfter = absoluteRetryDate
                ? new RetryConditionHeaderValue(DateTimeOffset.UtcNow.AddSeconds(20))
                : new RetryConditionHeaderValue(TimeSpan.FromSeconds(20));
            return limited;
        });
        using var services = CreateServices(handler);
        using var client = services.GetRequiredService<IHttpClientFactory>().CreateClient(ClientName);
        using var provider = new FredHistoricalDataProvider("synthetic-key", client);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        Func<Task> fetch = path == "economic"
            ? async () => { await provider.GetEconomicObservationsAsync(Definition(), new(2026, 11, 10), new(2026, 11, 10), new(2026, 11, 12), cancellation.Token); }
        : async () => { await provider.GetRateObservationsAsync("SOFR", new(2026, 11, 10), new(2026, 11, 10), cancellation.Token); };
        var failure = await fetch.Should().ThrowAsync<RateLimitException>();

        failure.Which.RetryAfter.Should().NotBeNull();
        failure.Which.RetryAfter!.Value.Should().BeCloseTo(TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(1));
        var expectedSends = path == "economic" ? 1 : 2;
        handler.CallCount.Should().Be(expectedSends, "neither the provider nor its named client retries a quota response");
        var budget = provider.GetRateLimitInfo();
        budget.RequestsMade.Should().Be(expectedSends);
        budget.IsLimited.Should().BeTrue();
        budget.RetryAfter.Should().NotBeNull();
        budget.RetryAfter!.Value.Should().BeCloseTo(TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(1));
        retryOwnership.Should().HaveCount(expectedSends).And.OnlyContain(owned => owned);
    }

    private static FredEconomicSeriesDefinition Definition()
        => new("SOFR", "SOFR", BondMarketObservationKind.Fixing, BondMarketValueUnit.Percent, "Synthetic FRED source", "USD");

    private static ServiceProvider CreateServices(HttpMessageHandler handler)
    {
        var services = new ServiceCollection();
        services.AddHttpClient(ClientName).ConfigurePrimaryHttpMessageHandler(() => handler).AddSharedResiliencePolicy();
        return services.BuildServiceProvider();
    }

    private static HttpResponseMessage Json(string value) => new(HttpStatusCode.OK)
    { Content = new StringContent(value, Encoding.UTF8, "application/json") };
}

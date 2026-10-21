using System.Net;
using FluentAssertions;
using Meridian.Application.Composition;
using Meridian.Application.Composition.Features;
using Meridian.Infrastructure.Http;
using Meridian.Platform.Monitoring;
using Microsoft.Extensions.DependencyInjection;
using Polly;
using Polly.CircuitBreaker;
using Xunit;

namespace Meridian.Tests.Application.Composition;

public sealed class HttpClientFeatureRegistrationTests
{
    [Fact]
    public async Task Register_ReportsTransitionsWithoutPostBuildInitialization()
    {
        var services = CreateServices();
        using var provider = services.BuildServiceProvider();
        var status = provider.GetRequiredService<CircuitBreakerStatusService>();

        var policy = await OpenCircuitAsync(provider, HttpClientNames.Default);

        var opened = status.GetDashboard().Breakers.Should().ContainSingle().Subject;
        opened.Name.Should().Be(HttpClientNames.Default);
        opened.State.Should().Be("Open");
        opened.LastError.Should().Be(nameof(HttpStatusCode.ServiceUnavailable));
        opened.TripCount.Should().Be(1);

        policy.Reset();

        status.GetDashboard().Breakers.Should().ContainSingle()
            .Which.State.Should().Be("Closed");
    }

    [Fact]
    public async Task Register_IndependentProvidersDoNotShareCircuitStateOrCallbacks()
    {
        // Building two hosts from the same registrations must not share a policy instance
        // or replace the first host's destination with the last host's status service.
        var services = CreateServices();
        services.AddHttpClient(HttpClientNames.Default)
            .ConfigurePrimaryHttpMessageHandler(() => new SuccessfulHandler());
        using var first = services.BuildServiceProvider();
        using var second = services.BuildServiceProvider();
        var firstStatus = first.GetRequiredService<CircuitBreakerStatusService>();
        var secondStatus = second.GetRequiredService<CircuitBreakerStatusService>();
        using var firstClient = first.GetRequiredService<IHttpClientFactory>()
            .CreateClient(HttpClientNames.Default);
        using var secondClient = second.GetRequiredService<IHttpClientFactory>()
            .CreateClient(HttpClientNames.Default);

        await OpenCircuitAsync(first, HttpClientNames.Default);

        firstStatus.GetDashboard().OpenCount.Should().Be(1);
        secondStatus.GetDashboard().TotalBreakers.Should().Be(0);
        await Assert.ThrowsAsync<BrokenCircuitException<HttpResponseMessage>>(
            () => firstClient.GetAsync("https://example.invalid/"));
        firstClient.Dispose();
        using var replacementClient = first.GetRequiredService<IHttpClientFactory>()
            .CreateClient(HttpClientNames.Default);
        await Assert.ThrowsAsync<BrokenCircuitException<HttpResponseMessage>>(
            () => replacementClient.GetAsync("https://example.invalid/"));
        using var successfulResponse = await secondClient.GetAsync("https://example.invalid/");
        successfulResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        await OpenCircuitAsync(second, HttpClientNames.Alpaca);

        firstStatus.GetDashboard().Breakers.Should().ContainSingle()
            .Which.Name.Should().Be(HttpClientNames.Default);
        secondStatus.GetDashboard().Breakers.Should().ContainSingle()
            .Which.Name.Should().Be(HttpClientNames.Alpaca);
    }

    [Fact]
    public async Task Register_WithoutDiagnosticsStillAllowsTrackedClients()
    {
        var services = new ServiceCollection();
        new HttpClientFeatureRegistration().Register(services, CompositionOptions.Default);
        using var provider = services.BuildServiceProvider();

        var policy = await OpenCircuitAsync(provider, HttpClientNames.Default);

        policy.CircuitState.Should().Be(Polly.CircuitBreaker.CircuitState.Open);
    }

    private static ServiceCollection CreateServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton<CircuitBreakerStatusService>();
        new HttpClientFeatureRegistration().Register(services, CompositionOptions.Default);
        return services;
    }

    private static async Task<AsyncCircuitBreakerPolicy<HttpResponseMessage>> OpenCircuitAsync(
        IServiceProvider provider,
        string clientName)
    {
        var policy = provider.GetRequiredKeyedService<IAsyncPolicy<HttpResponseMessage>>(clientName);
        // Exercise the real named breaker without the retry delays. The client assertion above
        // proves that subsequent HTTP requests use this same provider-owned policy.
        for (var attempt = 0; attempt < 5; attempt++)
        {
            using var response = await policy.ExecuteAsync(
                () => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));
        }

        return (AsyncCircuitBreakerPolicy<HttpResponseMessage>)policy;
    }

    private sealed class SuccessfulHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
    }
}

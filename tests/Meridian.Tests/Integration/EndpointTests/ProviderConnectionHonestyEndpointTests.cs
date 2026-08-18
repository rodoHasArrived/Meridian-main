using System.Net;
using System.Text.Json;
using FluentAssertions;
using Meridian.Identity.Auth;

namespace Meridian.Tests.Integration.EndpointTests;

[Trait("Category", "Integration")]
[Collection("Endpoint")]
public sealed class ProviderConnectionHonestyEndpointTests : IDisposable, IClassFixture<EndpointTestFixture>
{
    private readonly HttpClient _client;
    private readonly HttpClient _diagnosticsClient;

    public ProviderConnectionHonestyEndpointTests(EndpointTestFixture fixture)
    {
        _client = fixture.Client;
        _diagnosticsClient = fixture.CreatePermittedClient(
            UserPermission.ViewDiagnostics,
            UserPermission.ManageProviders);
    }

    public void Dispose() => _diagnosticsClient.Dispose();

    [Fact]
    public async Task ProvidersWithoutRuntimeDiagnostics_AreUnknownRatherThanFabricatedConnected()
    {
        var healthResponse = await _client.GetAsync("/api/providers/health");
        healthResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using var health = JsonDocument.Parse(await healthResponse.Content.ReadAsStringAsync());

        var unknown = health.RootElement.GetProperty("providers")
            .EnumerateArray()
            .First(provider => !provider.GetProperty("diagnosticsAvailable").GetBoolean());

        unknown.GetProperty("connectionState").GetString().Should().Be("unknown");
        unknown.GetProperty("isConnected").ValueKind.Should().Be(JsonValueKind.Null);
        unknown.GetProperty("healthy").ValueKind.Should().Be(JsonValueKind.Null);

        var providerName = unknown.GetProperty("name").GetString();
        providerName.Should().NotBeNullOrWhiteSpace();

        var dashboardResponse = await _client.GetAsync("/api/providers/dashboard");
        dashboardResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using var dashboard = JsonDocument.Parse(await dashboardResponse.Content.ReadAsStringAsync());
        var dashboardProvider = dashboard.RootElement.GetProperty("providers")
            .EnumerateArray()
            .Single(provider => string.Equals(
                provider.GetProperty("name").GetString(),
                providerName,
                StringComparison.OrdinalIgnoreCase));
        dashboardProvider.GetProperty("connectionState").GetString().Should().Be("unknown");
        dashboardProvider.GetProperty("isConnected").ValueKind.Should().Be(JsonValueKind.Null);
        dashboardProvider.GetProperty("trafficLight").GetString().Should().Be("unknown");

        var testResponse = await _diagnosticsClient.PostAsync(
            $"/api/providers/{Uri.EscapeDataString(providerName!)}/test",
            content: null);
        testResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using var test = JsonDocument.Parse(await testResponse.Content.ReadAsStringAsync());
        test.RootElement.GetProperty("connectionState").GetString().Should().Be("unavailable");
        test.RootElement.GetProperty("reachable").ValueKind.Should().Be(JsonValueKind.Null);

        var statusResponse = await _client.GetAsync("/api/providers/status");
        statusResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using var status = JsonDocument.Parse(await statusResponse.Content.ReadAsStringAsync());
        var statusProvider = status.RootElement
            .EnumerateArray()
            .Single(provider => string.Equals(
                provider.GetProperty("providerId").GetString(),
                providerName,
                StringComparison.OrdinalIgnoreCase));
        AssertUnknownConnection(statusProvider);

        var systemHealthResponse = await _client.GetAsync("/api/health/providers");
        systemHealthResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using var systemHealth = JsonDocument.Parse(await systemHealthResponse.Content.ReadAsStringAsync());
        var systemHealthProvider = systemHealth.RootElement.GetProperty("providers")
            .EnumerateArray()
            .Single(provider => string.Equals(
                provider.GetProperty("name").GetString(),
                providerName,
                StringComparison.OrdinalIgnoreCase));
        AssertUnknownConnection(systemHealthProvider);

        var diagnosticsResponse = await _client.GetAsync(
            $"/api/health/providers/{Uri.EscapeDataString(providerName!)}/diagnostics");
        diagnosticsResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using var diagnostics = JsonDocument.Parse(await diagnosticsResponse.Content.ReadAsStringAsync());
        AssertUnknownConnection(diagnostics.RootElement);

        // /api/health/providers/{provider}/test returns registry diagnostics and requires
        // ViewDiagnostics (W9-GOV-008), matching /api/diagnostics/providers/{providerName}/test.
        var healthTestResponse = await _diagnosticsClient.PostAsync(
            $"/api/health/providers/{Uri.EscapeDataString(providerName!)}/test",
            content: null);
        healthTestResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using var healthTest = JsonDocument.Parse(await healthTestResponse.Content.ReadAsStringAsync());
        AssertUnknownReachability(healthTest.RootElement);

        var diagnosticsTestResponse = await _diagnosticsClient.PostAsync(
            $"/api/diagnostics/providers/{Uri.EscapeDataString(providerName!)}/test",
            content: null);
        diagnosticsTestResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using var diagnosticsTest = JsonDocument.Parse(await diagnosticsTestResponse.Content.ReadAsStringAsync());
        AssertUnknownReachability(diagnosticsTest.RootElement);

        var connectivityResponse = await _diagnosticsClient.PostAsync("/api/diagnostics/test-connectivity", content: null);
        connectivityResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using var connectivity = JsonDocument.Parse(await connectivityResponse.Content.ReadAsStringAsync());
        var connectivityProvider = connectivity.RootElement.GetProperty("results")
            .EnumerateArray()
            .Single(provider => string.Equals(
                provider.GetProperty("name").GetString(),
                providerName,
                StringComparison.OrdinalIgnoreCase));
        AssertUnknownReachability(connectivityProvider);
    }

    private static void AssertUnknownConnection(JsonElement provider)
    {
        provider.GetProperty("diagnosticsAvailable").GetBoolean().Should().BeFalse();
        provider.GetProperty("connectionState").GetString().Should().Be("unknown");
        provider.GetProperty("isConnected").ValueKind.Should().Be(JsonValueKind.Null);
    }

    private static void AssertUnknownReachability(JsonElement provider)
    {
        provider.GetProperty("diagnosticsAvailable").GetBoolean().Should().BeFalse();
        provider.GetProperty("connectionState").GetString().Should().Be("unknown");
        provider.GetProperty("reachable").ValueKind.Should().Be(JsonValueKind.Null);
    }
}

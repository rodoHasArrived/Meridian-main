using FluentAssertions;
using Meridian.Ui.Services;
using System.Net;
using System.Text;
using System.Text.Json;

namespace Meridian.Ui.Tests.Services;

public sealed class SetupWizardServiceTests
{
    [Theory]
    [InlineData("Alpaca", "alpaca", "KeyId", "keyId")]
    [InlineData("Polygon", "polygon", "ApiKey", "apiKey")]
    [InlineData("Tiingo", "tiingo", "ApiKey", "token")]
    [InlineData("Finnhub", "finnhub", "ApiKey", "apiKey")]
    [InlineData("Alpha Vantage", "alphavantage", "ApiKey", "apiKey")]
    public async Task SaveCredentials_UsesAuthenticatedApiAndPreservesEnvironment(string provider, string canonical, string field, string inputName)
    {
        using var handler = new CredentialHandler(HttpStatusCode.OK);
        using var api = new ApiClientService(new CredentialClientFactory(handler));
        var service = new SetupWizardService(apiClient: api);
        var names = new[] { "ALPACA__KEYID", "ALPACA__SECRETKEY", "POLYGON__APIKEY", "TIINGO__TOKEN", "FINNHUB__APIKEY", "ALPHAVANTAGE__APIKEY" };
        var before = names.ToDictionary(n => n, n => Environment.GetEnvironmentVariable(n));
        var userBefore = OperatingSystem.IsWindows() ? names.ToDictionary(n => n, n => Environment.GetEnvironmentVariable(n, EnvironmentVariableTarget.User)) : null;
        await service.SaveCredentialsAsync(provider, new Dictionary<string, string>
        {
            [inputName] = "retained-vault-secret",
            ["secretKey"] = "retained-vault-secondary",
            ["useSandbox"] = "true"
        }, connectionId: "connection /A&connectionId=B");
        handler.Method.Should().Be(HttpMethod.Put);
        handler.Url.Should().Contain($"/api/providers/{canonical}/credentials?connectionId=connection%20%2FA%26connectionId%3DB");
        using var body = JsonDocument.Parse(handler.Body!);
        body.RootElement.GetProperty("credentials").GetProperty(field).GetString().Should().Be("retained-vault-secret");
        if (canonical == "alpaca")
            body.RootElement.GetProperty("environment").GetString().Should().Be("paper");
        foreach (var name in names)
        {
            (Environment.GetEnvironmentVariable(name) == before[name]).Should().BeTrue("the wizard must not persist secrets in process environment");
            if (userBefore is not null)
                (Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.User) == userBefore[name]).Should().BeTrue("the wizard must not persist secrets in user environment");
        }
    }

    [Fact]
    public async Task SaveCredentials_RefusedApiDoesNotReportSuccessOrEchoResponseSecrets()
    {
        using var handler = new CredentialHandler(HttpStatusCode.Forbidden);
        using var api = new ApiClientService(new CredentialClientFactory(handler));
        var service = new SetupWizardService(apiClient: api);
        var save = () => service.SaveCredentialsAsync("Polygon", new Dictionary<string, string> { ["apiKey"] = "request-secret" });
        var error = (await save.Should().ThrowAsync<InvalidOperationException>()).Which;
        error.ToString().Should().NotContain("response-secret").And.NotContain("request-secret");
    }

    private sealed class CredentialClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class CredentialHandler(HttpStatusCode status) : HttpMessageHandler
    {
        public HttpMethod? Method { get; private set; }
        public string? Url { get; private set; }
        public string? Body { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Method = request.Method;
            Url = request.RequestUri!.AbsoluteUri;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            return new HttpResponseMessage(status)
            {
                Content = new StringContent(
                status == HttpStatusCode.OK ? "{\"providerId\":\"provider\",\"credentialState\":3}" : "response-secret", Encoding.UTF8, "application/json")
            };
        }
    }

    [Fact]
    public void GetSetupPresets_UsesStrategyLabelForRetainedResearcherPreset()
    {
        var service = new SetupWizardService();

        var preset = service.GetSetupPresets()
            .Should()
            .ContainSingle(candidate => candidate.Id == "researcher")
            .Subject;

        preset.Name.Should().Be("Strategy Analyst");
        preset.Name.Should().NotContain("Research");
        preset.Description.Should().Contain("strategy-data");
    }
}

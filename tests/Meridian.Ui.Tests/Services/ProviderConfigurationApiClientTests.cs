using System.Net;
using System.Text;
using System.Text.Json;
using Meridian.Ui.Services;
using Meridian.Ui.Services.ProviderDiagnostics;

namespace Meridian.Ui.Tests.Services;

public sealed class ProviderConfigurationApiClientTests
{
    [Fact]
    public async Task DataSources_ReadsNumericHostProviderAndPostsTargetedToggle()
    {
        using var api = new ApiClientService(new Factory(async request =>
        {
            if (request.Method == HttpMethod.Get)
            {
                Assert.Equal("/api/config/datasources", request.RequestUri!.AbsolutePath);
                return Json("""
                    { "sources": [{ "id": "primary", "provider": 1, "name": "Alpaca", "type": "RealTime" }] }
                    """);
            }

            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/api/config/datasources/primary/toggle", request.RequestUri!.AbsolutePath);
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            Assert.False(body.RootElement.GetProperty("enabled").GetBoolean());
            return new HttpResponseMessage(HttpStatusCode.OK);
        }));

        var client = new ProviderConfigurationApiClient(api);
        var result = await client.GetDataSourcesAsync();
        Assert.Equal("Alpaca", Assert.Single(result.Sources!).Provider);
        await client.SetDataSourceEnabledAsync("primary", false);
    }

    [Fact]
    public async Task SetDefault_OnlyUpdatesRequestedSurface()
    {
        using var api = new ApiClientService(new Factory(async request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/api/config/datasources/defaults", request.RequestUri!.AbsolutePath);
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            Assert.Equal("history", body.RootElement.GetProperty("defaultHistoricalSourceId").GetString());
            Assert.True(body.RootElement.GetProperty("updateHistorical").GetBoolean());
            Assert.False(body.RootElement.GetProperty("updateRealTime").GetBoolean());
            return new HttpResponseMessage(HttpStatusCode.OK);
        }));

        await new ProviderConfigurationApiClient(api).SetDefaultAsync("history", isHistorical: true);
    }

    [Fact]
    public async Task SymbolMapping_WritesOnlyRequestedProviderAndLoadsHostOverrides()
    {
        var writes = 0;
        using var api = new ApiClientService(new Factory(async request =>
        {
            if (request.Method == HttpMethod.Get)
            {
                Assert.Equal("/api/symbols/mappings", request.RequestUri!.AbsolutePath);
                return Json("""
                    [{ "canonicalSymbol": "BRK.B", "providerSymbols": { "stooq": "brk-b.us", "ib": "BRK B" } }]
                    """);
            }

            writes++;
            Assert.Equal(HttpMethod.Put, request.Method);
            Assert.Equal("/api/symbols/mappings/BRK.B/providers/stooq", request.RequestUri!.AbsolutePath);
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            Assert.Equal("brk-b.us", body.RootElement.GetProperty("providerSymbol").GetString());
            Assert.Single(body.RootElement.EnumerateObject());
            return new HttpResponseMessage(HttpStatusCode.OK);
        }));

        var client = new SymbolMappingApiClient(api);
        await client.SetProviderSymbolAsync("BRK.B", "Stooq", "brk-b.us");

        Assert.Equal(1, writes);
        Assert.Equal("BRK B", Assert.Single(client.GetMappings()).ProviderSymbols!["IB"]);
        Assert.Equal("brk-b.us", client.TestMapping("BRK.B")["Stooq"]);
    }

    [Fact]
    public async Task SymbolMapping_UsesHostMultipartImportAndTextExport()
    {
        const string csv = "canonical,stooq\nBRK.B,brk-b.us\n";
        using var api = new ApiClientService(new Factory(async request =>
        {
            switch (request.RequestUri!.AbsolutePath)
            {
                case "/api/symbols/mappings/import":
                    Assert.Equal(HttpMethod.Post, request.Method);
                    Assert.Equal("multipart/form-data", request.Content!.Headers.ContentType!.MediaType);
                    Assert.Contains("brk-b.us", await request.Content.ReadAsStringAsync());
                    return Json("""{ "imported": 1 }""");
                case "/api/symbols/mappings/csv/export":
                    Assert.Equal(HttpMethod.Get, request.Method);
                    return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(csv, Encoding.UTF8, "text/csv") };
                case "/api/symbols/mappings":
                    return Json("[]");
                default:
                    throw new InvalidOperationException("Unexpected test route.");
            }
        }));

        var client = new SymbolMappingApiClient(api);
        Assert.Equal(1, await client.ImportFromCsvAsync(csv));
        Assert.Equal(csv, await client.ExportToCsvAsync());
    }

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };

    private sealed class Factory(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new Handler(send));
    }

    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => send(request);
    }
}

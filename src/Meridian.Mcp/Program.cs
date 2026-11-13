using Meridian.Mcp.Services;
using Meridian.Mcp.Tools;
using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(args);

// MCP servers communicate over stdio; all logging must go to stderr
builder.Logging.SetMinimumLevel(LogLevel.Warning);
builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);

builder.Services.AddSingleton<RepoPathService>();
builder.Services.AddHttpClient(LedgerTools.HttpClientName, client =>
{
    var configured = builder.Configuration["MeridianApi:BaseUrl"] ?? "http://localhost:8080/";
    if (!Uri.TryCreate(configured, UriKind.Absolute, out var address) ||
        (address.Scheme != Uri.UriSchemeHttps && !(address.Scheme == Uri.UriSchemeHttp && address.IsLoopback)) ||
        !string.IsNullOrEmpty(address.UserInfo) || !string.IsNullOrEmpty(address.Query) || !string.IsNullOrEmpty(address.Fragment))
        throw new InvalidOperationException("MeridianApi:BaseUrl must be an HTTPS origin or local HTTP workstation URL.");
    client.BaseAddress = new Uri(address.AbsoluteUri.TrimEnd('/') + "/");
    client.Timeout = TimeSpan.FromSeconds(30);
    var token = builder.Configuration["MeridianApi:BearerToken"];
    if (!string.IsNullOrWhiteSpace(token))
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
}).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly()
    .WithPromptsFromAssembly()
    .WithResourcesFromAssembly();

await builder.Build().RunAsync();

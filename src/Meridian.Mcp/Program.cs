using Meridian.Mcp.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(args);

// MCP servers communicate over stdio; all logging must go to stderr
builder.Logging.SetMinimumLevel(LogLevel.Warning);
builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);

builder.Services.AddSingleton<RepoPathService>();
builder.Services.AddSingleton(_ => LedgerApiClient.CreateHttpClient());
// Credential settings are evaluated only when a ledger read is invoked; other MCP tools do not
// require an operator session. Session/tenant authority remains with the API's existing login flow.
builder.Services.AddSingleton(sp => new LedgerApiClient(sp.GetRequiredService<HttpClient>()));

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly()
    .WithPromptsFromAssembly()
    .WithResourcesFromAssembly();

await builder.Build().RunAsync();

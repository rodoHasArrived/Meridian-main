using Meridian.Mcp.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(args);

// MCP servers communicate over stdio; all logging must go to stderr
builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Warning);

builder.Services.AddSingleton<RepoPathService>();

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly()
    .WithPromptsFromAssembly()
    .WithResourcesFromAssembly();

await builder.Build().RunAsync();

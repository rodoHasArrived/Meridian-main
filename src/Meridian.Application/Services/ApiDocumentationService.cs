using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Meridian.Application.Logging;
using Serilog;

namespace Meridian.Application.Services;

/// <summary>
/// Service for generating API documentation from code and comments.
/// Implements QW-121: API Docs from Comments and DEV-9: API Explorer / Swagger UI.
/// </summary>
public sealed class ApiDocumentationService
{
    private readonly ILogger _log = LoggingSetup.ForContext<ApiDocumentationService>();

    /// <summary>
    /// Generates OpenAPI 3.0 specification for the API.
    /// </summary>
    public OpenApiSpec GenerateOpenApiSpec()
    {
        var spec = new OpenApiSpec
        {
            OpenApi = "3.0.3",
            Info = new OpenApiInfo
            {
                Title = "Meridian API",
                Description = "REST API for Meridian - real-time and historical market data collection",
                Version = "1.6.1",
                Contact = new OpenApiContact
                {
                    Name = "Meridian",
                    Url = "https://github.com/rodoHasArrived/Meridian"
                },
                License = new OpenApiLicense
                {
                    Name = "MIT",
                    Url = "https://opensource.org/licenses/MIT"
                }
            },
            Servers = new List<OpenApiServer>
            {
                new OpenApiServer { Url = "http://localhost:8080", Description = "Local development server" }
            },
            Tags = GenerateTags(),
            Paths = GeneratePaths(),
            Components = GenerateComponents()
        };

        return spec;
    }

    /// <summary>
    /// Generates a Swagger UI HTML page.
    /// </summary>
    public string GenerateSwaggerHtml(string specUrl = "/api/openapi.json")
    {
        return $@"<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""UTF-8"">
    <title>Meridian - API Explorer</title>
    <link rel=""stylesheet"" type=""text/css"" href=""https://unpkg.com/swagger-ui-dist@5.9.0/swagger-ui.css"" />
    <style>
        html {{ box-sizing: border-box; overflow-y: scroll; }}
        *, *:before, *:after {{ box-sizing: inherit; }}
        body {{ margin: 0; background: #fafafa; }}
        .swagger-ui .topbar {{ display: none; }}
        .swagger-ui .info {{ margin: 30px 0; }}
        .custom-header {{
            background: linear-gradient(135deg, #1e3a5f 0%, #2d5a87 100%);
            color: white;
            padding: 20px;
            text-align: center;
        }}
        .custom-header h1 {{ margin: 0; font-size: 24px; }}
        .custom-header p {{ margin: 10px 0 0 0; opacity: 0.8; }}
    </style>
</head>
<body>
    <div class=""custom-header"">
        <h1>Meridian API</h1>
        <p>Interactive API Documentation</p>
    </div>
    <div id=""swagger-ui""></div>
    <script src=""https://unpkg.com/swagger-ui-dist@5.9.0/swagger-ui-bundle.js""></script>
    <script src=""https://unpkg.com/swagger-ui-dist@5.9.0/swagger-ui-standalone-preset.js""></script>
    <script>
        window.onload = function() {{
            window.ui = SwaggerUIBundle({{
                url: '{specUrl}',
                dom_id: '#swagger-ui',
                deepLinking: true,
                presets: [
                    SwaggerUIBundle.presets.apis,
                    SwaggerUIStandalonePreset
                ],
                plugins: [
                    SwaggerUIBundle.plugins.DownloadUrl
                ],
                layout: 'StandaloneLayout',
                defaultModelsExpandDepth: 1,
                defaultModelExpandDepth: 2,
                docExpansion: 'list',
                filter: true,
                showExtensions: true,
                showCommonExtensions: true
            }});
        }};
    </script>
</body>
</html>";
    }

    /// <summary>
    /// Generates markdown documentation for the API.
    /// </summary>
    public string GenerateMarkdownDocs()
    {
        var sb = new StringBuilder();
        var spec = GenerateOpenApiSpec();

        sb.AppendLine("# Meridian API Documentation");
        sb.AppendLine();
        sb.AppendLine($"Version: {spec.Info?.Version}");
        sb.AppendLine();
        sb.AppendLine(spec.Info?.Description);
        sb.AppendLine();

        sb.AppendLine("## Base URL");
        sb.AppendLine();
        foreach (var server in spec.Servers ?? new List<OpenApiServer>())
        {
            sb.AppendLine($"- `{server.Url}` - {server.Description}");
        }
        sb.AppendLine();

        // Group endpoints by tag
        var pathsByTag = new Dictionary<string, List<(string path, string method, OpenApiOperation op)>>();

        foreach (var (path, pathItem) in spec.Paths ?? new Dictionary<string, OpenApiPathItem>())
        {
            if (pathItem.Get != null)
                AddToTagGroup(pathsByTag, path, "GET", pathItem.Get);
            if (pathItem.Post != null)
                AddToTagGroup(pathsByTag, path, "POST", pathItem.Post);
            if (pathItem.Put != null)
                AddToTagGroup(pathsByTag, path, "PUT", pathItem.Put);
            if (pathItem.Delete != null)
                AddToTagGroup(pathsByTag, path, "DELETE", pathItem.Delete);
        }

        foreach (var (tag, endpoints) in pathsByTag.OrderBy(kvp => kvp.Key))
        {
            sb.AppendLine($"## {tag}");
            sb.AppendLine();

            foreach (var (path, method, op) in endpoints)
            {
                sb.AppendLine($"### {method} {path}");
                sb.AppendLine();
                sb.AppendLine(op.Summary);
                if (!string.IsNullOrEmpty(op.Description))
                {
                    sb.AppendLine();
                    sb.AppendLine(op.Description);
                }
                sb.AppendLine();

                if (op.Parameters?.Count > 0)
                {
                    sb.AppendLine("**Parameters:**");
                    sb.AppendLine();
                    sb.AppendLine("| Name | In | Type | Required | Description |");
                    sb.AppendLine("|------|-----|------|----------|-------------|");
                    foreach (var param in op.Parameters)
                    {
                        sb.AppendLine($"| {param.Name} | {param.In} | {param.Schema?.Type ?? "string"} | {(param.Required ? "Yes" : "No")} | {param.Description} |");
                    }
                    sb.AppendLine();
                }

                sb.AppendLine("**Responses:**");
                sb.AppendLine();
                foreach (var (code, response) in op.Responses ?? new Dictionary<string, OpenApiResponse>())
                {
                    sb.AppendLine($"- `{code}`: {response.Description}");
                }
                sb.AppendLine();
            }
        }

        return sb.ToString();
    }

    private void AddToTagGroup(
        Dictionary<string, List<(string, string, OpenApiOperation)>> groups,
        string path,
        string method,
        OpenApiOperation op)
    {
        var tag = op.Tags?.FirstOrDefault() ?? "Other";
        if (!groups.ContainsKey(tag))
            groups[tag] = new List<(string, string, OpenApiOperation)>();
        groups[tag].Add((path, method, op));
    }

    private List<OpenApiTag> GenerateTags()
    {
        return new List<OpenApiTag>
        {
            new OpenApiTag { Name = "Health", Description = "Health check and monitoring endpoints" },
            new OpenApiTag { Name = "Configuration", Description = "Configuration management" },
            new OpenApiTag { Name = "Backfill", Description = "Historical data backfill operations" },
            new OpenApiTag { Name = "Storage", Description = "Storage management and data organization" },
            new OpenApiTag { Name = "Symbols", Description = "Symbol subscription management" },
            new OpenApiTag { Name = "Historical", Description = "Historical data query endpoints" },
            new OpenApiTag { Name = "Diagnostics", Description = "Diagnostic and debugging tools" },
            new OpenApiTag { Name = "Tools", Description = "Utility tools and generators" }
        };
    }

    private Dictionary<string, OpenApiPathItem> GeneratePaths()
    {
        var paths = new Dictionary<string, OpenApiPathItem>();

        // Health endpoints
        paths["/health"] = new OpenApiPathItem
        {
            Get = new OpenApiOperation
            {
                Tags = new[] { "Health" },
                Summary = "Comprehensive health check",
                Description = "Returns detailed health status including drop rate, queue utilization, and data freshness",
                OperationId = "getHealth",
                Responses = new Dictionary<string, OpenApiResponse>
                {
                    ["200"] = new OpenApiResponse { Description = "System is healthy" },
                    ["503"] = new OpenApiResponse { Description = "System is unhealthy" }
                }
            }
        };

        paths["/ready"] = new OpenApiPathItem
        {
            Get = new OpenApiOperation
            {
                Tags = new[] { "Health" },
                Summary = "Kubernetes readiness probe",
                Description = "Returns 200 if ready to receive traffic",
                OperationId = "getReady",
                Responses = new Dictionary<string, OpenApiResponse>
                {
                    ["200"] = new OpenApiResponse { Description = "Service is ready" },
                    ["503"] = new OpenApiResponse { Description = "Service not ready" }
                }
            }
        };

        paths["/metrics"] = new OpenApiPathItem
        {
            Get = new OpenApiOperation
            {
                Tags = new[] { "Health" },
                Summary = "Prometheus metrics",
                Description = "Returns metrics in Prometheus exposition format",
                OperationId = "getMetrics",
                Responses = new Dictionary<string, OpenApiResponse>
                {
                    ["200"] = new OpenApiResponse { Description = "Metrics in Prometheus format" }
                }
            }
        };

        // Configuration endpoints
        paths["/api/config"] = new OpenApiPathItem
        {
            Get = new OpenApiOperation
            {
                Tags = new[] { "Configuration" },
                Summary = "Get current configuration",
                Description = "Returns the current application configuration",
                OperationId = "getConfig",
                Responses = new Dictionary<string, OpenApiResponse>
                {
                    ["200"] = new OpenApiResponse { Description = "Current configuration" }
                }
            }
        };

        // Historical data endpoints
        paths["/api/historical/query"] = new OpenApiPathItem
        {
            Post = new OpenApiOperation
            {
                Tags = new[] { "Historical" },
                Summary = "Query historical data",
                Description = "Query stored historical market data for a symbol",
                OperationId = "queryHistoricalData",
                RequestBody = new OpenApiRequestBody
                {
                    Description = "Query parameters",
                    Required = true,
                    Content = new Dictionary<string, OpenApiMediaType>
                    {
                        ["application/json"] = new OpenApiMediaType
                        {
                            Schema = new OpenApiSchema { Ref = "#/components/schemas/HistoricalDataQuery" }
                        }
                    }
                },
                Responses = new Dictionary<string, OpenApiResponse>
                {
                    ["200"] = new OpenApiResponse { Description = "Query results" },
                    ["400"] = new OpenApiResponse { Description = "Invalid query parameters" }
                }
            }
        };

        paths["/api/historical/symbols"] = new OpenApiPathItem
        {
            Get = new OpenApiOperation
            {
                Tags = new[] { "Historical" },
                Summary = "Get available symbols",
                Description = "Returns list of symbols with stored historical data",
                OperationId = "getHistoricalSymbols",
                Responses = new Dictionary<string, OpenApiResponse>
                {
                    ["200"] = new OpenApiResponse { Description = "List of symbols" }
                }
            }
        };

        // Diagnostic endpoints
        paths["/api/diagnostics/bundle"] = new OpenApiPathItem
        {
            Post = new OpenApiOperation
            {
                Tags = new[] { "Diagnostics" },
                Summary = "Generate diagnostic bundle",
                Description = "Creates a ZIP file with system diagnostics, logs, and configuration",
                OperationId = "createDiagnosticBundle",
                Responses = new Dictionary<string, OpenApiResponse>
                {
                    ["200"] = new OpenApiResponse { Description = "Diagnostic bundle created" }
                }
            }
        };

        paths["/api/diagnostics/errors"] = new OpenApiPathItem
        {
            Get = new OpenApiOperation
            {
                Tags = new[] { "Diagnostics" },
                Summary = "Get recent errors",
                Description = "Returns the last N errors from the application",
                OperationId = "getRecentErrors",
                Parameters = new List<OpenApiParameter>
                {
                    new OpenApiParameter
                    {
                        Name = "count",
                        In = "query",
                        Description = "Number of errors to return",
                        Required = false,
                        Schema = new OpenApiSchema { Type = "integer", Default = "10" }
                    }
                },
                Responses = new Dictionary<string, OpenApiResponse>
                {
                    ["200"] = new OpenApiResponse { Description = "Recent errors" }
                }
            }
        };

        // Tools endpoints
        paths["/api/tools/sample-data"] = new OpenApiPathItem
        {
            Post = new OpenApiOperation
            {
                Tags = new[] { "Tools" },
                Summary = "Generate sample data",
                Description = "Generates sample market data for testing",
                OperationId = "generateSampleData",
                Responses = new Dictionary<string, OpenApiResponse>
                {
                    ["200"] = new OpenApiResponse { Description = "Sample data generated" }
                }
            }
        };

        paths["/api/tools/config-templates"] = new OpenApiPathItem
        {
            Get = new OpenApiOperation
            {
                Tags = new[] { "Tools" },
                Summary = "Get configuration templates",
                Description = "Returns available configuration templates",
                OperationId = "getConfigTemplates",
                Responses = new Dictionary<string, OpenApiResponse>
                {
                    ["200"] = new OpenApiResponse { Description = "List of templates" }
                }
            }
        };

        paths["/api/tools/dry-run"] = new OpenApiPathItem
        {
            Post = new OpenApiOperation
            {
                Tags = new[] { "Tools" },
                Summary = "Run dry-run validation",
                Description = "Validates configuration without starting the service",
                OperationId = "dryRun",
                Responses = new Dictionary<string, OpenApiResponse>
                {
                    ["200"] = new OpenApiResponse { Description = "Validation results" }
                }
            }
        };

        // Backfill endpoints
        paths["/api/backfill/providers"] = new OpenApiPathItem
        {
            Get = new OpenApiOperation
            {
                Tags = new[] { "Backfill" },
                Summary = "Get backfill providers",
                Description = "Returns list of available historical data providers",
                OperationId = "getBackfillProviders",
                Responses = new Dictionary<string, OpenApiResponse>
                {
                    ["200"] = new OpenApiResponse { Description = "List of providers" }
                }
            }
        };

        paths["/api/backfill/run"] = new OpenApiPathItem
        {
            Post = new OpenApiOperation
            {
                Tags = new[] { "Backfill" },
                Summary = "Run backfill",
                Description = "Starts a historical data backfill operation",
                OperationId = "runBackfill",
                Responses = new Dictionary<string, OpenApiResponse>
                {
                    ["200"] = new OpenApiResponse { Description = "Backfill started" },
                    ["400"] = new OpenApiResponse { Description = "Invalid request" }
                }
            }
        };

        return paths;
    }

    private OpenApiComponents GenerateComponents()
    {
        return new OpenApiComponents
        {
            Schemas = new Dictionary<string, OpenApiSchema>
            {
                ["HistoricalDataQuery"] = new OpenApiSchema
                {
                    Type = "object",
                    Properties = new Dictionary<string, OpenApiSchema>
                    {
                        ["symbol"] = new OpenApiSchema { Type = "string", Description = "Stock symbol" },
                        ["from"] = new OpenApiSchema { Type = "string", Format = "date", Description = "Start date" },
                        ["to"] = new OpenApiSchema { Type = "string", Format = "date", Description = "End date" },
                        ["dataType"] = new OpenApiSchema { Type = "string", Description = "Type of data (trade, quote, bar)" },
                        ["limit"] = new OpenApiSchema { Type = "integer", Description = "Maximum records to return" }
                    },
                    Required = new[] { "symbol" }
                },
                ["BackfillRequest"] = new OpenApiSchema
                {
                    Type = "object",
                    Properties = new Dictionary<string, OpenApiSchema>
                    {
                        ["provider"] = new OpenApiSchema { Type = "string", Description = "Provider name" },
                        ["symbols"] = new OpenApiSchema { Type = "array", Items = new OpenApiSchema { Type = "string" } },
                        ["from"] = new OpenApiSchema { Type = "string", Format = "date" },
                        ["to"] = new OpenApiSchema { Type = "string", Format = "date" }
                    }
                }
            }
        };
    }
}


public sealed class OpenApiSpec
{
    public string OpenApi { get; set; } = "3.0.3";
    public OpenApiInfo? Info { get; set; }
    public List<OpenApiServer>? Servers { get; set; }
    public List<OpenApiTag>? Tags { get; set; }
    public Dictionary<string, OpenApiPathItem>? Paths { get; set; }
    public OpenApiComponents? Components { get; set; }
}

public sealed class OpenApiInfo
{
    public string? Title { get; set; }
    public string? Description { get; set; }
    public string? Version { get; set; }
    public OpenApiContact? Contact { get; set; }
    public OpenApiLicense? License { get; set; }
}

public sealed class OpenApiContact
{
    public string? Name { get; set; }
    public string? Url { get; set; }
}

public sealed class OpenApiLicense
{
    public string? Name { get; set; }
    public string? Url { get; set; }
}

public sealed class OpenApiServer
{
    public string? Url { get; set; }
    public string? Description { get; set; }
}

public sealed class OpenApiTag
{
    public string? Name { get; set; }
    public string? Description { get; set; }
}

public sealed class OpenApiPathItem
{
    public OpenApiOperation? Get { get; set; }
    public OpenApiOperation? Post { get; set; }
    public OpenApiOperation? Put { get; set; }
    public OpenApiOperation? Delete { get; set; }
}

public sealed class OpenApiOperation
{
    public string[]? Tags { get; set; }
    public string? Summary { get; set; }
    public string? Description { get; set; }
    public string? OperationId { get; set; }
    public List<OpenApiParameter>? Parameters { get; set; }
    public OpenApiRequestBody? RequestBody { get; set; }
    public Dictionary<string, OpenApiResponse>? Responses { get; set; }
}

public sealed class OpenApiParameter
{
    public string? Name { get; set; }
    public string? In { get; set; }
    public string? Description { get; set; }
    public bool Required { get; set; }
    public OpenApiSchema? Schema { get; set; }
}

public sealed class OpenApiRequestBody
{
    public string? Description { get; set; }
    public bool Required { get; set; }
    public Dictionary<string, OpenApiMediaType>? Content { get; set; }
}

public sealed class OpenApiMediaType
{
    public OpenApiSchema? Schema { get; set; }
}

public sealed class OpenApiResponse
{
    public string? Description { get; set; }
    public Dictionary<string, OpenApiMediaType>? Content { get; set; }
}

public sealed class OpenApiComponents
{
    public Dictionary<string, OpenApiSchema>? Schemas { get; set; }
}

public sealed class OpenApiSchema
{
    public string? Type { get; set; }
    public string? Format { get; set; }
    public string? Description { get; set; }
    public string? Ref { get; set; }
    public string? Default { get; set; }
    public OpenApiSchema? Items { get; set; }
    public Dictionary<string, OpenApiSchema>? Properties { get; set; }
    public string[]? Required { get; set; }
}


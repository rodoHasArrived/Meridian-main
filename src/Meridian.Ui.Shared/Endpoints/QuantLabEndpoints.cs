using System.Globalization;
using System.Text.Json;
using Meridian.Contracts.Api;
using Meridian.Identity.Auth;
using Meridian.QuantScript.Compilation;
using Meridian.QuantScript.Plotting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Meridian.Ui.Shared.Endpoints;

/// <summary>
/// HTTP endpoints that expose the Meridian QuantScript engine (the "Quant Lab")
/// to the browser workstation. Endpoints are only meaningful when an
/// <see cref="IScriptRunner"/> has been registered (gated by <c>QuantLab:Enabled</c>);
/// when not registered, calls return <c>503 Service Unavailable</c>.
/// </summary>
/// <remarks>
/// QuantScript compiles and executes each request in a dedicated, killable worker process with
/// bounded admission, protocol, output, time, memory, CPU, process-count, and host-data RPC usage.
/// The worker still has the launching user's file/network permissions, so this is containment for
/// trusted operators rather than a hostile-code sandbox. Keep the routes behind workstation auth.
/// </remarks>
public static class QuantLabEndpoints
{
    /// <summary>
    /// Maps Quant Lab routes onto the application. Safe to call regardless of whether the
    /// QuantScript engine is registered — endpoints respond with 503 when it is missing.
    /// </summary>
    public static void MapQuantLabEndpoints(this WebApplication app, JsonSerializerOptions jsonOptions)
    {
        var group = app.MapGroup("").WithTags("Quant Lab");

        // POST /api/quant/run — compile + execute a script and return a JSON-friendly result.
        group.MapPost(UiApiRoutes.QuantRun, async (
            QuantRunRequest? request,
            [FromServices] IServiceProvider services,
            HttpContext ctx,
            CancellationToken ct) =>
        {
            if (request is null || string.IsNullOrWhiteSpace(request.Source))
            {
                return Results.Json(
                    new { error = "Source code is required" },
                    jsonOptions,
                    statusCode: StatusCodes.Status400BadRequest);
            }

            if (!HasQuantLabExecutionPermission(ctx))
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }

            var runner = services.GetService<IScriptRunner>();
            if (runner is null)
            {
                return QuantLabUnavailable(jsonOptions);
            }

            try
            {
                var compiler = services.GetService<IQuantScriptCompiler>();
                var descriptors = compiler?.ExtractParameters(request.Source) ?? Array.Empty<ParameterDescriptor>();
                var parameters = QuantRunParameterParser.Normalize(request.Parameters, descriptors);
                var result = await runner.RunAsync(request.Source, parameters, ct).ConfigureAwait(false);
                return Results.Json(QuantRunResponse.From(result), jsonOptions);
            }
            catch (ArgumentException ex)
            {
                return Results.Json(
                    new { error = ex.Message },
                    jsonOptions,
                    statusCode: StatusCodes.Status400BadRequest);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return Results.StatusCode(StatusCodes.Status499ClientClosedRequest);
            }
            catch (Exception ex)
            {
                var logger = services.GetService<ILoggerFactory>()?.CreateLogger("QuantLabEndpoints");
                logger?.LogWarning(ex, "Quant Lab run failed");
                return Results.Json(
                    QuantRunResponse.FromError(ex.Message),
                    jsonOptions,
                    statusCode: StatusCodes.Status500InternalServerError);
            }
        })
        .WithName("RunQuantScript").RequirePermission(UserPermission.ManageStrategies)
        .Produces(200)
        .Produces(400)
        .Produces(500)
        .Produces(503);

        // POST /api/quant/parameters — extract parameter descriptors from a script source.
        group.MapPost(UiApiRoutes.QuantParameters, (
            QuantParametersRequest? request,
            [FromServices] IServiceProvider services) =>
        {
            if (request is null || string.IsNullOrWhiteSpace(request.Source))
            {
                return Results.Json(
                    new QuantParametersResponse(Array.Empty<QuantParameterDto>()),
                    jsonOptions);
            }

            var compiler = services.GetService<IQuantScriptCompiler>();
            if (compiler is null)
            {
                return QuantLabUnavailable(jsonOptions);
            }

            var parameters = compiler.ExtractParameters(request.Source);
            var dtos = parameters
                .Select(p => new QuantParameterDto(
                    Name: p.Name,
                    Label: p.Label,
                    TypeName: p.TypeName,
                    DefaultValue: FormatParameterValue(p.DefaultValue),
                    Min: double.IsFinite(p.Min) ? p.Min : null,
                    Max: double.IsFinite(p.Max) ? p.Max : null,
                    Description: p.Description))
                .ToArray();

            return Results.Json(new QuantParametersResponse(dtos), jsonOptions);
        })
        .WithName("ExtractQuantParameters")
        .RequirePermission(UserPermission.ManageStrategies)
        .Produces(200)
        .Produces(503);

        // GET /api/quant/templates — starter scripts so a new user has something to run.
        group.MapGet(UiApiRoutes.QuantTemplates, () =>
        {
            return Results.Json(new QuantTemplatesResponse(QuantLabTemplates.All), jsonOptions);
        })
        .WithName("GetQuantTemplates").RequireAnyPermission(UserPermission.ViewStrategies, UserPermission.ManageStrategies)
        .Produces(200);
    }

    private static bool HasQuantLabExecutionPermission(HttpContext context)
    {
        if (context.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] is not UserPermission permissions)
        {
            return false;
        }

        return (permissions & UserPermission.ManageStrategies) == UserPermission.ManageStrategies;
    }

    private static IResult QuantLabUnavailable(JsonSerializerOptions jsonOptions)
        => Results.Json(
            new
            {
                error = "Quant Lab is not enabled on this host. Set QuantLab:Enabled to true to enable.",
                quantLabEnabled = false
            },
            jsonOptions,
            statusCode: StatusCodes.Status503ServiceUnavailable);

    private static string? FormatParameterValue(object? value) => value switch
    {
        null => null,
        bool boolean => boolean ? "true" : "false",
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString()
    };
}

/// <summary>Body of a POST to <c>/api/quant/run</c>.</summary>
public sealed record QuantRunRequest(
    string Source,
    IReadOnlyDictionary<string, object?>? Parameters);

/// <summary>Body of a POST to <c>/api/quant/parameters</c>.</summary>
public sealed record QuantParametersRequest(string Source);

/// <summary>Plot data point with an ISO-8601 date for safe JSON transport.</summary>
public sealed record QuantPlotPointDto(string Date, double Value);

/// <summary>A single named series for multi-line plots.</summary>
public sealed record QuantPlotSeriesDto(string Label, IReadOnlyList<QuantPlotPointDto> Values);

/// <summary>OHLC bar for candlestick plots.</summary>
public sealed record QuantPlotBarDto(
    string Date,
    double Open,
    double High,
    double Low,
    double Close,
    double Volume);

/// <summary>JSON-friendly representation of a single plot emitted by a script.</summary>
public sealed record QuantPlotDto(
    string Title,
    string Type,
    IReadOnlyList<QuantPlotPointDto>? Series,
    IReadOnlyList<QuantPlotSeriesDto>? MultiSeries,
    IReadOnlyList<QuantPlotBarDto>? Candlestick,
    IReadOnlyList<IReadOnlyList<double>>? HeatmapData,
    IReadOnlyList<string>? HeatmapLabels);

/// <summary>JSON-friendly Roslyn diagnostic.</summary>
public sealed record QuantDiagnosticDto(string Severity, string Message, int Line, int Column);

/// <summary>JSON-friendly metric entry (label/value).</summary>
public sealed record QuantMetricDto(string Label, string Value);

/// <summary>JSON-friendly script-emitted trade row.</summary>
public sealed record QuantTradeDto(
    string Timestamp,
    string Symbol,
    string Side,
    decimal Quantity,
    decimal Price,
    decimal Commission,
    string FillId,
    string OrderId,
    int BacktestRunIndex);

/// <summary>JSON-friendly description of a discovered script parameter.</summary>
public sealed record QuantParameterDto(
    string Name,
    string Label,
    string TypeName,
    string? DefaultValue,
    double? Min,
    double? Max,
    string? Description);

/// <summary>Response returned by <c>/api/quant/parameters</c>.</summary>
public sealed record QuantParametersResponse(IReadOnlyList<QuantParameterDto> Parameters);

/// <summary>A pre-built starter snippet for the lab.</summary>
public sealed record QuantTemplateDto(string Id, string Title, string Description, string Source);

/// <summary>Response returned by <c>/api/quant/templates</c>.</summary>
public sealed record QuantTemplatesResponse(IReadOnlyList<QuantTemplateDto> Templates);

/// <summary>Response returned by <c>/api/quant/run</c>.</summary>
public sealed record QuantRunResponse(
    bool Success,
    double ElapsedMs,
    double CompileTimeMs,
    long PeakMemoryBytes,
    string? RuntimeError,
    string ConsoleOutput,
    IReadOnlyList<QuantDiagnosticDto> CompilationErrors,
    IReadOnlyList<QuantDiagnosticDto> CompilationWarnings,
    IReadOnlyList<QuantDiagnosticDto> RuntimeDiagnostics,
    IReadOnlyList<QuantMetricDto> Metrics,
    IReadOnlyList<QuantPlotDto> Plots,
    IReadOnlyList<QuantTradeDto> Trades,
    IReadOnlyList<QuantParameterDto> RuntimeParameters)
{
    /// <summary>Maps a strongly-typed <see cref="ScriptRunResult"/> into the JSON DTO.</summary>
    public static QuantRunResponse From(ScriptRunResult result) => new(
        Success: result.Success,
        ElapsedMs: result.Elapsed.TotalMilliseconds,
        CompileTimeMs: result.CompileTime.TotalMilliseconds,
        PeakMemoryBytes: result.PeakMemoryBytes,
        RuntimeError: result.RuntimeError,
        ConsoleOutput: result.ConsoleOutput,
        CompilationErrors: result.CompilationErrors
            .Select(d => new QuantDiagnosticDto(d.Severity, d.Message, d.Line, d.Column))
            .ToArray(),
        CompilationWarnings: (result.CompilationWarnings ?? Array.Empty<ScriptDiagnostic>())
            .Select(d => new QuantDiagnosticDto(d.Severity, d.Message, d.Line, d.Column))
            .ToArray(),
        RuntimeDiagnostics: result.RuntimeDiagnostics
            .Select(d => new QuantDiagnosticDto(d.Severity, d.Message, d.Line, d.Column))
            .ToArray(),
        Metrics: result.Metrics
            .Select(m => new QuantMetricDto(m.Key, m.Value))
            .ToArray(),
        Plots: result.Plots.Select(MapPlot).ToArray(),
        Trades: result.Trades
            .Select(t => new QuantTradeDto(
                Timestamp: t.Timestamp.ToString("O"),
                Symbol: t.Symbol,
                Side: t.Side,
                Quantity: t.Quantity,
                Price: t.Price,
                Commission: t.Commission,
                FillId: t.FillId.ToString("D"),
                OrderId: t.OrderId.ToString("D"),
                BacktestRunIndex: t.BacktestRunIndex))
            .ToArray(),
        RuntimeParameters: result.RuntimeParameters
            .Select(p => new QuantParameterDto(
                Name: p.Name,
                Label: p.Label,
                TypeName: p.TypeName,
                DefaultValue: FormatParameterValue(p.DefaultValue),
                Min: double.IsFinite(p.Min) ? p.Min : null,
                Max: double.IsFinite(p.Max) ? p.Max : null,
                Description: p.Description))
            .ToArray());

    /// <summary>Constructs an error response for unhandled exceptions.</summary>
    public static QuantRunResponse FromError(string message) => new(
        Success: false,
        ElapsedMs: 0,
        CompileTimeMs: 0,
        PeakMemoryBytes: 0,
        RuntimeError: message,
        ConsoleOutput: string.Empty,
        CompilationErrors: Array.Empty<QuantDiagnosticDto>(),
        CompilationWarnings: Array.Empty<QuantDiagnosticDto>(),
        RuntimeDiagnostics: Array.Empty<QuantDiagnosticDto>(),
        Metrics: Array.Empty<QuantMetricDto>(),
        Plots: Array.Empty<QuantPlotDto>(),
        Trades: Array.Empty<QuantTradeDto>(),
        RuntimeParameters: Array.Empty<QuantParameterDto>());

    private static string? FormatParameterValue(object? value) => value switch
    {
        null => null,
        bool boolean => boolean ? "true" : "false",
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString()
    };

    private static QuantPlotDto MapPlot(PlotRequest plot)
    {
        IReadOnlyList<QuantPlotPointDto>? series = plot.Series is null
            ? null
            : plot.Series
                .Select(p => new QuantPlotPointDto(p.Date.ToString("yyyy-MM-dd"), p.Value))
                .ToArray();

        IReadOnlyList<QuantPlotSeriesDto>? multi = plot.MultiSeries is null
            ? null
            : plot.MultiSeries
                .Select(s => new QuantPlotSeriesDto(
                    s.Label,
                    s.Values
                        .Select(p => new QuantPlotPointDto(p.Date.ToString("yyyy-MM-dd"), p.Value))
                        .ToArray()))
                .ToArray();

        IReadOnlyList<QuantPlotBarDto>? candlestick = plot.Candlestick is null
            ? null
            : plot.Candlestick
                .Select(b => new QuantPlotBarDto(
                    Date: b.Date.ToString("yyyy-MM-dd"),
                    Open: (double)b.Open,
                    High: (double)b.High,
                    Low: (double)b.Low,
                    Close: (double)b.Close,
                    Volume: (double)b.Volume))
                .ToArray();

        IReadOnlyList<IReadOnlyList<double>>? heatmap = plot.HeatmapData is null
            ? null
            : plot.HeatmapData.Select(row => (IReadOnlyList<double>)row.ToArray()).ToArray();

        return new QuantPlotDto(
            Title: plot.Title,
            Type: plot.Type.ToString(),
            Series: series,
            MultiSeries: multi,
            Candlestick: candlestick,
            HeatmapData: heatmap,
            HeatmapLabels: plot.HeatmapLabels);
    }
}

internal static class QuantRunParameterParser
{
    internal static IReadOnlyDictionary<string, object?> Normalize(
        IReadOnlyDictionary<string, object?>? parameters,
        IReadOnlyList<ParameterDescriptor> descriptors)
    {
        var descriptorTypes = descriptors
            .Where(static descriptor => !string.IsNullOrWhiteSpace(descriptor.Name))
            .GroupBy(static descriptor => descriptor.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.First().TypeName, StringComparer.OrdinalIgnoreCase);
        var normalized = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        foreach (var (name, suppliedValue) in parameters ?? new Dictionary<string, object?>())
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("QuantScript parameter names cannot be empty.", nameof(parameters));

            try
            {
                normalized[name] = descriptorTypes.TryGetValue(name, out var typeName)
                    ? ParseKnownValue(suppliedValue, typeName)
                    : ParseScalarValue(suppliedValue);
            }
            catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException or JsonException)
            {
                throw new ArgumentException(
                    $"QuantScript parameter '{name}' is malformed or outside the range of its declared type.",
                    nameof(parameters),
                    ex);
            }
        }

        return normalized;
    }

    private static object? ParseKnownValue(object? value, string? typeName)
    {
        var scalar = ParseScalarValue(value, preserveNumericText: true);
        if (scalar is null)
            throw new InvalidCastException("Declared parameters cannot be null overrides.");

        var text = scalar switch
        {
            string stringValue => stringValue,
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => scalar.ToString() ?? string.Empty
        };

        return (typeName ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "int" or "int32" => int.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture),
            "long" or "int64" => long.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture),
            "float" or "single" => ParseFiniteSingle(text),
            "double" => ParseFiniteDouble(text),
            "decimal" => decimal.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture),
            "bool" or "boolean" => scalar is bool boolean ? boolean : bool.Parse(text),
            "string" => scalar as string ?? throw new InvalidCastException("String parameters require a JSON string."),
            _ => ParseScalarValue(value)
        };
    }

    private static object? ParseScalarValue(object? value, bool preserveNumericText = false)
    {
        if (value is not JsonElement element)
            return value;

        return element.ValueKind switch
        {
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            JsonValueKind.String => element.GetString(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number when preserveNumericText => element.GetRawText(),
            JsonValueKind.Number when element.TryGetInt32(out var intValue) => intValue,
            JsonValueKind.Number when element.TryGetInt64(out var longValue) => longValue,
            JsonValueKind.Number when element.TryGetDecimal(out var decimalValue) => decimalValue,
            JsonValueKind.Number when element.TryGetDouble(out var doubleValue) && double.IsFinite(doubleValue) => doubleValue,
            _ => throw new JsonException("QuantScript parameters must be scalar JSON values.")
        };
    }

    private static float ParseFiniteSingle(string value)
    {
        var parsed = float.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);
        return float.IsFinite(parsed) ? parsed : throw new OverflowException("The value must be finite.");
    }

    private static double ParseFiniteDouble(string value)
    {
        var parsed = double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);
        return double.IsFinite(parsed) ? parsed : throw new OverflowException("The value must be finite.");
    }
}

internal static class QuantLabTemplates
{
    public static IReadOnlyList<QuantTemplateDto> All { get; } = new[]
    {
        new QuantTemplateDto(
            Id: "hello",
            Title: "Hello, Quant Lab",
            Description: "Print a message and a metric.",
            Source: """
                // Welcome to the Meridian Quant Lab.
                Print("Hello from the Quant Lab.");
                PrintMetric("answer", 42);
                """),
        new QuantTemplateDto(
            Id: "line-plot",
            Title: "Plot a sine wave",
            Description: "Emit a line plot built in pure C# (no market data required).",
            Source: """
                using System;
                using System.Collections.Generic;
                using Meridian.QuantScript.Plotting;

                var points = new List<(DateOnly Date, double Value)>();
                var start = new DateOnly(2026, 1, 1);
                for (var i = 0; i < 90; i++)
                {
                    points.Add((start.AddDays(i), Math.Sin(i / 6.0)));
                }

                PlotQueue.Current?.Enqueue(new PlotRequest("Sine wave", PlotType.Line, points));
                Print($"Emitted {points.Count} points.");
                """),
        new QuantTemplateDto(
            Id: "cumulative-return",
            Title: "Cumulative return + drawdown",
            Description: "Compute simple returns, cumulative growth, and drawdown for two synthetic series.",
            Source: """
                using System;
                using System.Collections.Generic;
                using Meridian.QuantScript.Plotting;

                static List<(DateOnly Date, double Value)> Build(double drift, double seed)
                {
                    var rng = new Random((int)seed);
                    var values = new List<(DateOnly, double)>();
                    var v = 100.0;
                    var date = new DateOnly(2026, 1, 1);
                    for (var i = 0; i < 250; i++)
                    {
                        v *= 1 + drift + (rng.NextDouble() - 0.5) * 0.02;
                        values.Add((date.AddDays(i), v));
                    }
                    return values;
                }

                var a = Build(0.0007, 1);
                var b = Build(0.0003, 2);

                PlotQueue.Current?.Enqueue(new PlotRequest(
                    "Equity curve",
                    PlotType.MultiLine,
                    MultiSeries: new (string, IReadOnlyList<(DateOnly, double)>)[]
                    {
                        ("Strategy A", a),
                        ("Strategy B", b)
                    }));

                PrintMetric("A final", a[^1].Value);
                PrintMetric("B final", b[^1].Value);
                """),
    };
}

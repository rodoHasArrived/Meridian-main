using System.Text.Json;
using Meridian.Backtesting.Sdk;
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

            var parameters = request.Parameters ?? new Dictionary<string, object?>();
            try
            {
                var result = await runner.RunAsync(request.Source, parameters, ct).ConfigureAwait(false);
                await RecordResearchRunsAsync(services, request, result, ct).ConfigureAwait(false);
                return Results.Json(QuantRunResponse.From(result), jsonOptions);
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
                    DefaultValue: p.DefaultValue?.ToString(),
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
        .WithName("GetQuantTemplates")
        .Produces(200);
    }

    /// <summary>
    /// Records any backtests the script ran into the shared strategy-run store.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Recording happens here rather than inside the script because a script executes in the
    /// QuantScript worker sandbox, whose only sanctioned route to host state is the typed
    /// market-data RPC seam. Handing that process a writable path into the run store would widen
    /// the isolation boundary the worker exists to enforce, so the host records the results the
    /// worker returned.
    /// </para>
    /// <para>
    /// This is a no-op unless the caller supplies a strategy identity and the host has registered a
    /// recorder, so existing callers are unaffected. Failures are logged and swallowed: a recording
    /// problem must not discard a completed run, and an unrecorded run simply carries no lineage.
    /// </para>
    /// </remarks>
    private static async Task RecordResearchRunsAsync(
        IServiceProvider services,
        QuantRunRequest request,
        ScriptRunResult result,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.StrategyId) || result.CapturedBacktests.Count == 0)
        {
            return;
        }

        var recorder = services.GetService<IResearchRunRecorder>();
        if (recorder is null)
        {
            return;
        }

        var descriptor = new ResearchRunDescriptor(
            StrategyId: request.StrategyId,
            StrategyName: string.IsNullOrWhiteSpace(request.StrategyName)
                ? request.StrategyId
                : request.StrategyName,
            CorrelationId: request.CorrelationId);

        foreach (var backtest in result.CapturedBacktests)
        {
            try
            {
                await recorder.RecordAsync(descriptor, backtest, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                services.GetService<ILoggerFactory>()?
                    .CreateLogger("QuantLabEndpoints")
                    .LogWarning(
                        ex,
                        "Could not record a Quant Lab backtest for strategy {StrategyId}",
                        Meridian.Execution.Logging.LogSanitizer.Sanitize(request.StrategyId));
            }
        }
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
}

/// <summary>Body of a POST to <c>/api/quant/run</c>.</summary>
/// <param name="Source">Script source to compile and execute.</param>
/// <param name="Parameters">Parameter overrides bound into the run.</param>
/// <param name="StrategyId">
/// Optional stable strategy identity. When supplied together with a recorder registration, any
/// backtest the script runs is recorded to the shared strategy-run store so the work carries the
/// same lineage as a Studio run. Omitted, the run executes exactly as before and is not recorded.
/// </param>
/// <param name="StrategyName">Optional display name for the recorded run.</param>
/// <param name="CorrelationId">
/// Optional pointer back to the artifact that produced the run — for a notebook, the notebook and
/// cell — so a recorded run can be traced to the work that created it.
/// </param>
public sealed record QuantRunRequest(
    string Source,
    IReadOnlyDictionary<string, object?>? Parameters,
    string? StrategyId = null,
    string? StrategyName = null,
    string? CorrelationId = null);

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
    decimal Commission);

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
                Commission: t.Commission))
            .ToArray(),
        RuntimeParameters: result.RuntimeParameters
            .Select(p => new QuantParameterDto(
                Name: p.Name,
                Label: p.Label,
                TypeName: p.TypeName,
                DefaultValue: p.DefaultValue?.ToString(),
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
        RuntimeDiagnostics: Array.Empty<QuantDiagnosticDto>(),
        Metrics: Array.Empty<QuantMetricDto>(),
        Plots: Array.Empty<QuantPlotDto>(),
        Trades: Array.Empty<QuantTradeDto>(),
        RuntimeParameters: Array.Empty<QuantParameterDto>());

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

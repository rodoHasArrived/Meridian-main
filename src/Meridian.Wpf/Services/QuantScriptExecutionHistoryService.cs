using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Meridian.Backtesting.Sdk;
using Meridian.QuantScript.Documents;
using Meridian.Storage.Archival;
using Meridian.Wpf.Models;

namespace Meridian.Wpf.Services;

public sealed record QuantScriptExecutionRecordRequest(
    string DocumentTitle,
    string DocumentPath,
    QuantScriptDocumentKind DocumentKind,
    bool Success,
    Dictionary<string, string> ParameterSnapshot,
    List<QuantScriptResolvedParameterDescriptorRecord> RuntimeParameters,
    string ConsoleExcerpt,
    List<QuantScriptExecutionMetricRecord> Metrics,
    List<string> PlotTitles,
    IReadOnlyList<BacktestResult> CapturedBacktests);

public sealed class QuantScriptExecutionHistoryService
{
    private readonly ConfigService _configService;
    private readonly StrategyRunWorkspaceService _strategyRunWorkspaceService;
    private readonly ILogger<QuantScriptExecutionHistoryService> _logger;

    public QuantScriptExecutionHistoryService(
        ConfigService configService,
        StrategyRunWorkspaceService strategyRunWorkspaceService,
        ILogger<QuantScriptExecutionHistoryService> logger)
    {
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));
        _strategyRunWorkspaceService = strategyRunWorkspaceService ?? throw new ArgumentNullException(nameof(strategyRunWorkspaceService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public static string BuildQuantScriptStrategyId(string documentTitle)
        => $"quantscript-{StrategyRunWorkspaceService.SlugifyStrategyName(documentTitle)}";

    public async Task<IReadOnlyList<QuantScriptExecutionRecord>> GetHistoryAsync(CancellationToken ct = default)
    {
        var historyDirectory = await ResolveHistoryDirectoryAsync().ConfigureAwait(false);
        if (!Directory.Exists(historyDirectory))
            return Array.Empty<QuantScriptExecutionRecord>();

        var records = new List<QuantScriptExecutionRecord>();
        foreach (var path in Directory.GetFiles(historyDirectory, "*.json", SearchOption.TopDirectoryOnly))
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                await using var stream = File.OpenRead(path);
                var record = await JsonSerializer.DeserializeAsync(
                    stream,
                    QuantScriptStorageJsonContext.Default.QuantScriptExecutionRecord,
                    ct).ConfigureAwait(false);

                if (record is not null)
                    records.Add(record);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Failed to read QuantScript execution record from {Path}", path);
            }
        }

        return records
            .OrderByDescending(static record => record.ExecutedAtUtc)
            .ToList();
    }

    public async Task<QuantScriptExecutionRecord> RecordExecutionAsync(
        QuantScriptExecutionRecordRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var executionId = Guid.NewGuid().ToString("N");
        var executedAtUtc = DateTimeOffset.UtcNow;
        var mirroredRunId = default(string);
        var warning = default(string);

        if (request.CapturedBacktests.Count == 1)
        {
            var backtest = request.CapturedBacktests[0];
            var publicationParameters = new Dictionary<string, string>(
                request.ParameterSnapshot,
                StringComparer.OrdinalIgnoreCase)
            {
                ["documentPath"] = request.DocumentPath ?? string.Empty,
                ["documentKind"] = request.DocumentKind.ToString(),
                ["executionId"] = executionId
            };

            mirroredRunId = await _strategyRunWorkspaceService.RecordBacktestRunAsync(
                backtest.Request,
                backtest,
                new BacktestRunPublicationOptions(
                    StrategyName: request.DocumentTitle,
                    StrategyId: BuildQuantScriptStrategyId(request.DocumentTitle),
                    AdditionalParameters: publicationParameters),
                ct).ConfigureAwait(false);
        }
        else if (request.CapturedBacktests.Count > 1)
        {
            warning = "Shared Research mirroring was skipped because this execution captured more than one backtest. Run the backtests separately to compare them in Strategy Runs.";
        }

        var record = new QuantScriptExecutionRecord(
            ExecutionId: executionId,
            DocumentTitle: request.DocumentTitle,
            DocumentPath: request.DocumentPath ?? string.Empty,
            DocumentKind: request.DocumentKind,
            ExecutedAtUtc: executedAtUtc,
            Success: request.Success,
            ParameterSnapshot: request.ParameterSnapshot,
            RuntimeParameters: request.RuntimeParameters,
            ConsoleExcerpt: request.ConsoleExcerpt,
            Metrics: request.Metrics,
            PlotTitles: request.PlotTitles,
            CapturedBacktestCount: request.CapturedBacktests.Count,
            MirroredRunId: mirroredRunId,
            Warning: warning);

        var historyDirectory = await ResolveHistoryDirectoryAsync().ConfigureAwait(false);
        var path = Path.Combine(historyDirectory, $"{record.ExecutionId}.json");
        var json = JsonSerializer.Serialize(record, QuantScriptStorageJsonContext.Default.QuantScriptExecutionRecord);
        await AtomicFileWriter.WriteAsync(path, json, ct).ConfigureAwait(false);

        return record;
    }

    private async Task<string> ResolveHistoryDirectoryAsync()
    {
        var config = await _configService.LoadConfigAsync().ConfigureAwait(false);
        var dataRoot = _configService.ResolveDataRoot(config);
        return Path.Combine(dataRoot, "_quantscript", "runs");
    }

    public static string ConvertValueToString(object? value)
    {
        if (value is null)
            return string.Empty;

        return value switch
        {
            DateOnly date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            DateTime dateTime => dateTime.ToString("O", CultureInfo.InvariantCulture),
            DateTimeOffset dateTimeOffset => dateTimeOffset.ToString("O", CultureInfo.InvariantCulture),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty
        };
    }
}

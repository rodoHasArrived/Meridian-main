using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Meridian.Ui.Services;

/// <summary>
/// Service for integrating with QuantConnect Lean Engine for backtesting.
/// Enables configuration, data synchronization, and backtest execution.
/// </summary>
public sealed class LeanIntegrationService
{
    private static readonly Lazy<LeanIntegrationService> _instance = new(() => new LeanIntegrationService());
    private readonly ApiClientService _apiClient;

    public static LeanIntegrationService Instance => _instance.Value;

    private LeanIntegrationService()
    {
        _apiClient = ApiClientService.Instance;
    }

    /// <summary>
    /// Raised when backtest status changes.
    /// </summary>
#pragma warning disable CS0067 // Event is declared but never raised in this implementation
    public event EventHandler<BacktestStatusChangedEventArgs>? BacktestStatusChanged;
#pragma warning restore CS0067

    /// <summary>
    /// Gets the current Lean integration status.
    /// </summary>
    public async Task<LeanStatus> GetStatusAsync(CancellationToken ct = default)
    {
        var response = await _apiClient.GetWithResponseAsync<LeanStatus>(
            "/api/lean/status",
            ct);

        return response.Data ?? new LeanStatus();
    }

    /// <summary>
    /// Gets the Lean configuration.
    /// </summary>
    public async Task<LeanConfiguration> GetConfigurationAsync(CancellationToken ct = default)
    {
        var response = await _apiClient.GetWithResponseAsync<LeanConfiguration>(
            "/api/lean/config",
            ct);

        return response.Data ?? new LeanConfiguration();
    }

    /// <summary>
    /// Updates the Lean configuration.
    /// </summary>
    public async Task<bool> UpdateConfigurationAsync(
        LeanConfigurationUpdate config,
        CancellationToken ct = default)
    {
        var response = await _apiClient.PostWithResponseAsync<object>(
            "/api/lean/config",
            config,
            ct);

        return response.Success;
    }

    /// <summary>
    /// Verifies the Lean installation.
    /// </summary>
    public async Task<LeanVerificationResult> VerifyInstallationAsync(CancellationToken ct = default)
    {
        var response = await _apiClient.PostWithResponseAsync<LeanVerificationResponse>(
            "/api/lean/verify",
            null,
            ct);

        if (response.Success && response.Data != null)
        {
            return new LeanVerificationResult
            {
                Success = response.Data.IsValid,
                LeanPath = response.Data.LeanPath,
                Version = response.Data.Version,
                DataPath = response.Data.DataPath,
                Errors = response.Data.Errors?.ToList() ?? new List<string>(),
                Warnings = response.Data.Warnings?.ToList() ?? new List<string>()
            };
        }

        return new LeanVerificationResult
        {
            Success = false,
            Errors = new List<string> { response.ErrorMessage ?? "Verification failed" }
        };
    }

    /// <summary>
    /// Gets available algorithms.
    /// </summary>
    public async Task<AlgorithmListResult> GetAlgorithmsAsync(CancellationToken ct = default)
    {
        var response = await _apiClient.GetWithResponseAsync<AlgorithmListResponse>(
            "/api/lean/algorithms",
            ct);

        if (response.Success && response.Data != null)
        {
            return new AlgorithmListResult
            {
                Success = true,
                Algorithms = response.Data.Algorithms?.ToList() ?? new List<AlgorithmInfo>()
            };
        }

        return new AlgorithmListResult
        {
            Success = false,
            Error = response.ErrorMessage ?? "Failed to get algorithms"
        };
    }

    /// <summary>
    /// Syncs collected data to Lean format.
    /// </summary>
    public async Task<DataSyncResult> SyncDataAsync(
        DataSyncOptions options,
        CancellationToken ct = default)
    {
        var response = await _apiClient.PostWithResponseAsync<DataSyncResponse>(
            "/api/lean/sync",
            new
            {
                symbols = options.Symbols,
                fromDate = options.FromDate?.ToString(FormatHelpers.IsoDateFormat),
                toDate = options.ToDate?.ToString(FormatHelpers.IsoDateFormat),
                resolution = options.Resolution,
                overwrite = options.Overwrite
            },
            ct);

        if (response.Success && response.Data != null)
        {
            return new DataSyncResult
            {
                Success = response.Data.Success,
                SymbolsSynced = response.Data.SymbolsSynced,
                FilesCreated = response.Data.FilesCreated,
                BytesWritten = response.Data.BytesWritten,
                Errors = response.Data.Errors?.ToList() ?? new List<string>()
            };
        }

        return new DataSyncResult
        {
            Success = false,
            Errors = new List<string> { response.ErrorMessage ?? "Data sync failed" }
        };
    }

    /// <summary>
    /// Gets data synchronization status.
    /// </summary>
    public async Task<DataSyncStatus> GetDataSyncStatusAsync(CancellationToken ct = default)
    {
        var response = await _apiClient.GetWithResponseAsync<DataSyncStatus>(
            "/api/lean/sync/status",
            ct);

        return response.Data ?? new DataSyncStatus();
    }

    /// <summary>
    /// Starts a backtest.
    /// </summary>
    public async Task<BacktestStartResult> StartBacktestAsync(
        BacktestOptions options,
        CancellationToken ct = default)
    {
        var response = await _apiClient.PostWithResponseAsync<BacktestStartResponse>(
            "/api/lean/backtest/start",
            new
            {
                algorithmPath = options.AlgorithmPath,
                algorithmName = options.AlgorithmName,
                startDate = options.StartDate?.ToString(FormatHelpers.IsoDateFormat),
                endDate = options.EndDate?.ToString(FormatHelpers.IsoDateFormat),
                initialCapital = options.InitialCapital,
                parameters = options.Parameters
            },
            ct);

        if (response.Success && response.Data != null)
        {
            return new BacktestStartResult
            {
                Success = true,
                BacktestId = response.Data.BacktestId
            };
        }

        return new BacktestStartResult
        {
            Success = false,
            Error = response.ErrorMessage ?? "Failed to start backtest"
        };
    }

    /// <summary>
    /// Gets backtest status.
    /// </summary>
    public async Task<BacktestStatus> GetBacktestStatusAsync(string backtestId, CancellationToken ct = default)
    {
        var response = await _apiClient.GetWithResponseAsync<BacktestStatus>(
            $"/api/lean/backtest/{backtestId}/status",
            ct);

        return response.Data ?? new BacktestStatus { State = BacktestState.Unknown };
    }

    /// <summary>
    /// Gets backtest results.
    /// </summary>
    public async Task<BacktestResults> GetBacktestResultsAsync(string backtestId, CancellationToken ct = default)
    {
        var response = await _apiClient.GetWithResponseAsync<BacktestResults>(
            $"/api/lean/backtest/{backtestId}/results",
            ct);

        return response.Data ?? new BacktestResults();
    }

    /// <summary>
    /// Stops a running backtest.
    /// </summary>
    public async Task<bool> StopBacktestAsync(string backtestId, CancellationToken ct = default)
    {
        var response = await _apiClient.PostWithResponseAsync<object>(
            $"/api/lean/backtest/{backtestId}/stop",
            null,
            ct);

        return response.Success;
    }

    /// <summary>
    /// Gets backtest history.
    /// </summary>
    public async Task<BacktestHistoryResult> GetBacktestHistoryAsync(
        int limit = 20,
        CancellationToken ct = default)
    {
        var response = await _apiClient.GetWithResponseAsync<BacktestHistoryResponse>(
            $"/api/lean/backtest/history?limit={limit}",
            ct);

        if (response.Success && response.Data != null)
        {
            return new BacktestHistoryResult
            {
                Success = true,
                Backtests = response.Data.Backtests?.ToList() ?? new List<BacktestSummary>()
            };
        }

        return new BacktestHistoryResult
        {
            Success = false,
            Error = response.ErrorMessage ?? "Failed to get backtest history"
        };
    }

    /// <summary>
    /// Deletes a backtest and its results.
    /// </summary>
    public async Task<bool> DeleteBacktestAsync(string backtestId, CancellationToken ct = default)
    {
        var response = await _apiClient.PostWithResponseAsync<object>(
            $"/api/lean/backtest/{backtestId}/delete",
            null,
            ct);

        return response.Success;
    }

    /// <summary>
    /// Gets the current auto-export status and configuration.
    /// </summary>
    public async Task<LeanAutoExportStatus> GetAutoExportStatusAsync(CancellationToken ct = default)
    {
        var response = await _apiClient.GetWithResponseAsync<LeanAutoExportStatus>(
            "/api/lean/auto-export",
            ct);

        return response.Data ?? new LeanAutoExportStatus();
    }

    /// <summary>
    /// Configures the Lean auto-export service.
    /// </summary>
    public async Task<bool> ConfigureAutoExportAsync(
        LeanAutoExportConfigureOptions options,
        CancellationToken ct = default)
    {
        var response = await _apiClient.PostWithResponseAsync<object>(
            "/api/lean/auto-export/configure",
            new
            {
                enabled = options.Enabled,
                leanDataPath = options.LeanDataPath,
                intervalSeconds = options.IntervalSeconds,
                symbols = options.Symbols
            },
            ct);

        return response.Success;
    }

    /// <summary>
    /// Ingests Lean backtest results from a local results file.
    /// </summary>
    public async Task<LeanResultsIngestResult> IngestBacktestResultsAsync(
        string resultsFilePath,
        string? backtestId = null,
        string? algorithmName = null,
        CancellationToken ct = default)
    {
        var response = await _apiClient.PostWithResponseAsync<LeanResultsIngestResult>(
            "/api/lean/results/ingest",
            new
            {
                resultsFilePath,
                backtestId,
                algorithmName
            },
            ct);

        if (response.Success && response.Data != null)
            return response.Data;

        return new LeanResultsIngestResult
        {
            Success = false,
            Error = response.ErrorMessage ?? "Results ingestion failed"
        };
    }

    /// <summary>
    /// Resolves the Lean ticker and data path components for a given Meridian symbol.
    /// </summary>
    public async Task<LeanSymbolMappingResult> GetSymbolMappingAsync(
        IEnumerable<string> symbols,
        CancellationToken ct = default)
    {
        var joined = string.Join(",", symbols);
        var response = await _apiClient.GetWithResponseAsync<LeanSymbolMappingResult>(
            $"/api/lean/symbol-map?symbols={Uri.EscapeDataString(joined)}",
            ct);

        return response.Data ?? new LeanSymbolMappingResult();
    }
}


public sealed class BacktestStatusChangedEventArgs : EventArgs
{
    public string BacktestId { get; set; } = string.Empty;
    public BacktestState State { get; set; }
    public double Progress { get; set; }
}



public sealed class LeanStatus
{
    public bool IsInstalled { get; set; }
    public bool IsConfigured { get; set; }
    public bool DataSyncEnabled { get; set; }
    public string? Version { get; set; }
    public DateTime? LastSync { get; set; }
    public int SymbolsSynced { get; set; }
}

public sealed class LeanConfiguration
{
    public string? LeanPath { get; set; }
    public string? DataPath { get; set; }
    public string? ResultsPath { get; set; }
    public bool AutoSync { get; set; }
    public string? DefaultResolution { get; set; }
    public List<string>? SyncSymbols { get; set; }
}

public sealed class LeanConfigurationUpdate
{
    public string? LeanPath { get; set; }
    public string? DataPath { get; set; }
    public string? ResultsPath { get; set; }
    public bool? AutoSync { get; set; }
    public string? DefaultResolution { get; set; }
    public List<string>? SyncSymbols { get; set; }
}

public sealed class LeanVerificationResult
{
    public bool Success { get; set; }
    public string? LeanPath { get; set; }
    public string? Version { get; set; }
    public string? DataPath { get; set; }
    public List<string> Errors { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}



public sealed class AlgorithmInfo
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
    public DateTime LastModified { get; set; }
    public bool IsValid { get; set; }
}

public sealed class AlgorithmListResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public List<AlgorithmInfo> Algorithms { get; set; } = new();
}



public sealed class DataSyncOptions
{
    public List<string>? Symbols { get; set; }
    public DateOnly? FromDate { get; set; }
    public DateOnly? ToDate { get; set; }
    public string Resolution { get; set; } = "Daily";
    public bool Overwrite { get; set; }
}

public sealed class DataSyncResult
{
    public bool Success { get; set; }
    public int SymbolsSynced { get; set; }
    public int FilesCreated { get; set; }
    public long BytesWritten { get; set; }
    public List<string> Errors { get; set; } = new();
}

public sealed class DataSyncStatus
{
    public bool IsSyncing { get; set; }
    public double Progress { get; set; }
    public string? CurrentSymbol { get; set; }
    public int SymbolsCompleted { get; set; }
    public int TotalSymbols { get; set; }
}



public sealed class BacktestOptions
{
    public string? AlgorithmPath { get; set; }
    public string? AlgorithmName { get; set; }
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public decimal InitialCapital { get; set; } = 100000m;
    public Dictionary<string, string>? Parameters { get; set; }
}

public sealed class BacktestStartResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public string? BacktestId { get; set; }
}

public sealed class BacktestStatus
{
    public string? BacktestId { get; set; }
    public BacktestState State { get; set; }
    public double Progress { get; set; }
    public DateTime? CurrentDate { get; set; }
    public DateTime? StartedAt { get; set; }
    public TimeSpan Elapsed { get; set; }
    public string? Error { get; set; }
}

public enum BacktestState : byte
{
    Unknown,
    Initializing,
    Running,
    Completed,
    Failed,
    Cancelled
}

public sealed class BacktestResults
{
    public string? BacktestId { get; set; }
    public string? AlgorithmName { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public decimal InitialCapital { get; set; }
    public decimal FinalCapital { get; set; }
    public decimal TotalReturn { get; set; }
    public decimal AnnualizedReturn { get; set; }
    public decimal SharpeRatio { get; set; }
    public decimal MaxDrawdown { get; set; }
    public int TotalTrades { get; set; }
    public decimal WinRate { get; set; }
    public decimal ProfitFactor { get; set; }
    public List<BacktestTradeRecord>? Trades { get; set; }
    public List<EquityPoint>? EquityCurve { get; set; }
}

public sealed class BacktestTradeRecord
{
    public string Symbol { get; set; } = string.Empty;
    public DateTime EntryTime { get; set; }
    public DateTime ExitTime { get; set; }
    public string Direction { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal EntryPrice { get; set; }
    public decimal ExitPrice { get; set; }
    public decimal ProfitLoss { get; set; }
    public decimal ReturnPercent { get; set; }
}

public sealed class EquityPoint
{
    public DateTime Date { get; set; }
    public decimal Equity { get; set; }
}

public sealed class BacktestSummary
{
    public string BacktestId { get; set; } = string.Empty;
    public string AlgorithmName { get; set; } = string.Empty;
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public BacktestState State { get; set; }
    public decimal? TotalReturn { get; set; }
    public decimal? SharpeRatio { get; set; }
}

public sealed class BacktestHistoryResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public List<BacktestSummary> Backtests { get; set; } = new();
}



public sealed class LeanVerificationResponse
{
    public bool IsValid { get; set; }
    public string? LeanPath { get; set; }
    public string? Version { get; set; }
    public string? DataPath { get; set; }
    public string[]? Errors { get; set; }
    public string[]? Warnings { get; set; }
}

public sealed class AlgorithmListResponse
{
    public List<AlgorithmInfo>? Algorithms { get; set; }
}

public sealed class DataSyncResponse
{
    public bool Success { get; set; }
    public int SymbolsSynced { get; set; }
    public int FilesCreated { get; set; }
    public long BytesWritten { get; set; }
    public string[]? Errors { get; set; }
}

public sealed class BacktestStartResponse
{
    public string? BacktestId { get; set; }
}

public sealed class BacktestHistoryResponse
{
    public List<BacktestSummary>? Backtests { get; set; }
}



public sealed class LeanAutoExportStatus
{
    public bool Available { get; set; }
    public bool Enabled { get; set; }
    public string? LeanDataPath { get; set; }
    public int IntervalSeconds { get; set; }
    public DateTimeOffset? LastExportAt { get; set; }
    public DateTimeOffset? LastExportError { get; set; }
    public string? LastErrorMessage { get; set; }
    public long TotalFilesExported { get; set; }
    public long TotalBytesExported { get; set; }
}

public sealed class LeanAutoExportConfigureOptions
{
    public bool? Enabled { get; set; }
    public string? LeanDataPath { get; set; }
    public int IntervalSeconds { get; set; }
    public List<string>? Symbols { get; set; }
}



public sealed class LeanResultsIngestResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public string? BacktestId { get; set; }
    public string? AlgorithmName { get; set; }
    public decimal? TotalReturn { get; set; }
    public decimal? SharpeRatio { get; set; }
    public int? TotalTrades { get; set; }
    public string? Message { get; set; }
}



public sealed class LeanSymbolMapping
{
    public string MdcSymbol { get; set; } = string.Empty;
    public string LeanTicker { get; set; } = string.Empty;
    public string SecurityType { get; set; } = string.Empty;
    public string Market { get; set; } = string.Empty;
}

public sealed class LeanSymbolMappingResult
{
    public List<LeanSymbolMapping> Mappings { get; set; } = new();
    public int Total { get; set; }
}


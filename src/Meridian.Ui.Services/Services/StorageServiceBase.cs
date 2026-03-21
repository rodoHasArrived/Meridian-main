using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Meridian.Contracts.Api;

namespace Meridian.Ui.Services;

/// <summary>
/// Base class for storage services providing platform-agnostic API delegation methods.
/// Accepts <see cref="ApiClientService"/> via constructor for testability and to avoid
/// coupling to a static singleton.
/// WPF StorageService inherits from this class and adds platform-specific functionality
/// (e.g., Brush-based styling).
/// </summary>
public class StorageServiceBase
{
    private readonly ApiClientService _apiClient;

    /// <summary>
    /// Creates a new instance with the specified API client.
    /// </summary>
    protected StorageServiceBase(ApiClientService apiClient)
    {
        _apiClient = apiClient;
    }

    /// <summary>
    /// Gets storage statistics summary.
    /// </summary>
    public async Task<StorageStatsSummary?> GetStorageStatsAsync(CancellationToken ct = default)
    {
        return await _apiClient.GetAsync<StorageStatsSummary>(UiApiRoutes.StorageStats, ct);
    }

    /// <summary>
    /// Gets detailed storage usage by category.
    /// </summary>
    public async Task<List<StorageCategory>?> GetStorageBreakdownAsync(CancellationToken ct = default)
    {
        return await _apiClient.GetAsync<List<StorageCategory>>(UiApiRoutes.StorageBreakdown, ct);
    }

    /// <summary>
    /// Gets symbol-specific information.
    /// </summary>
    public async Task<SymbolStorageInfo?> GetSymbolInfoAsync(string symbol, CancellationToken ct = default)
    {
        return await _apiClient.GetAsync<SymbolStorageInfo>(
            UiApiRoutes.WithParam(UiApiRoutes.StorageSymbolInfo, "symbol", symbol), ct);
    }

    /// <summary>
    /// Gets storage statistics for a specific symbol.
    /// </summary>
    public async Task<SymbolStorageStats?> GetSymbolStorageStatsAsync(string symbol, CancellationToken ct = default)
    {
        return await _apiClient.GetAsync<SymbolStorageStats>(
            UiApiRoutes.WithParam(UiApiRoutes.StorageSymbolStats, "symbol", symbol), ct);
    }

    /// <summary>
    /// Gets the folder path for a symbol's data.
    /// </summary>
    public async Task<string?> GetSymbolFolderPathAsync(string symbol, CancellationToken ct = default)
    {
        var response = await _apiClient.GetAsync<SymbolPathResponse>(
            UiApiRoutes.WithParam(UiApiRoutes.StorageSymbolPath, "symbol", symbol), ct);
        return response?.FolderPath;
    }

    /// <summary>
    /// Gets storage health report.
    /// </summary>
    public async Task<StorageHealthReport?> GetStorageHealthAsync(CancellationToken ct = default)
    {
        return await _apiClient.GetAsync<StorageHealthReport>(UiApiRoutes.StorageHealth, ct);
    }

    /// <summary>
    /// Gets files that need cleanup based on retention policies.
    /// </summary>
    public async Task<List<CleanupCandidate>?> GetCleanupCandidatesAsync(CancellationToken ct = default)
    {
        return await _apiClient.GetAsync<List<CleanupCandidate>>(UiApiRoutes.StorageCleanupCandidates, ct);
    }

    /// <summary>
    /// Runs storage cleanup.
    /// </summary>
    public async Task<CleanupResult?> RunCleanupAsync(bool dryRun = true, CancellationToken ct = default)
    {
        return await _apiClient.PostAsync<CleanupResult>(
            UiApiRoutes.StorageCleanup,
            new { dryRun },
            ct);
    }

    /// <summary>
    /// Gets archive statistics.
    /// </summary>
    public async Task<ArchiveStats?> GetArchiveStatsAsync(CancellationToken ct = default)
    {
        return await _apiClient.GetAsync<ArchiveStats>(UiApiRoutes.StorageArchiveStats, ct);
    }

    /// <summary>
    /// Formats a byte count into a human-readable string (e.g., "1.5 GB").
    /// </summary>
    public static string FormatBytes(long bytes) => FormatHelpers.FormatBytes(bytes);

    /// <summary>
    /// Gets the icon glyph for a data type.
    /// </summary>
    public static string GetFileIcon(string dataType)
    {
        return dataType.ToLowerInvariant() switch
        {
            "trades" => "\uE8AB",      // Exchange icon
            "quotes" => "\uE8D4",      // Document icon
            "depth" => "\uE8A1",       // List icon
            "bars" => "\uE9D9",        // Chart icon
            "parquet" => "\uE7C3",     // Database icon
            _ => "\uE8A5"              // File icon
        };
    }
}

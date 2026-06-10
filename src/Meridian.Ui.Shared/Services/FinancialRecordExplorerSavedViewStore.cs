using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Contracts.Workstation;
using Meridian.Storage.Archival;
using Microsoft.Extensions.Logging;

namespace Meridian.Ui.Shared.Services;

public interface IFinancialRecordExplorerSavedViewStore
{
    Task<IReadOnlyList<FinancialRecordExplorerSavedViewDto>> LoadAsync(string explorerId, CancellationToken ct = default);

    Task<FinancialRecordExplorerSavedViewDto> UpsertAsync(
        string explorerId,
        FinancialRecordExplorerSavedViewDto view,
        CancellationToken ct = default);
}

/// <summary>
/// JSON-backed operator saved-view store rooted under the workstation data directory.
/// </summary>
public sealed class FileFinancialRecordExplorerSavedViewStore : IFinancialRecordExplorerSavedViewStore
{
    private const int SnapshotVersion = 1;

    private readonly string _snapshotPath;
    private readonly ILogger<FileFinancialRecordExplorerSavedViewStore> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public FileFinancialRecordExplorerSavedViewStore(
        string workstationDataRoot,
        ILogger<FileFinancialRecordExplorerSavedViewStore> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workstationDataRoot);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        var explorerDirectory = Path.Combine(workstationDataRoot, "explorers");
        Directory.CreateDirectory(explorerDirectory);
        _snapshotPath = Path.Combine(explorerDirectory, "financial-record-explorer-saved-views.json");
    }

    public async Task<IReadOnlyList<FinancialRecordExplorerSavedViewDto>> LoadAsync(
        string explorerId,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(explorerId);

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var snapshot = await LoadCoreAsync(ct).ConfigureAwait(false);
            return snapshot.Views
                .Where(view => string.Equals(view.ExplorerId, explorerId, StringComparison.OrdinalIgnoreCase))
                .Select(static view => view.View)
                .OrderBy(static view => view.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static view => view.SavedViewId, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<FinancialRecordExplorerSavedViewDto> UpsertAsync(
        string explorerId,
        FinancialRecordExplorerSavedViewDto view,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(explorerId);
        ArgumentNullException.ThrowIfNull(view);

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var snapshot = await LoadCoreAsync(ct).ConfigureAwait(false);
            var views = snapshot.Views
                .Where(item =>
                    !string.Equals(item.ExplorerId, explorerId, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(item.View.SavedViewId, view.SavedViewId, StringComparison.OrdinalIgnoreCase))
                .ToList();
            views.Add(new FinancialRecordExplorerSavedViewStoreItem(explorerId, view));

            var next = new FinancialRecordExplorerSavedViewSnapshot(
                SnapshotVersion,
                views
                    .OrderBy(static item => item.ExplorerId, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(static item => item.View.Name, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(static item => item.View.SavedViewId, StringComparer.OrdinalIgnoreCase)
                    .ToArray());

            var json = JsonSerializer.Serialize(
                next,
                FinancialRecordExplorerSavedViewJsonContext.Default.FinancialRecordExplorerSavedViewSnapshot);
            await AtomicFileWriter.WriteAsync(_snapshotPath, json, ct).ConfigureAwait(false);
            return view;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<FinancialRecordExplorerSavedViewSnapshot> LoadCoreAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (!File.Exists(_snapshotPath))
        {
            return new FinancialRecordExplorerSavedViewSnapshot(SnapshotVersion, []);
        }

        try
        {
            await using var stream = File.OpenRead(_snapshotPath);
            var snapshot = await JsonSerializer.DeserializeAsync(
                    stream,
                    FinancialRecordExplorerSavedViewJsonContext.Default.FinancialRecordExplorerSavedViewSnapshot,
                    ct)
                .ConfigureAwait(false);

            if (snapshot is null)
            {
                return new FinancialRecordExplorerSavedViewSnapshot(SnapshotVersion, []);
            }

            if (snapshot.Version != SnapshotVersion)
            {
                throw new InvalidOperationException(
                    $"Financial record explorer saved-view snapshot version {snapshot.Version} is not supported. Expected version {SnapshotVersion}: {_snapshotPath}");
            }

            return snapshot;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Financial record explorer saved-view snapshot is not valid JSON: {Path}", _snapshotPath);
            throw new InvalidOperationException($"Financial record explorer saved-view snapshot is invalid: {_snapshotPath}", ex);
        }
    }
}

internal sealed record FinancialRecordExplorerSavedViewStoreItem(
    string ExplorerId,
    FinancialRecordExplorerSavedViewDto View);

internal sealed record FinancialRecordExplorerSavedViewSnapshot(
    int Version,
    IReadOnlyList<FinancialRecordExplorerSavedViewStoreItem> Views);

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, WriteIndented = true)]
[JsonSerializable(typeof(FinancialRecordExplorerSavedViewSnapshot))]
internal sealed partial class FinancialRecordExplorerSavedViewJsonContext : JsonSerializerContext;

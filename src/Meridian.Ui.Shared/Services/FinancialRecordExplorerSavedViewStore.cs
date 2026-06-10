using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Contracts.Workstation;
using Meridian.Storage.Archival;
using Microsoft.Extensions.Logging;

namespace Meridian.Ui.Shared.Services;

public interface IFinancialRecordExplorerSavedViewStore
{
    Task<IReadOnlyList<FinancialRecordExplorerSavedViewDto>> LoadAsync(string explorerId, CancellationToken ct = default);

    Task<FinancialRecordExplorerSavedViewDto> SaveAsync(
        string explorerId,
        FinancialRecordExplorerSavedViewDto savedView,
        CancellationToken ct = default);
}

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
                .Select(static view => view.SavedView)
                .OrderBy(static view => view.Label, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<FinancialRecordExplorerSavedViewDto> SaveAsync(
        string explorerId,
        FinancialRecordExplorerSavedViewDto savedView,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(explorerId);
        ArgumentNullException.ThrowIfNull(savedView);

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var snapshot = await LoadCoreAsync(ct).ConfigureAwait(false);
            var retained = snapshot.Views
                .Where(view =>
                    !string.Equals(view.ExplorerId, explorerId, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(view.SavedView.ViewId, savedView.ViewId, StringComparison.OrdinalIgnoreCase))
                .Append(new FinancialRecordExplorerSavedViewRecord(explorerId, savedView with { IsSystem = false, IsActive = false }))
                .OrderBy(static view => view.ExplorerId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static view => view.SavedView.Label, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            await PersistAsync(new FinancialRecordExplorerSavedViewSnapshot(SnapshotVersion, retained), ct)
                .ConfigureAwait(false);
            return savedView with { IsSystem = false, IsActive = false };
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
                    $"Financial record explorer saved-view snapshot version {snapshot.Version} is not supported. Expected {SnapshotVersion}: {_snapshotPath}");
            }

            return snapshot;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Financial record explorer saved-view snapshot is not valid JSON: {Path}", _snapshotPath);
            throw new InvalidOperationException($"Financial record explorer saved-view snapshot is invalid: {_snapshotPath}", ex);
        }
    }

    private async Task PersistAsync(FinancialRecordExplorerSavedViewSnapshot snapshot, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var json = JsonSerializer.Serialize(
            snapshot,
            FinancialRecordExplorerSavedViewJsonContext.Default.FinancialRecordExplorerSavedViewSnapshot);
        await AtomicFileWriter.WriteAsync(_snapshotPath, json, ct).ConfigureAwait(false);
    }
}

internal sealed record FinancialRecordExplorerSavedViewRecord(
    string ExplorerId,
    FinancialRecordExplorerSavedViewDto SavedView);

internal sealed record FinancialRecordExplorerSavedViewSnapshot(
    int Version,
    IReadOnlyList<FinancialRecordExplorerSavedViewRecord> Views);

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, WriteIndented = true)]
[JsonSerializable(typeof(FinancialRecordExplorerSavedViewSnapshot))]
internal sealed partial class FinancialRecordExplorerSavedViewJsonContext : JsonSerializerContext;

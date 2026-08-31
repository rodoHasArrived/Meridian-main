using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Contracts.Workstation;
using Meridian.Storage.Store;
using Microsoft.Extensions.Logging;

namespace Meridian.Ui.Shared.Services;

public interface IFinancialRecordExplorerSavedViewStore
{
    Task<IReadOnlyList<FinancialRecordExplorerSavedViewDto>> LoadAsync(
        string tenantId,
        string explorerId,
        CancellationToken ct = default);

    Task<FinancialRecordExplorerSavedViewDto> SaveAsync(
        string tenantId,
        string explorerId,
        FinancialRecordExplorerSavedViewDto savedView,
        CancellationToken ct = default);
}

public sealed class FileFinancialRecordExplorerSavedViewStore
    : JsonFileSnapshotStore<FinancialRecordExplorerSavedViewSnapshot>, IFinancialRecordExplorerSavedViewStore
{
    private const int SnapshotVersion = 1;

    private readonly ILogger<FileFinancialRecordExplorerSavedViewStore> _logger;

    public FileFinancialRecordExplorerSavedViewStore(
        string workstationDataRoot,
        ILogger<FileFinancialRecordExplorerSavedViewStore> logger)
        : base(
            ResolveSnapshotPath(workstationDataRoot),
            FinancialRecordExplorerSavedViewJsonContext.Default.FinancialRecordExplorerSavedViewSnapshot)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<IReadOnlyList<FinancialRecordExplorerSavedViewDto>> LoadAsync(
        string tenantId,
        string explorerId,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(explorerId);
        var normalizedTenantId = NormalizeTenantId(tenantId);

        return await ReadSnapshotAsync(
                snapshot => snapshot.Views
                    .Where(view =>
                        string.Equals(view.TenantId, normalizedTenantId, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(view.ExplorerId, explorerId, StringComparison.OrdinalIgnoreCase))
                    .Select(static view => view.SavedView)
                    .OrderBy(static view => view.Label, StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                ct)
            .ConfigureAwait(false);
    }

    public async Task<FinancialRecordExplorerSavedViewDto> SaveAsync(
        string tenantId,
        string explorerId,
        FinancialRecordExplorerSavedViewDto savedView,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(explorerId);
        ArgumentNullException.ThrowIfNull(savedView);
        var normalizedTenantId = NormalizeTenantId(tenantId);

        return await UpdateSnapshotAsync(
                snapshot =>
                {
                    var retained = snapshot.Views
                        .Where(view =>
                            !string.Equals(view.TenantId, normalizedTenantId, StringComparison.OrdinalIgnoreCase) ||
                            !string.Equals(view.ExplorerId, explorerId, StringComparison.OrdinalIgnoreCase) ||
                            !string.Equals(view.SavedView.ViewId, savedView.ViewId, StringComparison.OrdinalIgnoreCase))
                        .Append(new FinancialRecordExplorerSavedViewRecord(normalizedTenantId, explorerId, savedView with { IsSystem = false, IsActive = false }))
                        .OrderBy(static view => view.TenantId, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(static view => view.ExplorerId, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(static view => view.SavedView.Label, StringComparer.OrdinalIgnoreCase)
                        .ToArray();

                    return (
                        new FinancialRecordExplorerSavedViewSnapshot(SnapshotVersion, retained),
                        savedView with { IsSystem = false, IsActive = false });
                },
                ct)
            .ConfigureAwait(false);
    }

    protected override FinancialRecordExplorerSavedViewSnapshot CreateEmptySnapshot()
        => new(SnapshotVersion, []);

    protected override FinancialRecordExplorerSavedViewSnapshot HandleCorruptSnapshot(JsonException exception)
    {
        _logger.LogWarning(exception, "Financial record explorer saved-view snapshot is not valid JSON: {Path}", SnapshotPath);
        throw new InvalidOperationException($"Financial record explorer saved-view snapshot is invalid: {SnapshotPath}", exception);
    }

    protected override FinancialRecordExplorerSavedViewSnapshot OnSnapshotLoaded(
        FinancialRecordExplorerSavedViewSnapshot snapshot)
    {
        if (snapshot.Version != SnapshotVersion)
        {
            throw new InvalidOperationException(
                $"Financial record explorer saved-view snapshot version {snapshot.Version} is not supported. Expected {SnapshotVersion}: {SnapshotPath}");
        }

        return snapshot;
    }

    private static string ResolveSnapshotPath(string workstationDataRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workstationDataRoot);
        var explorerDirectory = Path.Combine(workstationDataRoot, "explorers");
        Directory.CreateDirectory(explorerDirectory);
        return Path.Combine(explorerDirectory, "financial-record-explorer-saved-views.json");
    }

    private static string NormalizeTenantId(string tenantId)
        => tenantId.Trim().ToLowerInvariant();
}

public sealed record FinancialRecordExplorerSavedViewRecord(
    string TenantId,
    string ExplorerId,
    FinancialRecordExplorerSavedViewDto SavedView);

public sealed record FinancialRecordExplorerSavedViewSnapshot(
    int Version,
    IReadOnlyList<FinancialRecordExplorerSavedViewRecord> Views);

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, WriteIndented = true)]
[JsonSerializable(typeof(FinancialRecordExplorerSavedViewSnapshot))]
internal sealed partial class FinancialRecordExplorerSavedViewJsonContext : JsonSerializerContext;

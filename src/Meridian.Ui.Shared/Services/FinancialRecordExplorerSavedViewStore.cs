using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Contracts.Workstation;
using Meridian.Storage.Store;
using Microsoft.Extensions.Logging;

namespace Meridian.Ui.Shared.Services;

/// <summary>
/// Loads and saves financial-record explorer views within a tenant and explorer scope.
/// </summary>
public interface IFinancialRecordExplorerSavedViewStore
{
    Task<IReadOnlyList<WorkstationTableViewDto>> LoadPersonalViewsAsync(
        string tenantId, string operatorId, string tableId, CancellationToken ct = default)
        => throw new NotSupportedException("Personal views are not supported by this store.");

    Task<WorkstationTableViewDto> SavePersonalViewAsync(
        string tenantId, string operatorId, string tableId, WorkstationTableViewSaveRequest request, CancellationToken ct = default)
        => throw new NotSupportedException("Personal views are not supported by this store.");

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

/// <summary>
/// Persists tenant-scoped financial-record explorer saved views to disk.
/// </summary>
public sealed class FileFinancialRecordExplorerSavedViewStore
    : JsonFileSnapshotStore<FinancialRecordExplorerSavedViewSnapshot>, IFinancialRecordExplorerSavedViewStore
{
    private const int SnapshotVersion = 2;

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

        await using var writerLease = await AcquireWriterLeaseAsync(ct).ConfigureAwait(false);
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
                        new FinancialRecordExplorerSavedViewSnapshot(SnapshotVersion, retained, snapshot.PersonalViews),
                        savedView with { IsSystem = false, IsActive = false });
                },
                ct)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<WorkstationTableViewDto>> LoadPersonalViewsAsync(
        string tenantId, string operatorId, string tableId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(operatorId);
        ArgumentException.ThrowIfNullOrWhiteSpace(tableId);
        return await ReadSnapshotAsync(snapshot =>
            snapshot.Views.Where(view => Same(view.TenantId, NormalizeTenantId(tenantId)) && Same(view.ExplorerId, tableId))
                .Select(view => new WorkstationTableViewDto(
                    view.SavedView.ViewId, view.SavedView.Label, view.SavedView.SearchText,
                    view.SavedView.ColumnIds ?? [], "compact", Filters: view.SavedView.Filters
                        .GroupBy(filter => filter.FilterId).ToDictionary(group => group.Key, group => group.Last().Value),
                    IsReadOnly: true, Origin: "Legacy tenant view — copy to my views"))
                .Concat((snapshot.PersonalViews ?? []).Where(view => Same(view.TenantId, NormalizeTenantId(tenantId)) &&
                    Same(view.OperatorId, operatorId) && Same(view.TableId, tableId)).Select(view => view.View))
                .OrderBy(view => view.Label, StringComparer.OrdinalIgnoreCase).ToArray(), ct).ConfigureAwait(false);
    }

    public async Task<WorkstationTableViewDto> SavePersonalViewAsync(
        string tenantId, string operatorId, string tableId, WorkstationTableViewSaveRequest request, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(operatorId);
        ArgumentException.ThrowIfNullOrWhiteSpace(tableId);
        ArgumentNullException.ThrowIfNull(request);
        var label = request.Label?.Trim() ?? string.Empty;
        if (label.Length is 0 or > 100 || request.SearchText?.Length > 1000 ||
            request.ColumnIds is null || request.ColumnIds.Count is 0 or > 100 ||
            request.ColumnIds.Any(column => string.IsNullOrWhiteSpace(column) || column.Length > 100) ||
            request.ColumnIds.Distinct(StringComparer.Ordinal).Count() != request.ColumnIds.Count ||
            request.Density is not ("compact" or "comfortable") ||
            request.SortDirection is not (null or "asc" or "desc") || request.SortColumnId?.Length > 100 ||
            request.Filters?.Count > 50 || request.Filters?.Any(filter => filter.Key.Length is 0 or > 100 || filter.Value is null || filter.Value.Length > 1000) == true ||
            request.Description?.Length > 2000 || request.ExplorerFilters?.Count > 50 ||
            request.ExplorerFilters?.Any(filter => filter is null || string.IsNullOrWhiteSpace(filter.FilterId) ||
                filter.FilterId.Length > 100 || filter.Label?.Length > 100 || filter.Value?.Length > 1000 || filter.Operator?.Length > 100) == true)
        {
            throw new ArgumentException("Saved table view contains invalid presentation settings.");
        }
        var view = new WorkstationTableViewDto(Guid.NewGuid().ToString("N"), label, request.SearchText ?? string.Empty,
            request.ColumnIds.ToArray(), request.Density, request.SortColumnId, request.SortDirection,
            request.Filters is null ? null : new Dictionary<string, string>(request.Filters), Origin: "My view")
        { Description = request.Description?.Trim(), ExplorerFilters = request.ExplorerFilters?.ToArray() };
        await using var writerLease = await AcquireWriterLeaseAsync(ct).ConfigureAwait(false);
        return await UpdateSnapshotAsync(snapshot =>
        {
            var retained = (snapshot.PersonalViews ?? []).Append(new WorkstationPersonalTableViewRecord(
                NormalizeTenantId(tenantId), operatorId.Trim(), tableId.Trim(), view)).ToArray();
            return (snapshot with { Version = SnapshotVersion, PersonalViews = retained }, view);
        }, ct).ConfigureAwait(false);
    }

    private static bool Same(string left, string right) => string.Equals(left.Trim(), right.Trim(), StringComparison.Ordinal);

    private async Task<FileStream> AcquireWriterLeaseAsync(CancellationToken ct)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            try
            { return new FileStream(SnapshotPath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) when (DateTimeOffset.UtcNow < deadline) { await Task.Delay(25, ct).ConfigureAwait(false); }
        }
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
        if (snapshot.Version is not (1 or SnapshotVersion))
        {
            throw new InvalidOperationException(
                $"Financial record explorer saved-view snapshot version {snapshot.Version} is not supported. Expected {SnapshotVersion}: {SnapshotPath}");
        }

        // Version 1 rows never acquire a guessed operator owner. They remain legacy tenant views.
        return snapshot with { Version = SnapshotVersion, PersonalViews = snapshot.PersonalViews ?? [] };
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
    IReadOnlyList<FinancialRecordExplorerSavedViewRecord> Views,
    IReadOnlyList<WorkstationPersonalTableViewRecord>? PersonalViews = null);

public sealed record WorkstationPersonalTableViewRecord(
    string TenantId, string OperatorId, string TableId, WorkstationTableViewDto View);

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, WriteIndented = true)]
[JsonSerializable(typeof(FinancialRecordExplorerSavedViewSnapshot))]
internal sealed partial class FinancialRecordExplorerSavedViewJsonContext : JsonSerializerContext;

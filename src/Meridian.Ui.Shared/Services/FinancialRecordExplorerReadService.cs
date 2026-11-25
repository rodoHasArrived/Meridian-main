using System.Globalization;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.Api;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.DirectLending;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Tenancy;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.Ledger;
using Meridian.Storage.Ledger;
using Meridian.Strategies.Services;

namespace Meridian.Ui.Shared.Services;

public sealed partial class FinancialRecordExplorerReadService
{
    public const string LedgerExplorerId = "ledger";
    public const string PortfolioExplorerId = "portfolio";
    public const string SecurityInstrumentExplorerId = "security-instrument";
    public const string ReportLineProvenanceExplorerId = "report-line-provenance";
    private const string LegacySavedViewTenantId = "legacy-global";

    private static readonly string[] KnownExplorerIds =
    [
        LedgerExplorerId,
        PortfolioExplorerId,
        SecurityInstrumentExplorerId,
        ReportLineProvenanceExplorerId
    ];

    private readonly IFinancialRecordExplorerSavedViewStore _savedViewStore;
    private readonly StrategyRunReadService? _runReadService;
    private readonly ReportPackWorkflowService? _reportPackWorkflowService;
    private readonly ReportPackDeliveryService? _reportPackDeliveryService;
    private readonly ISecurityMasterWorkbenchQueryService? _securityMasterWorkbenchQueryService;
    private readonly IAssetOperationsQueryService? _assetOperationsQueryService;
    private readonly DirectLendingOperationsReadService? _directLendingOperationsReadService;
    private readonly ILedgerJournalStore? _ledgerJournalStore;
    private readonly IAssetAccountingEventSpineService? _assetAccountingEventSpineService;
    private readonly IFundProfileTenancyRegistry? _tenancyRegistry;

    public FinancialRecordExplorerReadService(
        IFinancialRecordExplorerSavedViewStore savedViewStore,
        StrategyRunReadService? runReadService = null,
        ReportPackWorkflowService? reportPackWorkflowService = null,
        ReportPackDeliveryService? reportPackDeliveryService = null,
        ISecurityMasterWorkbenchQueryService? securityMasterWorkbenchQueryService = null,
        IAssetOperationsQueryService? assetOperationsQueryService = null,
        DirectLendingOperationsReadService? directLendingOperationsReadService = null,
        ILedgerJournalStore? ledgerJournalStore = null,
        IAssetAccountingEventSpineService? assetAccountingEventSpineService = null,
        IFundProfileTenancyRegistry? tenancyRegistry = null)
    {
        _savedViewStore = savedViewStore ?? throw new ArgumentNullException(nameof(savedViewStore));
        _runReadService = runReadService;
        _reportPackWorkflowService = reportPackWorkflowService;
        _reportPackDeliveryService = reportPackDeliveryService;
        _securityMasterWorkbenchQueryService = securityMasterWorkbenchQueryService;
        _assetOperationsQueryService = assetOperationsQueryService;
        _directLendingOperationsReadService = directLendingOperationsReadService;
        _ledgerJournalStore = ledgerJournalStore;
        _assetAccountingEventSpineService = assetAccountingEventSpineService;
        _tenancyRegistry = tenancyRegistry;
    }

    public static bool IsKnownExplorerId(string explorerId)
        => KnownExplorerIds.Contains(NormalizeExplorerId(explorerId), StringComparer.OrdinalIgnoreCase);

    public async Task<FinancialRecordExplorerDto?> GetExplorerAsync(
        string explorerId,
        CancellationToken ct = default)
        => await GetExplorerAsync(explorerId, LegacySavedViewTenantId, ct).ConfigureAwait(false);

    public async Task<FinancialRecordExplorerDto?> GetExplorerAsync(
        string explorerId,
        string tenantId,
        CancellationToken ct = default)
        => await GetExplorerAsync(explorerId, tenantId, query: null, ct).ConfigureAwait(false);

    public async Task<FinancialRecordExplorerDto?> GetExplorerAsync(
        string explorerId,
        string tenantId,
        FinancialRecordExplorerQueryDto? query,
        CancellationToken ct = default)
        => await GetExplorerAsync(explorerId, tenantId, query, FinancialRecordExplorerReadScope.All, ct).ConfigureAwait(false);

    public async Task<FinancialRecordExplorerDto?> GetExplorerAsync(
        string explorerId,
        string tenantId,
        FinancialRecordExplorerQueryDto? query,
        FinancialRecordExplorerReadScope scope,
        CancellationToken ct = default)
        => await GetExplorerAsync(explorerId, tenantId, companyId: null, query, scope, ct: ct).ConfigureAwait(false);

    /// <summary>
    /// The explorer for one tenant, with <paramref name="companyId"/> completing the fund-ownership
    /// scope used to choose which strategy run may be its source. Request-serving callers pass both;
    /// the in-process overloads above pass no company and fall back to tenant-only ownership.
    /// </summary>
    public async Task<FinancialRecordExplorerDto?> GetExplorerAsync(
        string explorerId,
        string tenantId,
        string? companyId,
        FinancialRecordExplorerQueryDto? query,
        FinancialRecordExplorerReadScope scope,
        ReportAccessQueryContext? reportAccess = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentNullException.ThrowIfNull(scope);
        var normalized = NormalizeExplorerId(explorerId);
        var fundScope = new FundOwnershipScope(tenantId, companyId);
        return normalized switch
        {
            LedgerExplorerId => await BuildLedgerExplorerAsync(tenantId, fundScope, query, ct).ConfigureAwait(false),
            PortfolioExplorerId => await BuildPortfolioExplorerAsync(tenantId, fundScope, query, ct).ConfigureAwait(false),
            SecurityInstrumentExplorerId => await BuildSecurityInstrumentExplorerAsync(tenantId, fundScope, query, scope, reportAccess, ct).ConfigureAwait(false),
            ReportLineProvenanceExplorerId => await BuildReportLineProvenanceExplorerAsync(tenantId, query, reportAccess, ct).ConfigureAwait(false),
            _ => null
        };
    }

    /// <summary>
    /// The tenant/company pair a strategy run's fund profile must be owned by for its detail to be
    /// used as an explorer source.
    /// </summary>
    private readonly record struct FundOwnershipScope(string TenantId, string? CompanyId);


    public async Task<FinancialRecordExplorerSelectedRecordDto?> GetRecordAsync(
        string explorerId,
        string recordId,
        CancellationToken ct = default)
        => await GetRecordAsync(explorerId, recordId, LegacySavedViewTenantId, ct).ConfigureAwait(false);

    public async Task<FinancialRecordExplorerSelectedRecordDto?> GetRecordAsync(
        string explorerId,
        string recordId,
        string tenantId,
        CancellationToken ct = default)
        => await GetRecordAsync(explorerId, recordId, tenantId, FinancialRecordExplorerReadScope.All, ct).ConfigureAwait(false);

    public async Task<FinancialRecordExplorerSelectedRecordDto?> GetRecordAsync(
        string explorerId,
        string recordId,
        string tenantId,
        FinancialRecordExplorerReadScope scope,
        CancellationToken ct = default)
        => await GetRecordAsync(explorerId, recordId, tenantId, companyId: null, scope, ct: ct).ConfigureAwait(false);

    public async Task<FinancialRecordExplorerSelectedRecordDto?> GetRecordAsync(
        string explorerId,
        string recordId,
        string tenantId,
        string? companyId,
        FinancialRecordExplorerReadScope scope,
        ReportAccessQueryContext? reportAccess = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recordId);

        // The record view is one row of the same explorer, so it carries the same enrichments and
        // must be projected the same way; routing it through the unscoped overload would withhold a
        // family from the list and hand it back on the drill-in -- or serve a foreign tenant's row
        // that the list itself refused.
        var explorer = await GetExplorerAsync(explorerId, tenantId, companyId, query: null, scope, reportAccess, ct).ConfigureAwait(false);
        return explorer?.Rows
            .FirstOrDefault(row => string.Equals(row.RecordId, recordId, StringComparison.OrdinalIgnoreCase))
            ?.Detail;
    }

    public async Task<FinancialRecordExplorerSavedViewDto?> SaveViewAsync(
        string explorerId,
        FinancialRecordExplorerSavedViewSaveRequestDto request,
        CancellationToken ct = default)
        => await SaveViewAsync(explorerId, LegacySavedViewTenantId, request, ct).ConfigureAwait(false);

    public async Task<FinancialRecordExplorerSavedViewDto?> SaveViewAsync(
        string explorerId,
        string tenantId,
        FinancialRecordExplorerSavedViewSaveRequestDto request,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentNullException.ThrowIfNull(request);
        var normalized = NormalizeExplorerId(explorerId);
        if (!IsKnownExplorerId(normalized))
        {
            return null;
        }

        var label = request.Label?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(label))
        {
            throw new InvalidOperationException("Saved view label is required.");
        }

        var filters = NormalizeFilters(request.Filters);
        var searchText = request.SearchText?.Trim() ?? string.Empty;
        if (filters.Count == 0 && string.IsNullOrWhiteSpace(searchText))
        {
            throw new InvalidOperationException("Saved view requires a search term or at least one filter.");
        }

        var view = new FinancialRecordExplorerSavedViewDto(
            ViewId: $"operator-{Slugify(label)}-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}",
            Label: label,
            Description: request.Description?.Trim() ?? string.Empty,
            IsSystem: false,
            IsActive: false,
            Filters: filters,
            SearchText: searchText,
            ColumnIds: NormalizeColumnIds(request.ColumnIds));

        return await _savedViewStore.SaveAsync(tenantId, normalized, view, ct).ConfigureAwait(false);
    }

}

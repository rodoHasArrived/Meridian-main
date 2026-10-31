using Meridian.Contracts.Api;
using Meridian.Contracts.FixedIncome;
using Meridian.Ui.Services;

namespace Meridian.Wpf.Services;

/// <summary>
/// Administrative bond calculation cutover over the shared reference-data routes. The server resolves the
/// reviewer from the authenticated actor and verifies inventory, block and receipt hashes.
/// </summary>
public interface IWorkstationBondCutoverApiClient
{
    Task<ApiResponse<BondMigrationInventoryDto>> GetReadinessAsync(CancellationToken ct = default);
    Task<ApiResponse<BondMigrationCutoverReceiptDto>> CutoverAsync(BondMigrationCutoverRequest request, CancellationToken ct = default);
    /// <summary>Read-only batch comparison of one candidate convention set across up to 100 bonds; staging stays per bond.</summary>
    Task<ApiResponse<BondMigrationPreviewDto[]>> PreviewMigrationBatchAsync(BondMigrationBatchPreviewRequest request, CancellationToken ct = default);
}

public sealed class WorkstationBondCutoverApiClient : IWorkstationBondCutoverApiClient
{
    private readonly ApiClientService _apiClient;

    public WorkstationBondCutoverApiClient(ApiClientService apiClient)
    {
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
    }

    public Task<ApiResponse<BondMigrationInventoryDto>> GetReadinessAsync(CancellationToken ct = default)
        => _apiClient.GetWithResponseAsync<BondMigrationInventoryDto>(UiApiRoutes.ReferenceDataBondCutoverReadiness, ct);

    public Task<ApiResponse<BondMigrationCutoverReceiptDto>> CutoverAsync(BondMigrationCutoverRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return _apiClient.PostWithResponseAsync<BondMigrationCutoverReceiptDto>(UiApiRoutes.ReferenceDataBondCutover, request, ct);
    }

    public Task<ApiResponse<BondMigrationPreviewDto[]>> PreviewMigrationBatchAsync(BondMigrationBatchPreviewRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return _apiClient.PostWithResponseAsync<BondMigrationPreviewDto[]>(UiApiRoutes.ReferenceDataBondMigrationBatchPreview, request, ct);
    }
}

using Meridian.Contracts.DirectLending;

namespace Meridian.Application.DirectLending;

public interface IDirectLendingServicerStatementService
{
    Task<ServicerStatementPreviewDto> PreviewAsync(ServicerStatementImportRequestDto request, CancellationToken ct = default);

    Task<ServicerStatementImportResultDto> ImportAsync(ServicerStatementImportRequestDto request, DirectLendingCommandMetadataDto? metadata = null, CancellationToken ct = default);

    Task<ServicerStatementPreviewDto?> GetBatchAsync(Guid batchId, CancellationToken ct = default);

    Task<IReadOnlyList<ServicerStatementRowDto>> GetRowsAsync(Guid batchId, CancellationToken ct = default);

    Task<IReadOnlyList<ServicerStatementPreviewDto>> ListBatchesAsync(CancellationToken ct = default);

    Task<ServicerStatementApplyResultDto> ApplyAsync(Guid batchId, ServicerStatementApplyRequestDto request, DirectLendingCommandMetadataDto? metadata = null, CancellationToken ct = default);
}

using Meridian.Contracts.SecurityMaster;

namespace Meridian.Application.SecurityMaster;

public interface IEdgarIngestOrchestrator
{
    Task<EdgarIngestResult> IngestAsync(EdgarIngestRequest request, CancellationToken ct = default);
}

using Meridian.Contracts.SecurityMaster;

namespace Meridian.Application.SecurityMaster;

/// <summary>
/// Defines ingestion of SEC EDGAR data into the Security Master workflow.
/// </summary>
public interface IEdgarIngestOrchestrator
{
    Task<EdgarIngestResult> IngestAsync(EdgarIngestRequest request, CancellationToken ct = default);
}

using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Meridian.Ui.Services.Services;
using Meridian.Contracts.Api.Quality;

namespace Meridian.Ui.Services.DataQuality;

/// <summary>
/// Quality-specific HTTP contract routed through the shared desktop API client infrastructure.
/// This replaces feature-local <see cref="HttpClient"/> usage as the long-term access pattern.
/// </summary>
public interface IDataQualityApiClient
{
    Task<QualityDashboardResponse?> GetDashboardAsync(CancellationToken ct = default);
    Task<IReadOnlyList<QualityGapResponse>> GetGapsAsync(int count, CancellationToken ct = default);
    Task<IReadOnlyList<QualityAnomalyResponse>> GetAnomaliesAsync(int count, CancellationToken ct = default);
    Task<QualityLatencyStatisticsResponse?> GetLatencyStatisticsAsync(CancellationToken ct = default);
    Task<QualityComparisonResponse?> GetProviderComparisonAsync(string symbol, CancellationToken ct = default);
    Task<bool> AcknowledgeAnomalyAsync(string anomalyId, CancellationToken ct = default);
    Task<QualityGapRemediationResponse?> RepairGapAsync(
        string symbol,
        QualityGapRemediationRequest request,
        CancellationToken ct = default);
    Task<QualityCheckResult?> RunQualityCheckAsync(string path, CancellationToken ct = default);
}

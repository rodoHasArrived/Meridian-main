using System.Threading;
using System.Threading.Tasks;

namespace Meridian.Ui.Services.DataQuality;

/// <summary>
/// Maps quality API responses into UI-ready presentation snapshots shared across desktop views.
/// </summary>
public interface IDataQualityPresentationService
{
    Task<DataQualityPresentationSnapshot> GetSnapshotAsync(string timeRange, CancellationToken ct = default);
    Task<DataQualityProviderComparisonPresentation> GetProviderComparisonAsync(string symbol, CancellationToken ct = default);
    DataQualitySymbolDrilldownPresentation BuildSymbolDrilldown(DataQualitySymbolPresentation symbol);
}

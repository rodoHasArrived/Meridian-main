using Meridian.Contracts.Api;
using Meridian.Contracts.Workstation;
using Meridian.Ui.Services;

namespace Meridian.Wpf.Features.Reporting;

/// <summary>Reads only the amount catalogue retained with an authorized governed reporting run.</summary>
public interface IReportRetainedAmountsApiClient
{
    Task<ApiResponse<ReportLedgerAmountBindingDto[]>> GetAmountsAsync(string runId, CancellationToken ct);
}

/// <summary>Uses the shared governed-report catalogue; no live ledger or legacy report fallback.</summary>
public sealed class ReportRetainedAmountsApiClient(ApiClientService client) : IReportRetainedAmountsApiClient
{
    public Task<ApiResponse<ReportLedgerAmountBindingDto[]>> GetAmountsAsync(string runId, CancellationToken ct)
        => client.GetWithResponseAsync<ReportLedgerAmountBindingDto[]>(
            UiApiRoutes.WithParam(UiApiRoutes.ReportingGovernedRunAmounts, "runId", runId), ct);
}

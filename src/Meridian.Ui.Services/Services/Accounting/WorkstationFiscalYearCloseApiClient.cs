using System.Globalization;
using Meridian.Contracts.Api;
using Meridian.Contracts.Ledger;

namespace Meridian.Ui.Services.Services.Accounting;

/// <summary>
/// Shared read of the governed fiscal-year close plan (W12-YEND-001). The server derives the plan
/// from retained close snapshots; workstations present it and never recompute balances.
/// </summary>
public interface IWorkstationFiscalYearCloseApiClient
{
    Task<FiscalYearClosePlanDto> GetClosePlanAsync(Guid ledgerBookId, int fiscalYear, CancellationToken ct = default);
}

public sealed class WorkstationFiscalYearCloseApiClient(ApiClientService api) : IWorkstationFiscalYearCloseApiClient
{
    public async Task<FiscalYearClosePlanDto> GetClosePlanAsync(Guid ledgerBookId, int fiscalYear, CancellationToken ct = default)
    {
        var route = UiApiRoutes.LedgerFiscalYearClosePlan
            .Replace("{ledgerBookId:guid}", ledgerBookId.ToString("D"), StringComparison.Ordinal)
            .Replace("{fiscalYear:int}", fiscalYear.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
        var response = await api.GetWithResponseAsync<FiscalYearClosePlanDto>(route, ct).ConfigureAwait(false);
        var plan = response.Success && response.Data is not null ? response.Data
            : throw new InvalidOperationException(response.StatusCode switch
            {
                403 => "The authenticated session cannot read this ledger book's fiscal-year close plan.",
                404 => "The ledger book was not found or is not accessible.",
                429 => "The service is busy. Retry this request shortly.",
                _ => response.ErrorMessage ?? "The governed fiscal-year close service is unavailable."
            });
        if (plan.LedgerBookId != ledgerBookId || plan.FiscalYear != fiscalYear)
            throw new InvalidOperationException("The fiscal-year close plan does not match the selected book and year.");
        // A ready plan must carry its hash and no blockers; anything else is shown as not ready.
        if (plan.IsReady && (plan.Blockers.Count > 0 || string.IsNullOrWhiteSpace(plan.PlanHashSha256)))
            throw new InvalidOperationException("The fiscal-year close plan is inconsistent: a ready plan must have a hash and no blockers.");
        return plan;
    }
}

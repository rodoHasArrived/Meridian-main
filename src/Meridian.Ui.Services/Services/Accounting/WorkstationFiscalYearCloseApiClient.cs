using System.Globalization;
using Meridian.Contracts.Api;
using Meridian.Contracts.Ledger;

namespace Meridian.Ui.Services.Services.Accounting;

/// <summary>
/// Shared fiscal-year close workflow. The server derives the plan from retained close snapshots
/// and commits a receipt for the exact plan the controller reviewed.
/// </summary>
public interface IWorkstationFiscalYearCloseApiClient
{
    Task<FiscalYearClosePlanDto> GetClosePlanAsync(Guid ledgerBookId, int fiscalYear, CancellationToken ct = default);
    Task<FiscalYearCloseReceiptDto?> GetCloseReceiptAsync(
        Guid ledgerBookId, int fiscalYear, Guid? receiptId = null, CancellationToken ct = default);
    Task<FiscalYearCloseReceiptDto> FinalizeCloseAsync(
        Guid ledgerBookId, int fiscalYear, FinalizeFiscalYearCloseRequestDto request, CancellationToken ct = default);
}

public sealed class WorkstationFiscalYearCloseApiClient(ApiClientService api) : IWorkstationFiscalYearCloseApiClient
{
    public async Task<FiscalYearClosePlanDto> GetClosePlanAsync(Guid ledgerBookId, int fiscalYear, CancellationToken ct = default)
    {
        var route = CloseRoute(UiApiRoutes.LedgerFiscalYearClosePlan, ledgerBookId, fiscalYear);
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

    public async Task<FiscalYearCloseReceiptDto?> GetCloseReceiptAsync(
        Guid ledgerBookId, int fiscalYear, Guid? receiptId = null, CancellationToken ct = default)
    {
        var route = CloseRoute(UiApiRoutes.LedgerFiscalYearCloseReceipts, ledgerBookId, fiscalYear);
        if (receiptId is { } id)
            route += $"?receiptId={id:D}";
        var response = await api.GetWithResponseAsync<FiscalYearCloseReceiptDto>(route, ct).ConfigureAwait(false);
        if (response.StatusCode == 404 && receiptId is null)
            return null;
        return ReadReceipt(response, ledgerBookId, fiscalYear, receiptId);
    }

    public async Task<FiscalYearCloseReceiptDto> FinalizeCloseAsync(
        Guid ledgerBookId, int fiscalYear, FinalizeFiscalYearCloseRequestDto request, CancellationToken ct = default)
    {
        var route = CloseRoute(UiApiRoutes.LedgerFiscalYearCloseReceipts, ledgerBookId, fiscalYear);
        var response = await api.PostWithResponseAsync<FiscalYearCloseReceiptDto>(route, request, ct).ConfigureAwait(false);
        var receipt = ReadReceipt(response, ledgerBookId, fiscalYear, request.CommandId);
        if (!string.Equals(receipt.Plan.PlanHashSha256, request.ExpectedPlanHashSha256, StringComparison.OrdinalIgnoreCase)
            || receipt.SupersedesReceiptId != request.SupersedesReceiptId)
            throw new InvalidOperationException("The fiscal-year close receipt does not match the reviewed command.");
        return receipt;
    }

    private static FiscalYearCloseReceiptDto ReadReceipt(
        ApiResponse<FiscalYearCloseReceiptDto> response, Guid bookId, int fiscalYear, Guid? expectedReceiptId)
    {
        var receipt = response.Success && response.Data is not null ? response.Data
            : throw new InvalidOperationException(response.StatusCode switch
            {
                403 => "The authenticated controller or book scope is unavailable for this fiscal-year close action.",
                404 => "The selected fiscal-year close receipt is unavailable.",
                409 => response.ErrorMessage ?? "The fiscal-year close plan or receipt changed. Reload and review it again.",
                429 => "The service is busy. Retry the same request shortly.",
                _ => response.ErrorMessage ?? "The governed fiscal-year close receipt service is unavailable."
            });
        if (receipt.ReceiptId == Guid.Empty || (expectedReceiptId.HasValue && receipt.ReceiptId != expectedReceiptId)
            || receipt.Plan.LedgerBookId != bookId || receipt.Plan.FiscalYear != fiscalYear
            || !receipt.Plan.IsReady || string.IsNullOrWhiteSpace(receipt.Plan.PlanHashSha256))
            throw new InvalidOperationException("The fiscal-year close receipt does not match the selected book and year.");
        return receipt;
    }

    private static string CloseRoute(string route, Guid ledgerBookId, int fiscalYear)
        => route
            .Replace("{ledgerBookId:guid}", ledgerBookId.ToString("D"), StringComparison.Ordinal)
            .Replace("{fiscalYear:int}", fiscalYear.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
}

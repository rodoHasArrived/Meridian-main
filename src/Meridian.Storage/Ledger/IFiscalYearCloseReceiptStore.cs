using Meridian.Contracts.Ledger;

namespace Meridian.Storage.Ledger;

/// <summary>Projects and retains the authorized fiscal close of one continuous ledger book.</summary>
public interface IFiscalYearCloseReceiptStore
{
    Task<FiscalYearClosePlanDto> GetFiscalYearClosePlanAsync(
        Guid ledgerBookId, int fiscalYear, CancellationToken ct = default);

    Task<FiscalYearCloseReceiptDto?> GetFiscalYearCloseReceiptAsync(
        Guid ledgerBookId, int fiscalYear, Guid? receiptId = null, CancellationToken ct = default);

    Task<FiscalYearCloseReceiptDto> FinalizeFiscalYearCloseAsync(
        Guid ledgerBookId, int fiscalYear, FinalizeFiscalYearCloseRequestDto request,
        string actor, string controllerRole, string tenantId, string companyId, CancellationToken ct = default);
}

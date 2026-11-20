using Meridian.Contracts.Ledger;

namespace Meridian.Storage.Ledger;

/// <summary>Retains a reviewed FX sale set and captures approved journal consequences without appending journals.</summary>
public interface ILedgerBookFxSettlementStore
{
    Task<BookFxSettlementSetDto?> GetBookFxSettlementSetAsync(Guid setId, string tenantId, string companyId,
        CancellationToken ct = default);

    Task<BookFxSettlementSetDto> RetainBookFxSettlementSetAsync(RetainBookFxSettlementSetRequestDto request,
        string actor, string controllerRole, string tenantId, string companyId, CancellationToken ct = default);

    Task<BookFxSettlementLegReceiptDto> CaptureBookFxSettlementLegAsync(Guid setId,
        CaptureBookFxSettlementLegRequestDto request, string actor, string controllerRole, string tenantId,
        string companyId, CancellationToken ct = default);
}

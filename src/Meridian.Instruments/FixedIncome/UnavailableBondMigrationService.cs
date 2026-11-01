using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.Workstation;

namespace Meridian.Instruments.FixedIncome;

/// <summary>Missing reference storage cannot certify an empty inventory or a successful cutover.</summary>
public sealed class UnavailableBondMigrationService : IBondMigrationService
{
    private static InvalidOperationException Unavailable()
        => new("Bond migration requires configured authoritative Security Master storage.");

    public Task<BondMigrationPreviewDto> PreviewAsync(BondMigrationPreviewRequest request, CancellationToken ct = default)
        => Task.FromException<BondMigrationPreviewDto>(Unavailable());

    public Task<UpdateSecurityFieldRequest> PrepareEditAsync(BondMigrationPreviewDto preview, string actor,
        string justification, CancellationToken ct = default)
        => Task.FromException<UpdateSecurityFieldRequest>(Unavailable());

    public Task<BondMigrationInventoryDto> GetInventoryAsync(CancellationToken ct = default)
        => Task.FromException<BondMigrationInventoryDto>(Unavailable());

    public Task<BondMigrationCutoverReceiptDto> CutoverAsync(BondMigrationCutoverRequest request, CancellationToken ct = default)
        => Task.FromException<BondMigrationCutoverReceiptDto>(Unavailable());
}

using Meridian.Contracts.AssetOperations;
using Meridian.Storage.SecurityMaster;

namespace Meridian.Instruments.AssetOperations;

public sealed class DirectLoanProjectionService
    : InstrumentProjectionServiceBase<DirectLoanProjectionRow, DirectLoanReferenceDto>, IDirectLoanReferenceService
{
    private readonly IDirectLoanReferenceProjectionStore _projectionStore;

    public DirectLoanProjectionService(
        ISecurityMasterStore securityMasterStore,
        IDirectLoanReferenceProjectionStore projectionStore)
        : base(securityMasterStore)
    {
        _projectionStore = projectionStore;
    }

    protected override string AssetClass => "DirectLoan";

    protected override Task<DirectLoanProjectionRow?> FetchRowAsync(Guid securityId, CancellationToken ct)
        => _projectionStore.GetDirectLoanAsync(securityId, ct);

    public Task<IReadOnlyList<DirectLoanReferenceDto>> GetByBorrowerAsync(string borrower, CancellationToken ct = default)
        => QueryByTermAsync(borrower, _projectionStore.GetByBorrowerAsync, ct);

    public Task<IReadOnlyList<DirectLoanReferenceDto>> GetByReferenceIndexAsync(string referenceIndex, CancellationToken ct = default)
        => QueryByTermAsync(referenceIndex, _projectionStore.GetByReferenceIndexAsync, ct);

    public async Task<IReadOnlyList<DirectLoanReferenceDto>> GetMaturityLadderAsync(DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        if (to < from)
        {
            return Array.Empty<DirectLoanReferenceDto>();
        }

        var rows = await _projectionStore.GetMaturityLadderAsync(from, to, ct).ConfigureAwait(false);
        return MapRows(rows);
    }

    public async Task<IReadOnlyList<DirectLoanCovenantDto>> GetCovenantsAsync(Guid securityId, CancellationToken ct = default)
    {
        var rows = await _projectionStore.GetCovenantsAsync(securityId, ct).ConfigureAwait(false);
        return rows
            .Select(static row => new DirectLoanCovenantDto(row.SecurityId, row.Ordinal, row.CovenantType, row.Threshold, row.Notes))
            .ToArray();
    }

    public async Task<IReadOnlyList<DirectLoanPrincipalPaymentDto>> GetPrincipalScheduleAsync(Guid securityId, CancellationToken ct = default)
    {
        var rows = await _projectionStore.GetPrincipalScheduleAsync(securityId, ct).ConfigureAwait(false);
        return MapPrincipalPayments(rows);
    }

    public async Task<IReadOnlyList<DirectLoanPrincipalPaymentDto>> GetPrincipalPaymentsDueAsync(DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        if (to < from)
        {
            return Array.Empty<DirectLoanPrincipalPaymentDto>();
        }

        var rows = await _projectionStore.GetPrincipalPaymentsDueAsync(from, to, ct).ConfigureAwait(false);
        return MapPrincipalPayments(rows);
    }

    protected override DirectLoanReferenceDto MapRow(DirectLoanProjectionRow row)
        => new(
            row.SecurityId,
            row.DisplayName,
            row.Currency,
            row.Borrower,
            row.MaturityDate,
            row.ReferenceIndex,
            row.SpreadBps,
            row.CurrentCouponRate,
            row.ResetFrequency,
            row.PricingSource,
            row.PrimaryIdentifierValue,
            row.Version);

    private static IReadOnlyList<DirectLoanPrincipalPaymentDto> MapPrincipalPayments(IReadOnlyList<DirectLoanPrincipalPaymentRow> rows)
        => rows
            .Select(static row => new DirectLoanPrincipalPaymentDto(row.SecurityId, row.Ordinal, row.PaymentDate, row.Amount))
            .ToArray();
}

public sealed class NullDirectLoanReferenceService : IDirectLoanReferenceService
{
    public Task<DirectLoanReferenceDto?> GetReferenceAsync(Guid securityId, CancellationToken ct = default)
        => Task.FromResult<DirectLoanReferenceDto?>(null);

    public Task<IReadOnlyList<DirectLoanReferenceDto>> GetByBorrowerAsync(string borrower, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<DirectLoanReferenceDto>>(Array.Empty<DirectLoanReferenceDto>());

    public Task<IReadOnlyList<DirectLoanReferenceDto>> GetByReferenceIndexAsync(string referenceIndex, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<DirectLoanReferenceDto>>(Array.Empty<DirectLoanReferenceDto>());

    public Task<IReadOnlyList<DirectLoanReferenceDto>> GetMaturityLadderAsync(DateOnly from, DateOnly to, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<DirectLoanReferenceDto>>(Array.Empty<DirectLoanReferenceDto>());

    public Task<IReadOnlyList<DirectLoanCovenantDto>> GetCovenantsAsync(Guid securityId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<DirectLoanCovenantDto>>(Array.Empty<DirectLoanCovenantDto>());

    public Task<IReadOnlyList<DirectLoanPrincipalPaymentDto>> GetPrincipalScheduleAsync(Guid securityId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<DirectLoanPrincipalPaymentDto>>(Array.Empty<DirectLoanPrincipalPaymentDto>());

    public Task<IReadOnlyList<DirectLoanPrincipalPaymentDto>> GetPrincipalPaymentsDueAsync(DateOnly from, DateOnly to, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<DirectLoanPrincipalPaymentDto>>(Array.Empty<DirectLoanPrincipalPaymentDto>());
}

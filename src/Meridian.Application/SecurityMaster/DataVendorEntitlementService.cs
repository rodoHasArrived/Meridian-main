using System.Collections.Generic;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;

namespace Meridian.Application.SecurityMaster;

public sealed class DataVendorEntitlementService : IDataVendorEntitlementService
{
    private readonly IDataVendorEntitlementStore _store;

    public DataVendorEntitlementService(IDataVendorEntitlementStore store)
    {
        _store = store;
    }

    public Task<IReadOnlyList<DataVendorEntitlementDto>> GetAllAsync(CancellationToken ct = default)
        => _store.GetAllAsync(ct);

    public Task<IReadOnlyList<DataVendorEntitlementDto>> GetExpiringAsync(
        int withinDays, CancellationToken ct = default)
        => _store.GetExpiringAsync(DateTimeOffset.UtcNow.AddDays(withinDays), ct);

    public async Task<DataVendorEntitlementDto> UpsertAsync(
        UpsertDataVendorEntitlementRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.VendorName))
            throw new ArgumentException("VendorName is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.Actor))
            throw new ArgumentException("Actor is required.", nameof(request));

        var effectiveTo = request.EffectiveTo;
        var status = DeriveStatus(effectiveTo, request.RenewalReminderDays);

        var dto = new DataVendorEntitlementDto(
            request.EntitlementId ?? Guid.NewGuid(),
            request.VendorName.Trim(),
            request.DataType,
            request.ContractReference?.Trim(),
            request.EffectiveFrom,
            effectiveTo,
            request.AumThresholdUsd,
            request.RequiresDirectClientContract,
            request.ContactEmail?.Trim(),
            request.RenewalReminderDays,
            status,
            request.Actor,
            DateTimeOffset.UtcNow)
        {
            ClientId = NormalizeOptional(request.ClientId),
            AccountId = NormalizeOptional(request.AccountId),
            FundProfileId = NormalizeOptional(request.FundProfileId),
            SecurityId = request.SecurityId,
            SourceCategory = NormalizeOptional(request.SourceCategory),
            ExpectedRefreshCadence = NormalizeOptional(request.ExpectedRefreshCadence),
            DefaultMaxDaysStale = request.DefaultMaxDaysStale,
            OperatorMetadata = NormalizeOptional(request.OperatorMetadata)
        };

        return await _store.UpsertAsync(dto, ct).ConfigureAwait(false);
    }

    public async Task DeactivateAsync(Guid entitlementId, string actor, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(actor))
            throw new ArgumentException("Actor is required.", nameof(actor));

        var existing = await _store.GetByIdAsync(entitlementId, ct).ConfigureAwait(false);
        if (existing is null)
            throw new InvalidOperationException($"Entitlement {entitlementId} not found.");

        await _store.DeactivateAsync(entitlementId, ct).ConfigureAwait(false);
    }

    private static DataVendorEntitlementStatus DeriveStatus(DateTimeOffset? effectiveTo, int renewalReminderDays)
    {
        if (effectiveTo.HasValue && effectiveTo.Value < DateTimeOffset.UtcNow)
            return DataVendorEntitlementStatus.Expired;

        if (effectiveTo.HasValue && effectiveTo.Value <= DateTimeOffset.UtcNow.AddDays(renewalReminderDays))
            return DataVendorEntitlementStatus.ExpiringSoon;

        return DataVendorEntitlementStatus.Active;
    }

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

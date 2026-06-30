using System.Text.Json;
using Meridian.Contracts.Tenancy;
using Meridian.Storage.Archival;

namespace Meridian.Ui.Shared.Services;

/// <summary>
/// File-backed <see cref="IFundProfileTenancyRegistry"/> for the single-process workstation host. Fund
/// profiles are keyed case-insensitively (normalized to a trimmed, lower-invariant id, matching how the
/// rest of the system compares FundProfileId) and the first tenant to bind a fund wins; a later bind by a
/// different tenant is a no-op that returns the original owner. Writes go through
/// <see cref="AtomicFileWriter"/> so a crash mid-write cannot corrupt the registry.
/// </summary>
public sealed class FileFundProfileTenancyRegistry : IFundProfileTenancyRegistry
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _snapshotPath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public FileFundProfileTenancyRegistry(string snapshotPath)
    {
        _snapshotPath = string.IsNullOrWhiteSpace(snapshotPath)
            ? throw new ArgumentException("Fund-profile tenancy snapshot path is required.", nameof(snapshotPath))
            : snapshotPath;
    }

    public async Task<FundProfileOwnership> BindAsync(
        string fundProfileId, string tenantId, string? companyId = null, CancellationToken ct = default)
    {
        var fund = NormalizeFund(fundProfileId);
        if (string.IsNullOrEmpty(fund))
        {
            throw new ArgumentException("fundProfileId is required.", nameof(fundProfileId));
        }

        var tenant = NormalizeRequired(tenantId, nameof(tenantId));
        var company = NormalizeOptional(companyId);

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var snapshot = await ReadWithoutLockAsync(ct).ConfigureAwait(false);
            var existing = snapshot.Bindings.FirstOrDefault(b =>
                string.Equals(b.FundProfileId, fund, StringComparison.Ordinal));
            if (existing is not null)
            {
                return new FundProfileOwnership(existing.FundProfileId, existing.TenantId, existing.CompanyId);
            }

            var bound = new FundProfileBinding(fund, tenant, company);
            var bindings = snapshot.Bindings
                .Append(bound)
                .OrderBy(b => b.FundProfileId, StringComparer.Ordinal)
                .ToArray();
            await WriteWithoutLockAsync(snapshot with { Bindings = bindings }, ct).ConfigureAwait(false);
            return new FundProfileOwnership(bound.FundProfileId, bound.TenantId, bound.CompanyId);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<FundProfileOwnership?> ResolveAsync(string fundProfileId, CancellationToken ct = default)
    {
        var fund = NormalizeFund(fundProfileId);
        if (string.IsNullOrEmpty(fund))
        {
            return null;
        }

        var snapshot = await ReadAsync(ct).ConfigureAwait(false);
        var binding = snapshot.Bindings.FirstOrDefault(b =>
            string.Equals(b.FundProfileId, fund, StringComparison.Ordinal));
        return binding is null
            ? null
            : new FundProfileOwnership(binding.FundProfileId, binding.TenantId, binding.CompanyId);
    }

    public async Task<bool> IsAccessibleAsync(
        string fundProfileId, string tenantId, string? companyId = null, CancellationToken ct = default)
    {
        var owner = await ResolveAsync(fundProfileId, ct).ConfigureAwait(false);
        return owner is null || owner.IsHeldBy(tenantId);
    }

    private async Task<TenancySnapshot> ReadAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await ReadWithoutLockAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<TenancySnapshot> ReadWithoutLockAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!File.Exists(_snapshotPath))
        {
            return TenancySnapshot.Empty;
        }

        await using var stream = File.OpenRead(_snapshotPath);
        return await JsonSerializer.DeserializeAsync<TenancySnapshot>(stream, JsonOptions, ct).ConfigureAwait(false)
            ?? TenancySnapshot.Empty;
    }

    private Task WriteWithoutLockAsync(TenancySnapshot snapshot, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(snapshot, JsonOptions);
        return AtomicFileWriter.WriteAsync(_snapshotPath, json, ct);
    }

    private static string NormalizeFund(string? value)
        => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToLowerInvariant();

    private static string NormalizeRequired(string? value, string parameterName)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new ArgumentException($"{parameterName} is required.", parameterName);
        }

        return normalized;
    }

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed record FundProfileBinding(string FundProfileId, string TenantId, string? CompanyId);

    private sealed record TenancySnapshot(IReadOnlyList<FundProfileBinding> Bindings)
    {
        public static TenancySnapshot Empty { get; } = new([]);
    }
}

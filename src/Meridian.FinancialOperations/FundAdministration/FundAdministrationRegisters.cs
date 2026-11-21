using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Core.IO;
using Meridian.Ledger;
using Meridian.Storage.Store;

namespace Meridian.FinancialOperations.FundAdministration;

/// <summary>
/// One capitalized asset a fund book depreciates. The register is the source the
/// fixed-asset-depreciation posting calculation reads; an asset is depreciated from its in-service
/// date until it is fully depreciated or retired.
/// </summary>
public sealed record FixedAssetRegisterEntry(
    FixedAssetRecord Asset,
    string TenantId,
    string CompanyId,
    string FundProfileId,
    Guid LedgerBookId,
    string RegisteredBy,
    DateTimeOffset RegisteredAtUtc,
    string EvidenceUri,
    DateOnly? RetiredOn = null);

/// <summary>Durable register of the fixed assets each fund book depreciates.</summary>
public interface IFixedAssetRegister
{
    Task<IReadOnlyList<FixedAssetRegisterEntry>> ListAsync(string tenantId, string companyId, string fundProfileId,
        Guid ledgerBookId, CancellationToken ct = default);

    /// <summary>Adds the asset, or replaces the entry with the same asset id in the same book.</summary>
    Task<FixedAssetRegisterEntry> UpsertAsync(FixedAssetRegisterEntry entry, CancellationToken ct = default);
}

/// <summary>
/// One declared distribution to one investor and the waterfall tiers it paid. Later distributions
/// to the same investor read the earlier tiers from here, so return of capital, preferred return
/// and GP catch-up are each paid once across the fund's life.
/// </summary>
public sealed record DistributionDeclarationRecord(
    string TenantId,
    string CompanyId,
    string FundProfileId,
    Guid LedgerBookId,
    string InvestorId,
    string DistributionId,
    DateOnly EffectiveDate,
    Guid JournalEntryId,
    decimal ContributedCapital,
    decimal PreferredReturnAccrued,
    decimal AmountToDistribute,
    decimal CarryRate,
    decimal CatchUpRate,
    decimal ReturnOfCapital,
    decimal PreferredReturn,
    decimal GpCatchUp,
    decimal LpCatchUp,
    decimal LpCarry,
    decimal GpCarry,
    string DeclaredBy,
    DateTimeOffset DeclaredAtUtc);

/// <summary>Durable register of declared distributions and their waterfall tiers.</summary>
public interface IDistributionRegister
{
    /// <summary>Serializes the queue read, waterfall calculation, draft admission and register writes.</summary>
    Task<IAsyncDisposable> AcquireDeclarationLeaseAsync(CancellationToken ct = default);

    Task<IReadOnlyList<DistributionDeclarationRecord>> ListAsync(string tenantId, string companyId, string fundProfileId,
        Guid ledgerBookId, string investorId, CancellationToken ct = default);

    /// <summary>
    /// Retains the declaration. Recording the same distribution again with the same terms returns
    /// the retained record; different terms under the same identity are refused.
    /// </summary>
    Task<DistributionDeclarationRecord> RecordAsync(DistributionDeclarationRecord record, CancellationToken ct = default);
}

internal static class FundAdministrationRegisterJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    public static bool Same(string? left, string? right) => string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);

    public static string Require(string? value, string name)
        => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException($"{name} is required.", name) : value.Trim();
}

/// <summary>
/// File-backed <see cref="IFixedAssetRegister"/>. A corrupt snapshot is an error, never an empty
/// register: losing the register must block depreciation, not silently stop it.
/// </summary>
public sealed class FileFixedAssetRegister(string directory)
    : JsonFileSnapshotStore<FileFixedAssetRegister.Snapshot>(Path.Combine(directory, "fixed-asset-register.json"), FundAdministrationRegisterJson.Options),
      IFixedAssetRegister
{
    public Task<IReadOnlyList<FixedAssetRegisterEntry>> ListAsync(string tenantId, string companyId, string fundProfileId,
        Guid ledgerBookId, CancellationToken ct = default)
        => ReadSnapshotAsync<IReadOnlyList<FixedAssetRegisterEntry>>(snapshot => snapshot.Assets
            .Where(entry => InScope(entry, tenantId, companyId, fundProfileId, ledgerBookId))
            .OrderBy(entry => entry.Asset.InServiceDate).ThenBy(entry => entry.Asset.AssetId)
            .ToArray(), ct);

    public Task<FixedAssetRegisterEntry> UpsertAsync(FixedAssetRegisterEntry entry, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(entry.Asset);
        if (entry.Asset.AssetId == Guid.Empty || entry.LedgerBookId == Guid.Empty)
            throw new ArgumentException("A registered fixed asset needs an asset id and a ledger book.", nameof(entry));
        if (entry.RetiredOn is { } retired && retired < entry.Asset.InServiceDate)
            throw new ArgumentException("A fixed asset cannot be retired before it enters service.", nameof(entry));
        // Building the schedule applies the asset's own validation (life, cost, salvage).
        if (entry.Asset.Method != DepreciationMethod.UnitsOfProduction)
            _ = new DepreciationScheduleCalculator().BuildSchedule(entry.Asset);
        var normalized = entry with
        {
            TenantId = FundAdministrationRegisterJson.Require(entry.TenantId, nameof(entry.TenantId)),
            CompanyId = FundAdministrationRegisterJson.Require(entry.CompanyId, nameof(entry.CompanyId)),
            FundProfileId = FundAdministrationRegisterJson.Require(entry.FundProfileId, nameof(entry.FundProfileId)),
            RegisteredBy = FundAdministrationRegisterJson.Require(entry.RegisteredBy, nameof(entry.RegisteredBy)),
            EvidenceUri = FundAdministrationRegisterJson.Require(entry.EvidenceUri, nameof(entry.EvidenceUri)),
            RegisteredAtUtc = entry.RegisteredAtUtc.ToUniversalTime()
        };
        return UpdateSnapshotAsync(snapshot =>
        {
            var owner = snapshot.Assets.FirstOrDefault(existing => existing.Asset.AssetId == normalized.Asset.AssetId);
            if (owner is not null && !InScope(owner, normalized.TenantId, normalized.CompanyId, normalized.FundProfileId, normalized.LedgerBookId))
                throw new InvalidOperationException("This fixed asset is registered to a different fund book.");
            // Earlier periods were depreciated on the registered terms and are not recomputed, so
            // new terms would charge more or less than the asset's depreciable cost.
            if (owner is not null && owner.Asset with { DisplayName = normalized.Asset.DisplayName, FinancialAccountId = normalized.Asset.FinancialAccountId } != normalized.Asset)
                throw new InvalidOperationException(
                    "A registered fixed asset's cost, salvage value, life, method, currency and in-service date cannot be changed. Retire it and register the revised asset under a new id.");
            var assets = snapshot.Assets.Where(existing => existing.Asset.AssetId != normalized.Asset.AssetId).Append(normalized).ToArray();
            return (new Snapshot(assets), normalized);
        }, ct);
    }

    protected override Snapshot CreateEmptySnapshot() => new([]);

    private static bool InScope(FixedAssetRegisterEntry entry, string tenantId, string companyId, string fundProfileId, Guid ledgerBookId)
        => entry.LedgerBookId == ledgerBookId && FundAdministrationRegisterJson.Same(entry.TenantId, tenantId) &&
           FundAdministrationRegisterJson.Same(entry.CompanyId, companyId) && FundAdministrationRegisterJson.Same(entry.FundProfileId, fundProfileId);

    public sealed record Snapshot(IReadOnlyList<FixedAssetRegisterEntry> Assets);
}

/// <summary>File-backed <see cref="IDistributionRegister"/>; a corrupt snapshot is an error.</summary>
public sealed class FileDistributionRegister(string directory)
    : JsonFileSnapshotStore<FileDistributionRegister.Snapshot>(Path.Combine(directory, "distribution-register.json"), FundAdministrationRegisterJson.Options),
      IDistributionRegister
{
    public async Task<IAsyncDisposable> AcquireDeclarationLeaseAsync(CancellationToken ct = default)
        => await ExclusiveFileLock.AcquireAsync(SnapshotPath + ".declaration.lock", TimeSpan.FromSeconds(30), ct)
            .ConfigureAwait(false);

    public Task<IReadOnlyList<DistributionDeclarationRecord>> ListAsync(string tenantId, string companyId, string fundProfileId,
        Guid ledgerBookId, string investorId, CancellationToken ct = default)
        => ReadSnapshotAsync<IReadOnlyList<DistributionDeclarationRecord>>(snapshot => snapshot.Distributions
            .Where(record => InScope(record, tenantId, companyId, fundProfileId, ledgerBookId, investorId))
            .OrderBy(record => record.EffectiveDate).ThenBy(record => record.DeclaredAtUtc)
            .ToArray(), ct);

    public Task<DistributionDeclarationRecord> RecordAsync(DistributionDeclarationRecord record, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        return UpdateSnapshotAsync(snapshot =>
        {
            var existing = snapshot.Distributions.FirstOrDefault(item =>
                InScope(item, record.TenantId, record.CompanyId, record.FundProfileId, record.LedgerBookId, record.InvestorId) &&
                FundAdministrationRegisterJson.Same(item.DistributionId, record.DistributionId));
            if (existing is null)
                return (new Snapshot([.. snapshot.Distributions, record]), record);
            // Who recorded it and when may differ on a retry; the economics may not.
            if (existing with { DeclaredBy = record.DeclaredBy, DeclaredAtUtc = record.DeclaredAtUtc } != record)
                throw new InvalidOperationException(
                    $"Distribution '{record.DistributionId}' is already declared for this investor with different terms.");
            return (snapshot, existing);
        }, ct);
    }

    protected override Snapshot CreateEmptySnapshot() => new([]);

    private static bool InScope(DistributionDeclarationRecord record, string tenantId, string companyId, string fundProfileId,
        Guid ledgerBookId, string investorId)
        => record.LedgerBookId == ledgerBookId && FundAdministrationRegisterJson.Same(record.TenantId, tenantId) &&
           FundAdministrationRegisterJson.Same(record.CompanyId, companyId) && FundAdministrationRegisterJson.Same(record.FundProfileId, fundProfileId) &&
           FundAdministrationRegisterJson.Same(record.InvestorId, investorId);

    public sealed record Snapshot(IReadOnlyList<DistributionDeclarationRecord> Distributions);
}

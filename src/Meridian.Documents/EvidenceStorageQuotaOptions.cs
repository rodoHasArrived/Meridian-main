using Meridian.Core.Exceptions;

namespace Meridian.Documents;

/// <summary>Resource limits for retained Evidence Vault packages.</summary>
public sealed class EvidenceStorageQuotaOptions
{
    /// <summary>Maximum bytes in a single retained artifact. The existing 100 MiB ceiling remains authoritative.</summary>
    public long MaxArtifactBytes { get; set; } = 100L * 1024 * 1024;

    /// <summary>Maximum artifact, manifest, and index bytes in one package.</summary>
    public long MaxPackageBytes { get; set; } = 1024L * 1024 * 1024;

    /// <summary>Maximum number of distinct retained artifacts in one package.</summary>
    public int MaxArtifactsPerPackage { get; set; } = 256;

    /// <summary>Default tenant budget, including published packages and active reservations.</summary>
    public long DefaultTenantBudgetBytes { get; set; } = 10L * 1024 * 1024 * 1024;

    /// <summary>Optional case-insensitive budget overrides keyed by tenant identifier.</summary>
    public Dictionary<string, long> TenantBudgetBytes { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Free disk space that must remain after outstanding reserved writes.</summary>
    public long MinimumDiskHeadroomBytes { get; set; } = 256L * 1024 * 1024;

    /// <summary>Validates limits and returns a copy isolated from subsequent configuration mutation.</summary>
    public EvidenceStorageQuotaOptions ValidateAndSnapshot()
    {
        if (MaxArtifactBytes is <= 0 or > 100L * 1024 * 1024)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxArtifactBytes));
        }
        if (MaxPackageBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxPackageBytes));
        }
        if (MaxArtifactsPerPackage <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxArtifactsPerPackage));
        }
        if (DefaultTenantBudgetBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(DefaultTenantBudgetBytes));
        }
        if (MinimumDiskHeadroomBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MinimumDiskHeadroomBytes));
        }
        ArgumentNullException.ThrowIfNull(TenantBudgetBytes);
        var tenants = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var (tenant, budget) in TenantBudgetBytes)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(tenant);
            if (budget < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(TenantBudgetBytes));
            }
            tenants.Add(tenant.Trim(), budget);
        }
        return new EvidenceStorageQuotaOptions
        {
            MaxArtifactBytes = MaxArtifactBytes,
            MaxPackageBytes = MaxPackageBytes,
            MaxArtifactsPerPackage = MaxArtifactsPerPackage,
            DefaultTenantBudgetBytes = DefaultTenantBudgetBytes,
            TenantBudgetBytes = tenants,
            MinimumDiskHeadroomBytes = MinimumDiskHeadroomBytes
        };
    }
}

/// <summary>A retained package cannot fit within a configured storage limit.</summary>
public sealed class EvidenceStorageQuotaExceededException(string reason, string message)
    : MeridianException(message)
{
    /// <summary>The violated limit: artifact-bytes, package-bytes, package-count, tenant-bytes, or disk-headroom.</summary>
    public string Reason { get; } = reason;
}

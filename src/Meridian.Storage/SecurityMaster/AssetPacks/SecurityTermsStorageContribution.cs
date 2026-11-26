using Meridian.Contracts.SecurityMaster;
using Npgsql;

namespace Meridian.Storage.SecurityMaster.AssetPacks;

/// <summary>
/// One class's storage disposition. Specialized writers remain explicit rather than masquerading
/// as a schema-driven projection, and a missing descriptor cannot silently omit a class.
/// </summary>
internal sealed record SecurityTermsStorageContribution(
    string AssetClass,
    SecurityTermsProjectionKind ProjectionKind,
    SecurityTermsProjectionDescriptor? Descriptor = null,
    string? DispositionReason = null,
    Func<PostgresSecurityMasterStore, NpgsqlConnection, NpgsqlTransaction, SecurityProjectionRecord, CancellationToken, Task>? SpecializedWriter = null)
{
    internal static SecurityTermsStorageContribution Declarative(SecurityTermsProjectionDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        return new(descriptor.AssetClass, SecurityTermsProjectionKind.Declarative, descriptor);
    }

    internal static SecurityTermsStorageContribution Specialized(
        string assetClass,
        string reason,
        Func<PostgresSecurityMasterStore, NpgsqlConnection, NpgsqlTransaction, SecurityProjectionRecord, CancellationToken, Task> writer)
        => new(assetClass, SecurityTermsProjectionKind.Specialized, DispositionReason: reason, SpecializedWriter: writer);
}

namespace Meridian.Storage.SecurityMaster;

/// <summary>The minimum declared-schedule capabilities activated for one Security Master database.</summary>
public sealed record SecurityMasterTermsCapabilityFloor(
    string MinimumDeclaredMappingVersion,
    string MinimumKernelVersion,
    string ChangeRecord,
    string UpdatedBy,
    DateTimeOffset UpdatedAt);

/// <summary>Distinguishes a pre-migration database from an installed but not yet activated floor.</summary>
public sealed record SecurityMasterTermsCapabilityState(
    bool TableExists,
    SecurityMasterTermsCapabilityFloor? Floor);

/// <summary>
/// Reads the deployment floor. Applications never activate or lower it; activation belongs to the
/// deployment change record after the all-node inventory and data gates have been reviewed.
/// </summary>
public interface ISecurityMasterTermsCapabilityStore
{
    /// <summary>Reads the singleton floor, or its explicit pre-migration/unset state.</summary>
    Task<SecurityMasterTermsCapabilityState> ReadAsync(CancellationToken ct = default);
}

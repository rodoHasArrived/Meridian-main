using Meridian.Contracts.SecurityMaster;

namespace Meridian.Storage.SecurityMaster;

/// <summary>
/// Storage abstraction for operator-supplied per-security override values. Implementations
/// must be safe to call concurrently; the canonical implementation is backed by Postgres
/// and uses upsert semantics keyed by security id.
/// </summary>
public interface IOperatorOverridesStore
{
    Task<OperatorOverridesDto?> GetAsync(Guid securityId, CancellationToken ct = default);

    Task<OperatorOverridesDto> PatchAsync(
        Guid securityId,
        OperatorOverridesPatchRequest request,
        string updatedBy,
        CancellationToken ct = default);
}

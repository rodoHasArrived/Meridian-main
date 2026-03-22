namespace Meridian.Application.Coordination;

/// <summary>
/// Durable lease record used for shared-storage resource ownership.
/// </summary>
public sealed record LeaseRecord(
    string ResourceId,
    string InstanceId,
    long LeaseVersion,
    DateTimeOffset AcquiredAtUtc,
    DateTimeOffset ExpiresAtUtc,
    DateTimeOffset LastRenewedAtUtc
);

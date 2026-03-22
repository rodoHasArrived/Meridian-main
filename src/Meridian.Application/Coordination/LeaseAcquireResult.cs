namespace Meridian.Application.Coordination;

public sealed record LeaseAcquireResult(
    bool Acquired,
    bool TakenOver,
    LeaseRecord? Lease,
    string? CurrentOwner,
    DateTimeOffset? CurrentExpiryUtc,
    string? Reason
);

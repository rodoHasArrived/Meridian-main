namespace Meridian.Contracts.Configuration;

/// <summary>Non-secret projection of a retained, connection-owned verification audit event.</summary>
public sealed record ProviderVerificationObservationDto(DateTimeOffset RecordedAtUtc, bool Succeeded);

public sealed record ProviderVerificationHistoryDto(
    string ConnectionId,
    string ProviderId,
    DateTimeOffset AsOfUtc,
    IReadOnlyList<ProviderVerificationObservationDto> Observations,
    bool IsTruncated);

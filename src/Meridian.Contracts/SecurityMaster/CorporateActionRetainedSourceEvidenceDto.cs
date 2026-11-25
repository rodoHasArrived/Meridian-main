namespace Meridian.Contracts.SecurityMaster;

/// <summary>
/// Original UTF-8 JSON record retained with a source proposal. The raw digest authenticates the
/// exact retained text, while the provider event version may use a canonical content digest.
/// This companion is provenance, not economic terms or permission to accept or post an event.
/// </summary>
public sealed record CorporateActionRetainedSourceEvidenceDto(
    string ProviderEventType,
    string Schema,
    string RawPayloadJson,
    string RawPayloadSha256);

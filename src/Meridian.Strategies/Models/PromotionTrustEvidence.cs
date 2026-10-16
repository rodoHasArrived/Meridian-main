namespace Meridian.Strategies.Models;

/// <summary>
/// Provider trust evidence bound to a run's immutable inputs. Completeness and freshness
/// are independent gates; an execution-controls snapshot supplies neither.
/// </summary>
public sealed record PromotionTrustEvidence(
    string RunId,
    string InputHashSha256,
    string SourceReference,
    DateTimeOffset ObservedAt,
    DateTimeOffset ValidUntil,
    bool IsComplete);

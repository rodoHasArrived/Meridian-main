namespace Meridian.Contracts.Options;

/// <summary>Neutral single-contract criteria. Passing these criteria is not trading permission.</summary>
public sealed record OptionScreenRequest(
    string UnderlyingSymbol,
    int MinDte = 0,
    int MaxDte = 365,
    decimal MinDelta = -1m,
    decimal MaxDelta = 1m,
    string Right = "Both",
    long MinOpenInterest = 0,
    long MinVolume = 0,
    decimal? MaxSpreadFraction = null,
    decimal? MinPremium = null,
    string PremiumBasis = "Bid",
    int StrikeRange = 20);

public sealed record OptionScreenCandidate(
    string Id, string Symbol, string UnderlyingSymbol, string Right, string Expiration,
    int DaysToExpiration, string Strike, string? Delta, string Bid, string Ask,
    string? Mid, string? SpreadFraction, long? OpenInterest, long? Volume,
    string Multiplier, string Currency, string Exchange, string Style,
    string Source, DateTimeOffset ObservedAt, bool PassesScreen,
    IReadOnlyList<string> RejectionReasons);

public sealed record OptionScreenResult(
    OptionScreenRequest Criteria, DateTimeOffset ScreenedAt,
    IReadOnlyList<OptionScreenCandidate> Candidates,
    IReadOnlyList<string> LoadedExpirations, IReadOnlyList<string> FailedExpirations,
    IReadOnlyList<string> OmittedExpirations, int StrikeRange,
    string CoverageNote, string AuthorityNote);

/// <summary>Exact proposed basket terms; unrestricted strategy names do not confer support.</summary>
public sealed record OptionAssessmentLeg(
    string LegId, string InstrumentType, string Symbol, string Side, string PositionIntent,
    string Ratio, string Currency, string? UnderlyingSymbol = null, string? Right = null,
    string? Expiration = null, string? Strike = null, string? Multiplier = null,
    string? Exchange = null, string? Deliverable = null);

public sealed record OptionAssessmentBasket(
    string Quantity, string OrderType, string PriceEffect, string? LimitPrice,
    string Currency, IReadOnlyList<OptionAssessmentLeg> Legs);

/// <summary>Broker-reported observations, retained as dated evidence rather than a current acceptance.</summary>
public sealed record BrokerOptionAssessment(
    string SchemaVersion, Guid FundAccountId, string ProviderId, string ExternalAccountId,
    DateTimeOffset AssessedAt, string BrokerReference, string Outcome, string RawBrokerStatus,
    OptionAssessmentBasket Basket,
    string? InitialMarginBefore = null, string? InitialMarginChange = null, string? InitialMarginAfter = null,
    string? MaintenanceMarginBefore = null, string? MaintenanceMarginChange = null, string? MaintenanceMarginAfter = null,
    string? ExcessLiquidityAfter = null, string? CashImpact = null,
    string? PermissionEvidence = null, string? RestrictionEvidence = null,
    IReadOnlyList<string>? Warnings = null,
    string? MarginCurrency = null, string? CashImpactCurrency = null);

/// <summary>The CSV alternative replaces Basket.Legs and uses the same validated header.</summary>
public sealed record OptionAssessmentImportRequest(
    BrokerOptionAssessment Assessment, string SourceFileName, string SourceContentBase64,
    string SourceContentType = "application/octet-stream", string? LegsCsv = null,
    string? CorrectsAssessmentId = null);

public sealed record OptionAssessmentImportPreview(
    BrokerOptionAssessment Assessment, string BasketFingerprint, string PreviewFingerprint,
    string SourceSha256, long SourceBytes, string? CorrectsAssessmentId,
    IReadOnlyList<string> Warnings);

public sealed record AcceptOptionAssessmentImportRequest(
    Guid IdempotencyKey, string PreviewFingerprint, OptionAssessmentImportRequest Import);

public sealed record RetainedOptionAssessment(
    string AssessmentId, BrokerOptionAssessment Assessment, string BasketFingerprint,
    string ContentSha256, DateTimeOffset RecordedAt, string RecordedBy,
    string SourceVaultId, string SourceDocumentId, string SourceSha256,
    string AssessmentVaultId, string AssessmentDocumentId,
    string? CorrectsAssessmentId, bool IsSuperseded = false);

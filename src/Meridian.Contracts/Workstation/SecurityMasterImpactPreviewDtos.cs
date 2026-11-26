using Meridian.Contracts.SecurityMaster;
using System.Text.Json.Serialization;

namespace Meridian.Contracts.Workstation;

[JsonConverter(typeof(JsonStringEnumConverter<SecurityImpactCompleteness>))]
public enum SecurityImpactCompleteness { Complete, Partial, Unavailable }
public sealed record SecurityImpactIssue(string Code, string Reason);
public sealed record SecurityAffectedLedgerBooksResult(SecurityImpactCompleteness Completeness,
    IReadOnlyList<Guid> BookIds, string Basis, IReadOnlyList<SecurityImpactIssue> Issues);
public sealed record SecurityImpactPeriod(Guid BookId, Guid PeriodId, long Version,
    DateOnly StartDate, DateOnly EndDate, string Status);
public sealed record SecurityMasterImpactScope(string TenantId, string? CompanyId = null);
public sealed record SecurityReleasedReportImpact(SecurityImpactCompleteness Completeness,
    IReadOnlyList<string> RunIds, string DependencyFingerprint, IReadOnlyList<SecurityImpactIssue> Issues);
public sealed record CreateSecurityMasterImpactPreviewRequest(DateTimeOffset AsOf);

/// <summary>Immutable review evidence. Partial exposure is explicit; no inferred lot or report counts.</summary>
public sealed record SecurityMasterImpactPreviewReceipt(
    string SchemaVersion,
    SecurityMasterImpactScope Scope,
    Guid SecurityId,
    Guid RevisionId,
    long CanonicalVersion,
    string RevisionFingerprint,
    string CanonicalTermsHash,
    SecurityDetailDto PreviousTerms,
    SecurityDetailDto ProposedTerms,
    DateTimeOffset AsOf,
    DateTimeOffset CapturedAt,
    SecurityAffectedLedgerBooksResult Books,
    IReadOnlyList<SecurityImpactPeriod> Periods,
    SecurityReleasedReportImpact Reports,
    SecurityCashFlowCalculationReceipt? Before,
    SecurityCashFlowCalculationReceipt? After,
    IReadOnlyList<SecurityImpactIssue> Issues,
    string DependencyFingerprint,
    string ContentSha256)
{
    /// <summary>The immutable revision's fund scope, retained for operator inspection.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? FundProfileId { get; init; }
}

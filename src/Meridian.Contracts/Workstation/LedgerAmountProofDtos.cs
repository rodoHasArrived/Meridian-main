namespace Meridian.Contracts.Workstation;

/// <summary>The complete retained accounting boundary for an amount proof.</summary>
public sealed record LedgerAmountScopeDto(
    string TenantId,
    string CompanyId,
    string FundProfileId,
    Guid LedgerBookId,
    Guid PeriodId);

public sealed record LedgerAmountProofEvidenceDto(
    string EvidenceId,
    string Kind,
    string Label,
    string? Route,
    string SourceSystem,
    DateTimeOffset? RetainedAt,
    EvidenceStatusDto Status,
    string? ContentHash = null,
    string? Reason = null)
{
    /// <summary>The exact posted source boundary when this source supports a generated report amount.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public LedgerAmountScopeDto? SourceScope { get; init; }

    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? SourceSubjectId { get; init; }
}

/// <summary>One server-evaluated amount proof shared by the browser and desktop.</summary>
public sealed record LedgerAmountProofDto(
    string SubjectId,
    LedgerAmountScopeDto Scope,
    decimal Amount,
    string Currency,
    EvidenceStatusDto Status,
    IReadOnlyList<LedgerAmountProofEvidenceDto> Evidence,
    IReadOnlyList<string> Warnings);

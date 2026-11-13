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
    IReadOnlyList<string> Warnings)
{
    /// <summary>
    /// Accounting decisions retained with contributing journals, independent of source readiness.
    /// An absent legacy value establishes no approval or coverage classification.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<AccountingPreparerAttestationDisclosureDto>? AccountingPreparerAttestations { get; init; }

    /// <summary>False for any preparer-attested or unclassified upstream dependency; null makes no claim.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public bool? OrdinaryRecordedApprovalSatisfied { get; init; }
}

/// <summary>
/// A classification of an immutable native accounting decision, not independent approval or source proof.
/// Complete entitlement operands remain in the separately authorized native receipt.
/// </summary>
public sealed record AccountingPreparerAttestationDisclosureDto(
    int SchemaVersion,
    string Classification,
    string Disclosure,
    LedgerAmountScopeDto SourceScope,
    IReadOnlyList<Guid> JournalEntryIds,
    IReadOnlyList<string> ValidationIssues)
{
    public Guid? AttestationId { get; init; }
    public Guid? CaseId { get; init; }
    public Guid? ProjectionId { get; init; }
    public Guid? EventId { get; init; }
    public long? EventVersion { get; init; }
    public string? Actor { get; init; }
    public DateTimeOffset? RecordedAt { get; init; }
    public string? PolicyVersion { get; init; }
    public string? PolicyHash { get; init; }
    public Guid? DeclarationId { get; init; }
    public long? DeclarationRevision { get; init; }
    public string? DraftedCandidateFingerprint { get; init; }
    public string? PostingIntentHash { get; init; }
}

/// <summary>Conservative approval classification shared by amount consumers; it defines no coverage denominator.</summary>
public static class LedgerAmountApprovalClassification
{
    public const string PreparerAttestation = "PreparerAttestation";
    public const string Unknown = "Unknown";

    public static bool MayCountAsOrdinaryApproval(LedgerAmountProofDto amount, bool ordinaryApprovalRecorded)
        => ordinaryApprovalRecorded && amount.OrdinaryRecordedApprovalSatisfied is not false
            && amount.AccountingPreparerAttestations is not { Count: > 0 };
}

using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Meridian.Contracts.SecurityMaster;

[JsonConverter(typeof(JsonStringEnumConverter<DataQualityRuleCategory>))]
public enum DataQualityRuleCategory
{
    FileMappingSufficiency,
    DataTypeRestriction,
    MinimumAttribute,
    TaxonomyCheck,
    ValueConsistency,
    RefreshControl,
    StalenessMonitor,
    ExternalComparison
}

[JsonConverter(typeof(JsonStringEnumConverter<DataQualityRuleSeverity>))]
public enum DataQualityRuleSeverity
{
    Warning,
    Error,
    HardBlock
}

public sealed record DataQualityRuleViolationDto(
    string RuleId,
    string RuleName,
    DataQualityRuleCategory Category,
    Guid SecurityId,
    string? FieldPath,
    string Message,
    DataQualityRuleSeverity Severity,
    DateTimeOffset DetectedAt);

public sealed record SecurityMasterQualityReportDto(
    DateTimeOffset RunAt,
    int SecuritiesScanned,
    int ViolationCount,
    IReadOnlyList<DataQualityRuleViolationDto> Violations)
{
    /// <summary>
    /// How many stored securities rows — active and inactive — carry asset-specific terms with no
    /// declared <c>termsFormat</c> tag: the ADR-024 decision-5 retirement gate
    /// (<c>select count(*) from securities where asset_specific_terms-&gt;&gt;'termsFormat' is null</c>),
    /// measured per deployment so the shape rule's remaining backlog is evidence instead of a
    /// guess. The tag drains organically: every write stamps it, so the count only falls. Null on
    /// reports persisted before the gauge existed. (One deliberate strictness over the SQL: a
    /// non-string tag also counts as untagged here; validation separately rejects it.)
    /// </summary>
    public int? UntaggedTermsPayloadCount { get; init; }
}

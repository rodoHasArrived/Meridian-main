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
    IReadOnlyList<DataQualityRuleViolationDto> Violations);

namespace Meridian.Contracts.Workstation;

/// <summary>A typed refusal or incomplete prerequisite, with a browser remediation path.</summary>
public sealed record InstrumentCapabilityReasonDto(string Code, string Reason, string RemediationPath);

/// <summary>
/// A passive assessment of a named operation on one selected Security Master version. Ready does
/// not grant approval or posting authority. Applicability and evaluation completeness are separate.
/// </summary>
public sealed record InstrumentCapabilityAssessmentDto(
    string Capability,
    string Label,
    string Operation,
    string Applicability,
    string Status,
    string EvaluationState,
    long? SecurityVersion,
    DateTimeOffset AsOfUtc,
    string Context,
    IReadOnlyList<InstrumentCapabilityReasonDto> Reasons);

/// <summary>One selected security's passive operation assessments; missing identities remain explicit.</summary>
public sealed record InstrumentCapabilitiesDto(
    Guid SecurityId,
    long? SecurityVersion,
    DateTimeOffset AsOfUtc,
    IReadOnlyList<InstrumentCapabilityAssessmentDto> Capabilities);

/// <summary>Bounded batch response. It is security eligibility evidence, not a holdings inventory.</summary>
public sealed record InstrumentCapabilityBatchDto(
    DateTimeOffset AsOfUtc,
    IReadOnlyList<InstrumentCapabilitiesDto> Securities);

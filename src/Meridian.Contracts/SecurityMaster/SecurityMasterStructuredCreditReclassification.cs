using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Meridian.Contracts.SecurityMaster;

/// <summary>
/// Whether a legacy securitised row can be re-modelled as <c>StructuredCredit</c> from the terms it
/// already carries (ADR-022). <see cref="Blocked"/> rows list what the operator still has to supply.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<StructuredCreditReclassificationDisposition>))]
public enum StructuredCreditReclassificationDisposition
{
    Ready,
    Blocked
}

/// <summary>One reason a reclassification proposal needs operator input before it can be applied.</summary>
public sealed record StructuredCreditReclassificationBlockerDto(
    string Code,
    string Message);

/// <summary>
/// One legacy securitised row the report proposes to re-model as <c>StructuredCredit</c>.
/// <paramref name="DerivedAssetSpecificTerms"/> holds the complete stamped terms document when the
/// disposition is <see cref="StructuredCreditReclassificationDisposition.Ready"/>, and only the
/// terms carried over from the source record when it is
/// <see cref="StructuredCreditReclassificationDisposition.Blocked"/>.
/// </summary>
public sealed record StructuredCreditReclassificationCandidateDto(
    Guid SecurityId,
    string DisplayName,
    string PrimaryIdentifierKind,
    string PrimaryIdentifierValue,
    string SourceAssetClass,
    string SourceLabel,
    StructuredCreditReclassificationDisposition Disposition,
    string? ProposedKind,
    JsonElement DerivedAssetSpecificTerms,
    IReadOnlyList<StructuredCreditReclassificationBlockerDto> Blockers);

/// <summary>
/// Report-first inventory of the active securitised rows still homed on <c>Bond</c> or
/// <c>CustomAsset</c> (ADR-022). Building the report changes nothing; applying a proposal is a
/// separate governed amendment.
/// </summary>
public sealed record StructuredCreditReclassificationReportDto(
    DateTimeOffset GeneratedAt,
    int SecuritiesScanned,
    int UnreadableCount,
    int CandidateCount,
    int ReadyCount,
    int BlockedCount,
    IReadOnlyList<StructuredCreditReclassificationCandidateDto> Candidates);

public interface ISecurityMasterStructuredCreditReclassificationService
{
    /// <summary>Scans the active Security Master population and builds the reclassification report.</summary>
    Task<StructuredCreditReclassificationReportDto> BuildReportAsync(CancellationToken ct = default);
}

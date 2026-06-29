using Meridian.Application.SecurityMaster;
using Meridian.Contracts.Workstation;
using Microsoft.Extensions.Logging;

namespace Meridian.Ui.Shared.Services;

/// <summary>
/// Report-pack-backed <see cref="IRestatementCandidateResolver"/> for the Security Master Passport
/// Workbench's closed-period restatement path (Phase 3). When a governed reference-data edit publishes
/// into a locked accounting period, this resolver locates the published report packs that consumed the
/// edited security and surfaces them as restatement <i>candidates</i>. The operator approves each via
/// <see cref="ReportPackWorkflowService.Restate"/>; the workbench only proposes — it never mutates a
/// published number.
///
/// <para>This replaces <see cref="NullRestatementCandidateResolver"/> as the registered default, so a
/// hard-closed book with report exposure no longer always degrades to a manual "locate affected packs"
/// task.</para>
///
/// <para><b>Matching.</b> A pack is a candidate when it is in a live published state
/// (<see cref="ReportPackWorkflowStateDto.Published"/> or <see cref="ReportPackWorkflowStateDto.Restated"/>),
/// is scoped to the impacted fund profile, and carries a retained report-line provenance entry tying a
/// line to this security (by <c>SecurityMasterId</c>/<c>SecurityDefinitionId</c>, or a security-kind
/// source whose id is the security). Matching is precise on purpose: a published pack that cannot be
/// tied to the security is left to the caller's default-deny safety net (a hard-closed book still
/// reports <see cref="SecurityMasterRestatementDecision.RestatementRequired"/> with a manual locate
/// task), so a genuine miss is never silently completed.</para>
///
/// <para><b>Deferred</b> to later slices: narrowing candidates to the specific reporting period
/// covering the edit's effective date (the pack <c>Period</c> is a free-form label, so period-precise
/// filtering is not yet safe and over-inclusion stays operator-reviewed); soft-closed governed-adjustment
/// posting; and a durable security→report-line index that would replace this per-fund scan.</para>
/// </summary>
public sealed class ReportPackRestatementCandidateResolver : IRestatementCandidateResolver
{
    private readonly ReportPackWorkflowService _reportPackWorkflow;
    private readonly ILogger<ReportPackRestatementCandidateResolver> _logger;

    public ReportPackRestatementCandidateResolver(
        ReportPackWorkflowService reportPackWorkflow,
        ILogger<ReportPackRestatementCandidateResolver> logger)
    {
        _reportPackWorkflow = reportPackWorkflow ?? throw new ArgumentNullException(nameof(reportPackWorkflow));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<IReadOnlyList<RestatementCandidateDto>> ResolveAsync(
        Guid securityId,
        Guid ledgerBookId,
        DateOnly effectiveDate,
        IReadOnlyList<string> changedFields,
        string? fundProfileId,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(fundProfileId))
        {
            // Without a fund scope the published packs cannot be narrowed. The caller's default-deny
            // path still flags a hard-closed book for manual locate, so this is not a silent miss.
            _logger.LogWarning(
                "Restatement candidates for security {SecurityId} could not be located: the published revision has no fund profile scope.",
                securityId);
            return Task.FromResult<IReadOnlyList<RestatementCandidateDto>>([]);
        }

        var changedFieldSummary = changedFields.Count == 0
            ? "reference data"
            : string.Join(", ", changedFields);

        var candidates = new List<RestatementCandidateDto>();
        foreach (var record in _reportPackWorkflow.ListRecordsForFundProfile(fundProfileId))
        {
            ct.ThrowIfCancellationRequested();

            // Only a live published output can require a restatement; drafts, in-review, approved, and
            // archived/rejected packs are not published numbers consumers rely on.
            if (record.State is not (ReportPackWorkflowStateDto.Published or ReportPackWorkflowStateDto.Restated))
            {
                continue;
            }

            var matchedLines = MatchSecurityLines(record, securityId);
            if (matchedLines.Count == 0)
            {
                continue;
            }

            candidates.Add(new RestatementCandidateDto(
                ReportId: record.ReportId,
                // A restatement publishes the next version of the same report, so the currently published
                // record is the prior version the operator restates from.
                PriorVersionReportId: record.ReportId,
                PeriodLabel: record.Period,
                Summary:
                    $"Published report pack {record.ReportId:D} for period {record.Period} has " +
                    $"{matchedLines.Count} line(s) sourced from security {securityId:D}; the governed edit to " +
                    $"{changedFieldSummary} (effective {effectiveDate:yyyy-MM-dd}) may require a restatement.",
                ChangedLines: matchedLines));
        }

        if (candidates.Count > 0)
        {
            _logger.LogInformation(
                "Located {CandidateCount} report-pack restatement candidate(s) for security {SecurityId} in fund profile {FundProfileId}.",
                candidates.Count, securityId, fundProfileId);
        }

        return Task.FromResult<IReadOnlyList<RestatementCandidateDto>>(candidates);
    }

    /// <summary>
    /// The retained report-line provenance entries on <paramref name="record"/> that tie a line to the
    /// edited security, projected into proposed changed lines. The proposal carries the line's retained
    /// report value (as both previous and current, since the restated value is the operator's to enter)
    /// and forwards the provenance evidence id so the operator's later <see cref="ReportPackWorkflowService.Restate"/>
    /// call can satisfy its evidence requirement.
    /// </summary>
    private static IReadOnlyList<ReportPackChangedLineDto> MatchSecurityLines(
        ReportPackWorkflowRecordDto record, Guid securityId)
    {
        if (record.LineProvenance is not { Count: > 0 } provenance)
        {
            return [];
        }

        return provenance
            .Where(line => LineReferencesSecurity(line, securityId))
            .Select(line => new ReportPackChangedLineDto(
                LineKey: line.LineKey,
                PreviousValue: line.ReportValue ?? string.Empty,
                CurrentValue: line.ReportValue ?? string.Empty,
                EvidenceLinks: string.IsNullOrWhiteSpace(line.EvidenceId)
                    ? null
                    : [new ReportPackEvidenceLinkDto(
                        EvidenceId: line.EvidenceId,
                        Label: $"Report-line provenance for {line.LineKey}",
                        Route: line.FinancialRecordHref,
                        Source: string.IsNullOrWhiteSpace(line.SourceKind) ? "report-line-provenance" : line.SourceKind)]))
            .ToArray();
    }

    private static bool LineReferencesSecurity(ReportPackLineProvenanceDto line, Guid securityId)
        => MatchesSecurity(line.SecurityMasterId, securityId)
           || MatchesSecurity(line.SecurityDefinitionId, securityId)
           || (ContainsToken(line.SourceKind, "security") && MatchesSecurity(line.SourceId, securityId));

    private static bool MatchesSecurity(string? value, Guid securityId)
        => !string.IsNullOrWhiteSpace(value)
           && Guid.TryParse(value, out var parsed)
           && parsed == securityId;

    private static bool ContainsToken(string? value, string token)
        => !string.IsNullOrWhiteSpace(value)
           && value.Contains(token, StringComparison.OrdinalIgnoreCase);
}

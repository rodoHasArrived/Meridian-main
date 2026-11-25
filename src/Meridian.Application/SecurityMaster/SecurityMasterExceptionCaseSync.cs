using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;

namespace Meridian.Application.SecurityMaster;

/// <summary>SLA days per Security Master exception (review D2), matching the existing quality SLA settings.</summary>
public sealed record SecurityMasterExceptionSla(
    int IdentifierConflictDays = 5,
    int FieldConflictDays = 3,
    int QualityHardBlockDays = 1,
    int QualityErrorDays = 3,
    int QualityWarningDays = 5);

/// <summary>
/// Review D2 (owner decision 2026-11-25: dedicated global queue). Projects open conflicts and the latest
/// data-quality violations into deployment-global exception cases with SLA due dates. One case per rule
/// and subject; re-running is idempotent, and a resolved conflict or cleared violation closes its case.
/// </summary>
public sealed class SecurityMasterExceptionCaseSync
{
    public const string ConflictCategory = "Conflict";
    public const string IdentifierAmbiguityCategory = "IdentifierAmbiguity";
    public const string DataQualityCategory = "DataQuality";

    private readonly ISecurityMasterExceptionCaseStore _cases;
    private readonly ISecurityMasterConflictService _conflicts;
    private readonly ISecurityMasterDataQualityService? _quality;
    private readonly SecurityMasterExceptionSla _sla;

    public SecurityMasterExceptionCaseSync(
        ISecurityMasterExceptionCaseStore cases,
        ISecurityMasterConflictService conflicts,
        ISecurityMasterDataQualityService? quality = null,
        SecurityMasterExceptionSla? sla = null)
    {
        _cases = cases ?? throw new ArgumentNullException(nameof(cases));
        _conflicts = conflicts ?? throw new ArgumentNullException(nameof(conflicts));
        _quality = quality;
        _sla = sla ?? new SecurityMasterExceptionSla();
    }

    public async Task<SecurityMasterExceptionSyncResult> SyncAsync(CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var observations = new List<SecurityMasterExceptionObservation>();
        var categories = new List<string> { ConflictCategory, IdentifierAmbiguityCategory };

        foreach (var conflict in await _conflicts.GetOpenConflictsAsync(ct).ConfigureAwait(false))
        {
            var isIdentifier = conflict.ConflictKind.Contains("Identifier", StringComparison.OrdinalIgnoreCase);
            var category = isIdentifier ? IdentifierAmbiguityCategory : ConflictCategory;
            var days = isIdentifier ? _sla.IdentifierConflictDays : _sla.FieldConflictDays;
            observations.Add(new SecurityMasterExceptionObservation(
                CaseKey: $"conflict:{conflict.ConflictId:D}",
                Category: category,
                RuleCode: conflict.ConflictKind,
                SecurityId: conflict.SecurityId,
                Severity: "Error",
                Title: $"{conflict.ConflictKind} on {conflict.FieldPath}",
                Detail: $"{conflict.ProviderA} says '{conflict.ValueA}'; {conflict.ProviderB} says '{conflict.ValueB}'.",
                SlaDueAt: conflict.DetectedAt.AddDays(days)));
        }

        if (_quality is not null)
        {
            var report = await _quality.GetLatestReportAsync(ct).ConfigureAwait(false);
            if (report is not null)
            {
                // Only a report that exists governs data-quality cases; without one they are left as-is.
                categories.Add(DataQualityCategory);
                foreach (var violation in report.Violations)
                {
                    var days = violation.Severity switch
                    {
                        DataQualityRuleSeverity.HardBlock => _sla.QualityHardBlockDays,
                        DataQualityRuleSeverity.Error => _sla.QualityErrorDays,
                        _ => _sla.QualityWarningDays
                    };
                    observations.Add(new SecurityMasterExceptionObservation(
                        CaseKey: $"quality:{violation.RuleId}:{violation.SecurityId:D}:{violation.FieldPath}",
                        Category: DataQualityCategory,
                        RuleCode: violation.RuleId,
                        SecurityId: violation.SecurityId,
                        Severity: violation.Severity.ToString(),
                        Title: violation.RuleName,
                        Detail: violation.Message,
                        SlaDueAt: violation.DetectedAt.AddDays(days)));
                }
            }
        }

        return await _cases.SyncAsync(categories, observations, now, ct).ConfigureAwait(false);
    }
}

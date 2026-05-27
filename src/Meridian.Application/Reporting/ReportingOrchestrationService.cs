using System.Collections.Frozen;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;

namespace Meridian.Application.Reporting;

public interface IReportingOrchestrationService
{
    Task<ReportingOutputManifest> ExecuteAsync(ReportingJobContract contract, CancellationToken cancellationToken);
    IReadOnlyList<ReportingRunAuditEntry> GetAudit(string runId);
    Task<bool> TransitionApprovalAsync(string runId, ReportingRunStatus target, string actor, string role, string notes, CancellationToken cancellationToken);
}

public interface IReportingTemplateCatalog
{
    ReportingTemplateMetadata Get(string templateId);
}

public sealed class ReportingOrchestrationService : IReportingOrchestrationService
{
    private static readonly FrozenDictionary<ReportingRunStatus, string[]> AllowedRoles = new Dictionary<ReportingRunStatus, string[]>
    {
        [ReportingRunStatus.InReview] = ["Reviewer", "OperationsLead"],
        [ReportingRunStatus.Approved] = ["OperationsLead", "ComplianceOfficer"],
        [ReportingRunStatus.Released] = ["OperationsLead"]
    }.ToFrozenDictionary();

    private readonly IReportingTemplateCatalog catalog;
    private readonly ConcurrentDictionary<string, ReportingOutputManifest> manifests = new();
    private readonly ConcurrentDictionary<string, List<ReportingRunAuditEntry>> audits = new();

    public ReportingOrchestrationService(IReportingTemplateCatalog catalog)
    {
        this.catalog = catalog;
    }

    public Task<ReportingOutputManifest> ExecuteAsync(ReportingJobContract contract, CancellationToken cancellationToken)
    {
        var template = catalog.Get(contract.TemplateId);
        var runId = $"{contract.JobId}-{contract.AsOfDate:yyyyMMdd}";
        Exception? lastError = null;

        for (var attempt = 0; attempt <= contract.MaxRetries; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var sections = template.Sections.Select(section =>
                {
                    var snapshot = $"snap-{section}-{contract.AsOfDate:yyyyMMdd}";
                    var checkpoint = $"recon-{section}-{contract.AsOfDate:yyyyMMdd}";
                    return new ReportingSectionManifest(section, snapshot, checkpoint, ComputeHash(runId, section, snapshot, checkpoint));
                }).ToImmutableArray();

                var manifest = new ReportingOutputManifest(
                    runId,
                    contract.TemplateId,
                    contract.AsOfDate,
                    ReportingRunStatus.Draft,
                    sections,
                    [
                        $"{runId}.json",
                        $"{runId}.pdf"
                    ]);

                manifests[runId] = manifest;
                AppendAudit(runId, "RunGenerated", contract.RequestedBy, $"trigger={contract.Trigger}; attempt={attempt + 1}");
                return Task.FromResult(manifest);
            }
            catch (Exception ex)
            {
                lastError = ex;
                AppendAudit(runId, "RunFailed", contract.RequestedBy, $"attempt={attempt + 1}; error={ex.Message}");
            }
        }

        throw new InvalidOperationException($"Reporting run failed after {contract.MaxRetries + 1} attempts.", lastError);
    }

    public IReadOnlyList<ReportingRunAuditEntry> GetAudit(string runId)
        => audits.TryGetValue(runId, out var entries) ? entries.AsReadOnly() : [];

    public Task<bool> TransitionApprovalAsync(string runId, ReportingRunStatus target, string actor, string role, string notes, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!manifests.TryGetValue(runId, out var current))
        {
            return Task.FromResult(false);
        }

        if (!IsTransitionAllowed(current.Status, target))
        {
            return Task.FromResult(false);
        }

        if (AllowedRoles.TryGetValue(target, out var roles) && !roles.Contains(role, StringComparer.OrdinalIgnoreCase))
        {
            AppendAudit(runId, "ApprovalDenied", actor, $"target={target}; role={role}; notes={notes}");
            return Task.FromResult(false);
        }

        var updated = current with { Status = target };
        manifests[runId] = updated;
        AppendAudit(runId, "ApprovalTransition", actor, $"{current.Status}->{target}; role={role}; notes={notes}");
        return Task.FromResult(true);
    }

    private static bool IsTransitionAllowed(ReportingRunStatus from, ReportingRunStatus to)
        => (from, to) switch
        {
            (ReportingRunStatus.Draft, ReportingRunStatus.InReview) => true,
            (ReportingRunStatus.InReview, ReportingRunStatus.Approved) => true,
            (ReportingRunStatus.Approved, ReportingRunStatus.Released) => true,
            _ => false
        };

    private void AppendAudit(string runId, string action, string actor, string notes)
    {
        var queue = audits.GetOrAdd(runId, static _ => []);
        queue.Add(new ReportingRunAuditEntry(runId, DateTimeOffset.UtcNow, action, actor, notes));
    }

    private static string ComputeHash(params string[] values)
    {
        var joined = string.Join('|', values);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(joined));
        return Convert.ToHexString(bytes);
    }
}

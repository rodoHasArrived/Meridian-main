using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;

namespace Meridian.Application.Reporting;

public interface IReportingOrchestrationService
{
    Task<ReportingOutputManifest> ExecuteAsync(ReportingJobContract contract, CancellationToken cancellationToken);
    Task<IReadOnlyList<ReportingOutputManifest>> ExecuteDueSchedulesAsync(IEnumerable<ReportingScheduleContract> schedules, DateTimeOffset nowUtc, CancellationToken cancellationToken);
    ReportingOutputManifest? GetManifest(string runId);
    IReadOnlyList<ReportingRunAuditEntry> GetAudit(string runId);
    Task<bool> TransitionApprovalAsync(string runId, ReportingRunStatus target, string actor, string role, string notes, CancellationToken cancellationToken);
}

public interface IReportingTemplateCatalog
{
    ReportingTemplateMetadata Get(string templateId);
    IReadOnlyList<ReportingTemplateMetadata> ListTemplates();
}

public interface IReportingSectionRenderer
{
    ReportingSectionManifest RenderSection(string runId, ReportingJobContract contract, ReportingTemplateMetadata template, string sectionId, int attempt);
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
    private readonly IReportingSectionRenderer renderer;
    private readonly Func<DateTimeOffset> utcNow;
    private readonly ConcurrentDictionary<string, ReportingOutputManifest> manifests = new();
    private readonly ConcurrentDictionary<string, object> auditLocks = new();
    private readonly ConcurrentDictionary<string, List<ReportingRunAuditEntry>> audits = new();

    public ReportingOrchestrationService(IReportingTemplateCatalog catalog)
        : this(catalog, new DeterministicReportingSectionRenderer(), () => DateTimeOffset.UtcNow)
    {
    }

    public ReportingOrchestrationService(
        IReportingTemplateCatalog catalog,
        IReportingSectionRenderer renderer,
        Func<DateTimeOffset> utcNow)
    {
        this.catalog = catalog;
        this.renderer = renderer;
        this.utcNow = utcNow;
    }

    public Task<ReportingOutputManifest> ExecuteAsync(ReportingJobContract contract, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(contract);
        if (contract.MaxRetries < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(contract), "MaxRetries must be zero or greater.");
        }

        var runId = BuildRunId(contract);
        Exception? lastError = null;

        for (var attempt = 1; attempt <= contract.MaxRetries + 1; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var template = catalog.Get(contract.TemplateId);
                var sections = template.Sections
                    .Select(section => renderer.RenderSection(runId, contract, template, section, attempt))
                    .ToImmutableArray();

                var manifest = new ReportingOutputManifest(
                    runId,
                    contract.TemplateId,
                    contract.AsOfDate,
                    ReportingRunStatus.Draft,
                    sections,
                    [
                        $"{runId}.manifest.json",
                        $"{runId}.pdf"
                    ],
                    attempt,
                    contract.Trigger,
                    contract.ScheduleId);

                manifests[runId] = manifest;
                AppendAudit(runId, "RunGenerated", contract.RequestedBy, $"trigger={contract.Trigger}; attempt={attempt}; lineageSections={sections.Length}");
                return Task.FromResult(manifest);
            }
            catch (Exception ex) when (attempt <= contract.MaxRetries)
            {
                lastError = ex;
                AppendAudit(runId, "RunRetry", contract.RequestedBy, $"attempt={attempt}; error={ex.Message}");
            }
            catch (Exception ex)
            {
                lastError = ex;
                var failed = new ReportingOutputManifest(
                    runId,
                    contract.TemplateId,
                    contract.AsOfDate,
                    ReportingRunStatus.Failed,
                    [],
                    [],
                    attempt,
                    contract.Trigger,
                    contract.ScheduleId,
                    ex.Message);
                manifests[runId] = failed;
                AppendAudit(runId, "RunFailed", contract.RequestedBy, $"attempt={attempt}; error={ex.Message}");
                throw new InvalidOperationException($"Reporting run failed after {attempt} attempts.", lastError);
            }
        }

        throw new InvalidOperationException($"Reporting run failed after {contract.MaxRetries + 1} attempts.", lastError);
    }

    public async Task<IReadOnlyList<ReportingOutputManifest>> ExecuteDueSchedulesAsync(IEnumerable<ReportingScheduleContract> schedules, DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(schedules);
        var generated = new List<ReportingOutputManifest>();
        foreach (var schedule in schedules.OrderBy(static value => value.DueAtUtc).ThenBy(static value => value.ScheduleId, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (schedule.DueAtUtc > nowUtc)
            {
                continue;
            }

            var contract = new ReportingJobContract(
                JobId: schedule.ScheduleId,
                TemplateId: schedule.TemplateId,
                AsOfDate: schedule.NextAsOfDate,
                Trigger: ReportingRunTrigger.Scheduled,
                MaxRetries: schedule.MaxRetries,
                RequestedBy: schedule.RequestedBy,
                RequestedAtUtc: nowUtc,
                CronExpression: schedule.CronExpression,
                ScheduleId: schedule.ScheduleId);
            generated.Add(await ExecuteAsync(contract, cancellationToken).ConfigureAwait(false));
        }

        return generated;
    }

    public ReportingOutputManifest? GetManifest(string runId)
        => manifests.TryGetValue(runId, out var manifest) ? manifest : null;

    public IReadOnlyList<ReportingRunAuditEntry> GetAudit(string runId)
    {
        if (!audits.TryGetValue(runId, out var entries))
        {
            return [];
        }

        var auditLock = auditLocks.GetOrAdd(runId, static _ => new object());
        lock (auditLock)
        {
            return entries.ToArray();
        }
    }

    public Task<bool> TransitionApprovalAsync(string runId, ReportingRunStatus target, string actor, string role, string notes, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!manifests.TryGetValue(runId, out var current))
        {
            return Task.FromResult(false);
        }

        if (!IsTransitionAllowed(current.Status, target))
        {
            AppendAudit(runId, "ApprovalDenied", actor, $"from={current.Status}; target={target}; role={role}; notes={notes}");
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
        var auditLock = auditLocks.GetOrAdd(runId, static _ => new object());
        lock (auditLock)
        {
            queue.Add(new ReportingRunAuditEntry(runId, utcNow(), action, actor, notes));
        }
    }

    private static string BuildRunId(ReportingJobContract contract)
        => $"{contract.JobId}-{contract.AsOfDate:yyyyMMdd}";
}

public sealed class DeterministicReportingSectionRenderer : IReportingSectionRenderer
{
    public ReportingSectionManifest RenderSection(string runId, ReportingJobContract contract, ReportingTemplateMetadata template, string sectionId, int attempt)
    {
        var snapshot = $"snap-{template.TemplateId}-{sectionId}-{contract.AsOfDate:yyyyMMdd}";
        var snapshotHash = ComputeHash(template.TemplateId, template.Version, sectionId, snapshot);
        var checkpoint = $"recon-{sectionId}-{contract.AsOfDate:yyyyMMdd}";
        var lineage = new ReportingLineageReference(sectionId, snapshot, snapshotHash, checkpoint, contract.RequestedAtUtc);
        return new ReportingSectionManifest(sectionId, snapshot, checkpoint, ComputeHash(runId, sectionId, snapshot, checkpoint, snapshotHash), lineage);
    }

    private static string ComputeHash(params string[] values)
    {
        var joined = string.Join('|', values);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(joined));
        return Convert.ToHexString(bytes);
    }
}

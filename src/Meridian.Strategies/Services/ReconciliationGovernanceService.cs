using System.Text;
using System.Text.Json;
using Meridian.Contracts.Workstation;

namespace Meridian.Strategies.Services;

public sealed record ReconciliationPolicyThresholds(
    int MaxOpenBreakCount = 0,
    int MaxCriticalOpenBreakCount = 0,
    decimal MaxAbsoluteVariance = 0.01m,
    int MaxBreakAgeHours = 24,
    bool RequireSecondaryApprovalForWaivers = true);

public sealed record ReconciliationGateEvaluation(
    TradingAcceptanceGateStatusDto Status,
    string Detail,
    int OpenBreakCount,
    int CriticalOpenBreakCount,
    decimal MaxObservedAbsoluteVariance,
    bool SecondaryApprovalRequired);

public interface IReconciliationGovernanceAuditStore
{
    Task AppendAsync(ReconciliationGateEvaluation evaluation, CancellationToken ct = default);
}

public sealed class JsonlReconciliationGovernanceAuditStore(string path) : IReconciliationGovernanceAuditStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = false };
    public async Task AppendAsync(ReconciliationGateEvaluation evaluation, CancellationToken ct = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        var payload = JsonSerializer.Serialize(new
        {
            asOf = DateTimeOffset.UtcNow,
            status = evaluation.Status.ToString(),
            evaluation.Detail,
            evaluation.OpenBreakCount,
            evaluation.CriticalOpenBreakCount,
            evaluation.MaxObservedAbsoluteVariance,
            evaluation.SecondaryApprovalRequired
        }, JsonOptions);
        await File.AppendAllTextAsync(path, payload + Environment.NewLine, Encoding.UTF8, ct).ConfigureAwait(false);
    }
}

public sealed class ReconciliationGovernanceService(
    IReconciliationRunRepository repository,
    IReconciliationGovernanceAuditStore? auditStore = null)
{
    public async Task<ReconciliationGateEvaluation> EvaluateGateAsync(
        string runId,
        ReconciliationPolicyThresholds policy,
        bool waiverRequested,
        bool secondaryApprovalSigned,
        CancellationToken ct = default,
        bool writeAudit = true)
    {
        var latest = await repository.GetLatestForRunAsync(runId, ct).ConfigureAwait(false);
        if (latest is null)
        {
            var missing = new ReconciliationGateEvaluation(
                TradingAcceptanceGateStatusDto.Blocked,
                "No reconciliation run is available for this strategy run.",
                0,
                0,
                0m,
                false);
            if (writeAudit && auditStore is not null)
                await auditStore.AppendAsync(missing, ct).ConfigureAwait(false);
            return missing;
        }

        var openBreaks = latest.Breaks.Where(static b => b.Status is not ReconciliationBreakStatus.Resolved and not ReconciliationBreakStatus.Matched).ToArray();
        var openCount = openBreaks.Length;
        var criticalCount = openBreaks.Count(static b => b.Severity == ReconciliationBreakSeverity.Critical);
        var maxAbsVariance = openBreaks.Select(static b => Math.Abs(b.Variance)).DefaultIfEmpty(0m).Max();

        var secondaryRequired = waiverRequested && policy.RequireSecondaryApprovalForWaivers;
        var breached = openCount > policy.MaxOpenBreakCount || criticalCount > policy.MaxCriticalOpenBreakCount || maxAbsVariance > policy.MaxAbsoluteVariance;
        var reviewRequired = breached && waiverRequested && (!secondaryRequired || secondaryApprovalSigned);
        var blocked = breached && (!waiverRequested || (secondaryRequired && !secondaryApprovalSigned));

        var status = blocked
            ? TradingAcceptanceGateStatusDto.Blocked
            : reviewRequired
                ? TradingAcceptanceGateStatusDto.ReviewRequired
                : TradingAcceptanceGateStatusDto.Ready;

        var detail = breached
            ? $"Policy breached (open={openCount}, critical={criticalCount}, maxVariance={maxAbsVariance:0.####})."
            : $"Policy within threshold (open={openCount}, critical={criticalCount}, maxVariance={maxAbsVariance:0.####}).";

        var evaluation = new ReconciliationGateEvaluation(status, detail, openCount, criticalCount, maxAbsVariance, secondaryRequired);
        if (writeAudit && auditStore is not null)
            await auditStore.AppendAsync(evaluation, ct).ConfigureAwait(false);
        return evaluation;
    }

    public static async Task<string> ExportEvidenceAsync(
        ReconciliationGateEvaluation evaluation,
        string outputDirectory,
        string runId,
        CancellationToken ct = default)
    {
        Directory.CreateDirectory(outputDirectory);
        var baseName = $"reconciliation-evidence-{runId}-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
        var jsonPath = Path.Combine(outputDirectory, baseName + ".json");
        var mdPath = Path.Combine(outputDirectory, baseName + ".md");

        await File.WriteAllTextAsync(jsonPath, JsonSerializer.Serialize(evaluation, new JsonSerializerOptions { WriteIndented = true }), ct).ConfigureAwait(false);
        var summary = $"# Reconciliation Evidence\n\n- Run: `{runId}`\n- Status: `{evaluation.Status}`\n- Detail: {evaluation.Detail}\n- Open breaks: {evaluation.OpenBreakCount}\n- Critical open breaks: {evaluation.CriticalOpenBreakCount}\n- Max absolute variance: {evaluation.MaxObservedAbsoluteVariance}\n- Secondary approval required: {evaluation.SecondaryApprovalRequired}\n";
        await File.WriteAllTextAsync(mdPath, summary, ct).ConfigureAwait(false);
        return jsonPath;
    }
}

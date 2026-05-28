using Meridian.Contracts.Workstation;
using Meridian.Strategies.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Ui.Shared.Evidence;

public sealed class EvidenceSubjectResolver
{
    public const string StrategyRunKind = "strategy-run";
    public const string PaperReadinessKind = "paper-readiness";
    public const string ReconciliationReviewKind = "reconciliation-review";
    public const string StatementRunKind = "statement-run";
    public const string ReportPackKind = "report-pack";
    public const string ProviderTrustKind = "provider-trust";
    public const string AnalysisExportKind = "analysis-export";

    private static readonly HashSet<string> SupportedKinds = new(StringComparer.OrdinalIgnoreCase)
    {
        StrategyRunKind,
        PaperReadinessKind,
        ReconciliationReviewKind,
        StatementRunKind,
        ReportPackKind,
        ProviderTrustKind,
        AnalysisExportKind
    };

    private readonly IServiceProvider _services;

    public EvidenceSubjectResolver(IServiceProvider services)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
    }

    public bool IsSupportedKind(string? subjectKind)
        => !string.IsNullOrWhiteSpace(subjectKind) && SupportedKinds.Contains(subjectKind);

    public async Task<IReadOnlyList<EvidenceSubjectDto>> ListAsync(CancellationToken ct = default)
    {
        var subjects = new List<EvidenceSubjectDto>
        {
            new(
                SubjectId: "current",
                SubjectKind: PaperReadinessKind,
                Label: "Current paper trading readiness",
                Workspace: "Trading",
                Route: "/trading/readiness",
                PageTag: "TradingReadiness"),
            new(
                SubjectId: "current",
                SubjectKind: ReportPackKind,
                Label: "Current report-pack output",
                Workspace: "Reporting",
                Route: "/reporting",
                PageTag: "ReportingShell"),
            new(
                SubjectId: "dk1",
                SubjectKind: ProviderTrustKind,
                Label: "DK1 provider trust gate",
                Workspace: "Data",
                Route: "/data",
                PageTag: "ProviderHealth")
        };

        var runService = _services.GetService<StrategyRunReadService>();
        if (runService is not null)
        {
            var runs = await runService.GetRunsAsync(ct: ct).ConfigureAwait(false);
            subjects.AddRange(runs.Take(100).Select(run => new EvidenceSubjectDto(
                SubjectId: run.RunId,
                SubjectKind: StrategyRunKind,
                Label: $"{run.StrategyName} {run.Mode} run",
                Workspace: ResolveWorkspace(run.Mode),
                Route: $"/strategy?runId={Uri.EscapeDataString(run.RunId)}",
                PageTag: "StrategyRuns")));
        }

        return subjects
            .OrderBy(static subject => subject.Workspace, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static subject => subject.Label, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<EvidenceSubjectDto?> ResolveAsync(
        string subjectKind,
        string subjectId,
        CancellationToken ct = default)
    {
        if (!IsSupportedKind(subjectKind) || string.IsNullOrWhiteSpace(subjectId))
        {
            return null;
        }

        if (string.Equals(subjectKind, StrategyRunKind, StringComparison.OrdinalIgnoreCase))
        {
            var runService = _services.GetService<StrategyRunReadService>();
            if (runService is null)
            {
                return null;
            }

            var run = await runService.GetRunDetailAsync(subjectId, ct).ConfigureAwait(false);
            return run is null
                ? null
                : new EvidenceSubjectDto(
                    SubjectId: run.Summary.RunId,
                    SubjectKind: StrategyRunKind,
                    Label: $"{run.Summary.StrategyName} {run.Summary.Mode} run",
                    Workspace: ResolveWorkspace(run.Summary.Mode),
                    Route: $"/strategy?runId={Uri.EscapeDataString(run.Summary.RunId)}",
                    PageTag: "StrategyRuns");
        }

        return subjectKind.ToLowerInvariant() switch
        {
            PaperReadinessKind => new EvidenceSubjectDto(
                SubjectId: subjectId,
                SubjectKind: PaperReadinessKind,
                Label: "Current paper trading readiness",
                Workspace: "Trading",
                Route: "/trading/readiness",
                PageTag: "TradingReadiness"),
            ReconciliationReviewKind => new EvidenceSubjectDto(
                SubjectId: subjectId,
                SubjectKind: ReconciliationReviewKind,
                Label: $"Reconciliation review {subjectId}",
                Workspace: "Accounting",
                Route: "/accounting",
                PageTag: "FundReconciliation"),
            StatementRunKind => new EvidenceSubjectDto(
                SubjectId: subjectId,
                SubjectKind: StatementRunKind,
                Label: $"Statement run {subjectId}",
                Workspace: "Accounting",
                Route: $"/accounting?statementRunId={Uri.EscapeDataString(subjectId)}",
                PageTag: "FundReconciliation"),
            ReportPackKind => new EvidenceSubjectDto(
                SubjectId: subjectId,
                SubjectKind: ReportPackKind,
                Label: "Report-pack output",
                Workspace: "Reporting",
                Route: "/reporting",
                PageTag: "ReportingShell"),
            ProviderTrustKind => new EvidenceSubjectDto(
                SubjectId: subjectId,
                SubjectKind: ProviderTrustKind,
                Label: "Provider trust gate",
                Workspace: "Data",
                Route: "/data",
                PageTag: "ProviderHealth"),
            AnalysisExportKind => new EvidenceSubjectDto(
                SubjectId: subjectId,
                SubjectKind: AnalysisExportKind,
                Label: $"Analysis export {subjectId}",
                Workspace: "Reporting",
                Route: "/reporting",
                PageTag: "ReportingShell"),
            _ => null
        };
    }

    private static string ResolveWorkspace(StrategyRunMode mode)
        => mode is StrategyRunMode.Paper or StrategyRunMode.Live ? "Trading" : "Strategy";
}

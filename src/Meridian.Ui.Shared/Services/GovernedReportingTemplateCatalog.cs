using System.Collections.Immutable;
using Meridian.Contracts.Workstation;
using Meridian.Reporting;

namespace Meridian.Ui.Shared.Services;

public sealed class GovernedReportingTemplateCatalog : IReportingTemplateCatalog
{
    private readonly IReportingTemplateCatalog _fallbackCatalog;
    private readonly ReportTemplateRegistryService _templateRegistry;

    public GovernedReportingTemplateCatalog(
        IReportingTemplateCatalog fallbackCatalog,
        ReportTemplateRegistryService templateRegistry)
    {
        _fallbackCatalog = fallbackCatalog ?? throw new ArgumentNullException(nameof(fallbackCatalog));
        _templateRegistry = templateRegistry ?? throw new ArgumentNullException(nameof(templateRegistry));
    }

    public ReportingTemplateMetadata Get(string templateId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templateId);
        var governed = FindLatestApproved(templateId.Trim());
        return governed is null
            ? _fallbackCatalog.Get(templateId)
            : Project(governed);
    }

    public IReadOnlyList<ReportingTemplateMetadata> ListTemplates()
    {
        var governed = _templateRegistry
            .List()
            .Where(static record => record.Status == ReportTemplateLifecycleStatusDto.Approved)
            .GroupBy(static record => record.Definition.TemplateId.Name, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group
                .OrderByDescending(static record => record.Definition.TemplateId.Version)
                .First())
            .Select(Project);

        return governed
            .Concat(_fallbackCatalog.ListTemplates())
            .GroupBy(static template => template.TemplateId, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .OrderBy(static template => template.TemplateId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public ReportAccessEvaluationDto EvaluateAccess(string templateId, ReportAccessQueryContext? accessContext)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templateId);
        var governed = FindLatestApproved(templateId.Trim());
        return governed is null
            ? ReportAccessPolicyEvaluator.Evaluate(null, accessContext)
            : ReportAccessPolicyEvaluator.Evaluate(governed.Definition.AccessPolicy, accessContext);
    }

    private ReportTemplateGovernanceRecordDto? FindLatestApproved(string templateId) =>
        _templateRegistry
            .List()
            .Where(record =>
                record.Status == ReportTemplateLifecycleStatusDto.Approved &&
                string.Equals(record.Definition.TemplateId.Name, templateId, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(static record => record.Definition.TemplateId.Version)
            .FirstOrDefault();

    private static ReportingTemplateMetadata Project(ReportTemplateGovernanceRecordDto record)
    {
        var definition = record.Definition;
        var sections = definition.Sections is { Count: > 0 }
            ? definition.Sections.ToImmutableArray()
            : (definition.Grids ?? [])
                .Select(static grid => grid.GridId)
                .Where(static gridId => !string.IsNullOrWhiteSpace(gridId))
                .ToImmutableArray();
        if (sections.Length == 0)
        {
            sections = ["report-writer"];
        }

        var family = Enum.TryParse<ReportingTemplateFamily>(record.Family, ignoreCase: true, out var parsedFamily)
            ? parsedFamily
            : ReportingTemplateFamily.CustomReport;
        return new ReportingTemplateMetadata(
            definition.TemplateId.Name,
            family,
            definition.DisplayName,
            $"{definition.TemplateId.Version}.0.0",
            sections,
            ImmutableDictionary<string, string>.Empty
                .Add("source", "report-template-registry")
                .Add("templateVersion", definition.TemplateId.Version.ToString())
                .Add("lifecycleStatus", record.Status.ToString())
                .Add("isBuiltIn", record.IsBuiltIn.ToString())
                .Add("gridCount", (definition.Grids?.Count ?? 0).ToString()),
            definition.Grids,
            definition.AccessPolicy);
    }
}

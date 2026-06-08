using System.Collections.Immutable;

namespace Meridian.Reporting;

public sealed class DefaultReportingTemplateCatalog : IReportingTemplateCatalog
{
    private readonly IReadOnlyDictionary<string, ReportingTemplateMetadata> templates = new Dictionary<string, ReportingTemplateMetadata>(StringComparer.OrdinalIgnoreCase)
    {
        ["investor-monthly-statement"] = new(
            "investor-monthly-statement",
            ReportingTemplateFamily.InvestorStatement,
            "Investor Monthly Statement",
            "1.0.0",
            ["cover", "performance", "positions", "flows"],
            ImmutableDictionary<string, string>.Empty.Add("audience", "investor")),
        ["sec-13f-packet"] = new(
            "sec-13f-packet",
            ReportingTemplateFamily.SecFilingPacket,
            "SEC 13F Filing Packet",
            "1.0.0",
            ["cover", "holdings", "attestation"],
            ImmutableDictionary<string, string>.Empty.Add("audience", "regulator")),
        ["shadow-nav-daily-pack"] = new(
            "shadow-nav-daily-pack",
            ReportingTemplateFamily.ShadowNavPack,
            "Shadow NAV Daily Pack",
            "1.0.0",
            ["cover", "valuation", "breaks", "signoff"],
            ImmutableDictionary<string, string>.Empty.Add("audience", "ops")),
        ["performance-quarterly-report"] = new(
            "performance-quarterly-report",
            ReportingTemplateFamily.PerformanceReport,
            "Performance Quarterly Report",
            "1.0.0",
            ["cover", "performance", "benchmark", "attribution"],
            ImmutableDictionary<string, string>.Empty.Add("audience", "investment-committee")),
        ["holdings-board-report"] = new(
            "holdings-board-report",
            ReportingTemplateFamily.HoldingsReport,
            "Holdings Board Report",
            "1.0.0",
            ["cover", "holdings", "asset-class", "exceptions"],
            ImmutableDictionary<string, string>.Empty.Add("audience", "board")),
        ["capital-account-statement"] = new(
            "capital-account-statement",
            ReportingTemplateFamily.CapitalAccountStatement,
            "Capital Account Statement",
            "1.0.0",
            ["cover", "capital-balance", "flows", "allocation"],
            ImmutableDictionary<string, string>.Empty.Add("audience", "investor")),
        ["board-governance-packet"] = new(
            "board-governance-packet",
            ReportingTemplateFamily.BoardPacket,
            "Board Governance Packet",
            "1.0.0",
            ["cover", "operations-record", "approvals", "risk-exceptions"],
            ImmutableDictionary<string, string>.Empty.Add("audience", "board")),
        ["audit-evidence-package"] = new(
            "audit-evidence-package",
            ReportingTemplateFamily.AuditPackage,
            "Audit Evidence Package",
            "1.0.0",
            ["cover", "source-evidence", "reconciliation", "lineage"],
            ImmutableDictionary<string, string>.Empty.Add("audience", "auditor")),
        ["certified-dataset-export"] = new(
            "certified-dataset-export",
            ReportingTemplateFamily.CertifiedDataset,
            "Certified Dataset Export",
            "1.0.0",
            ["cover", "dataset-snapshot", "hashes", "lineage"],
            ImmutableDictionary<string, string>.Empty.Add("audience", "data-governance"))
    };

    public ReportingTemplateMetadata Get(string templateId)
        => templates.TryGetValue(templateId, out var value)
            ? value
            : throw new KeyNotFoundException($"Unknown reporting template '{templateId}'.");

    public IReadOnlyList<ReportingTemplateMetadata> ListTemplates()
        => templates.Values.OrderBy(static template => template.TemplateId, StringComparer.OrdinalIgnoreCase).ToArray();
}

using System.Collections.Immutable;
using Meridian.Modularity;

namespace Meridian.Reporting;

public sealed class DefaultReportingTemplateCatalog : IReportingTemplateCatalog
{
    private static readonly IReadOnlyList<ReportingTemplateMetadata> BuiltInTemplates =
    [
        new(
            "investor-monthly-statement",
            ReportingTemplateFamily.InvestorStatement,
            "Investor Monthly Statement",
            "1.0.0",
            ["cover", "performance", "positions", "flows"],
            ImmutableDictionary<string, string>.Empty.Add("audience", "investor")),
        new(
            "sec-13f-packet",
            ReportingTemplateFamily.SecFilingPacket,
            "SEC 13F Filing Packet",
            "1.0.0",
            ["cover", "holdings", "attestation"],
            ImmutableDictionary<string, string>.Empty.Add("audience", "regulator")),
        new(
            "shadow-nav-daily-pack",
            ReportingTemplateFamily.ShadowNavPack,
            "Shadow NAV Daily Pack",
            "1.0.0",
            ["cover", "valuation", "breaks", "signoff"],
            ImmutableDictionary<string, string>.Empty.Add("audience", "ops")),
        new(
            "performance-quarterly-report",
            ReportingTemplateFamily.PerformanceReport,
            "Performance Quarterly Report",
            "1.0.0",
            ["cover", "performance", "benchmark", "attribution"],
            ImmutableDictionary<string, string>.Empty.Add("audience", "investment-committee")),
        new(
            "holdings-board-report",
            ReportingTemplateFamily.HoldingsReport,
            "Holdings Board Report",
            "1.0.0",
            ["cover", "holdings", "asset-class", "exceptions"],
            ImmutableDictionary<string, string>.Empty.Add("audience", "board")),
        new(
            "capital-account-statement",
            ReportingTemplateFamily.CapitalAccountStatement,
            "Capital Account Statement",
            "1.0.0",
            ["cover", "capital-balance", "flows", "allocation"],
            ImmutableDictionary<string, string>.Empty.Add("audience", "investor")),
        new(
            "board-governance-packet",
            ReportingTemplateFamily.BoardPacket,
            "Board Governance Packet",
            "1.0.0",
            ["cover", "operations-record", "approvals", "risk-exceptions"],
            ImmutableDictionary<string, string>.Empty.Add("audience", "board")),
        new(
            "audit-evidence-package",
            ReportingTemplateFamily.AuditPackage,
            "Audit Evidence Package",
            "1.0.0",
            ["cover", "source-evidence", "reconciliation", "lineage"],
            ImmutableDictionary<string, string>.Empty.Add("audience", "auditor")),
        new(
            "certified-dataset-export",
            ReportingTemplateFamily.CertifiedDataset,
            "Certified Dataset Export",
            "1.0.0",
            ["cover", "dataset-snapshot", "hashes", "lineage"],
            ImmutableDictionary<string, string>.Empty.Add("audience", "data-governance"))
    ];

    private readonly ExtensionRegistry<ReportingTemplateMetadata> templates;
    private readonly IReadOnlyList<ReportingTemplateMetadata> sortedTemplates;

    /// <summary>
    /// Creates a catalog containing only the built-in reporting templates.
    /// </summary>
    public DefaultReportingTemplateCatalog()
        : this(Array.Empty<ReportingTemplateMetadata>())
    {
    }

    /// <summary>
    /// Creates a catalog seeded with the built-in templates plus any additional templates
    /// (for example DI-registered <see cref="ReportingTemplateMetadata"/> instances). Built-in
    /// templates take precedence when a template id collides, preserving existing behaviour.
    /// </summary>
    public DefaultReportingTemplateCatalog(IEnumerable<ReportingTemplateMetadata> additionalTemplates)
    {
        ArgumentNullException.ThrowIfNull(additionalTemplates);

        templates = new ExtensionRegistry<ReportingTemplateMetadata>(static template => template.TemplateId);
        templates.AddRange(BuiltInTemplates);

        foreach (var template in additionalTemplates)
        {
            // Built-ins win on conflict; additional templates extend the catalog.
            templates.TryAdd(template);
        }

        // The catalog is immutable after construction, so pre-sort once instead of on every call.
        sortedTemplates = templates.Entries
            .OrderBy(static template => template.TemplateId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public ReportingTemplateMetadata Get(string templateId)
        => templates.TryGet(templateId, out var value)
            ? value
            : throw new KeyNotFoundException($"Unknown reporting template '{templateId}'.");

    public IReadOnlyList<ReportingTemplateMetadata> ListTemplates()
        => sortedTemplates;
}

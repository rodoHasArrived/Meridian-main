using System.Collections.Immutable;

namespace Meridian.Application.Reporting;

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
            ImmutableDictionary<string, string>.Empty.Add("audience", "ops"))
    };

    public ReportingTemplateMetadata Get(string templateId)
        => templates.TryGetValue(templateId, out var value)
            ? value
            : throw new KeyNotFoundException($"Unknown reporting template '{templateId}'.");

    public IReadOnlyList<ReportingTemplateMetadata> ListTemplates()
        => templates.Values.OrderBy(static template => template.TemplateId, StringComparer.OrdinalIgnoreCase).ToArray();
}

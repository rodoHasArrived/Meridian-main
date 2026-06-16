using System.Collections.Immutable;
using FluentAssertions;
using Meridian.Reporting;

namespace Meridian.Tests.Reporting;

/// <summary>
/// Behaviour-preservation tests for <see cref="DefaultReportingTemplateCatalog"/> after its
/// migration onto the shared <c>ExtensionRegistry</c> primitive. Pins the built-in catalog,
/// the id-sorted listing, and the <see cref="KeyNotFoundException"/> contract, and exercises
/// the new extensibility seam.
/// </summary>
public sealed class DefaultReportingTemplateCatalogBehaviorTests
{
    // ListTemplates orders by TemplateId (OrdinalIgnoreCase), independent of declaration order.
    private static readonly string[] ExpectedListedOrder =
    [
        "audit-evidence-package",
        "board-governance-packet",
        "capital-account-statement",
        "certified-dataset-export",
        "holdings-board-report",
        "investor-monthly-statement",
        "performance-quarterly-report",
        "sec-13f-packet",
        "shadow-nav-daily-pack"
    ];

    [Fact]
    public void ListTemplates_ReturnsBuiltInSet_SortedById()
    {
        new DefaultReportingTemplateCatalog().ListTemplates().Select(t => t.TemplateId)
            .Should().Equal(ExpectedListedOrder);
    }

    [Fact]
    public void Get_ReturnsTemplate_ForKnownId()
    {
        var template = new DefaultReportingTemplateCatalog().Get("sec-13f-packet");

        template.TemplateId.Should().Be("sec-13f-packet");
        template.Family.Should().Be(ReportingTemplateFamily.SecFilingPacket);
    }

    [Fact]
    public void Get_Throws_ForUnknownId()
    {
        var act = () => new DefaultReportingTemplateCatalog().Get("nope");

        act.Should().Throw<KeyNotFoundException>().WithMessage("Unknown reporting template 'nope'.");
    }

    [Fact]
    public void AdditionalTemplates_ExtendCatalog()
    {
        var extra = new ReportingTemplateMetadata(
            "custom-report",
            ReportingTemplateFamily.PerformanceReport,
            "Custom Report",
            "1.0.0",
            ["cover"],
            ImmutableDictionary<string, string>.Empty);

        var catalog = new DefaultReportingTemplateCatalog([extra]);

        catalog.ListTemplates().Should().HaveCount(ExpectedListedOrder.Length + 1);
        catalog.Get("custom-report").Should().Be(extra);
    }

    [Fact]
    public void AdditionalTemplates_DoNotOverrideBuiltIns_OnConflict()
    {
        var conflicting = new ReportingTemplateMetadata(
            "sec-13f-packet",
            ReportingTemplateFamily.PerformanceReport,
            "Tampered",
            "9.9.9",
            ["tampered"],
            ImmutableDictionary<string, string>.Empty);

        var catalog = new DefaultReportingTemplateCatalog([conflicting]);

        catalog.ListTemplates().Should().HaveCount(ExpectedListedOrder.Length);
        catalog.Get("sec-13f-packet").Name.Should().Be("SEC 13F Filing Packet");
    }
}

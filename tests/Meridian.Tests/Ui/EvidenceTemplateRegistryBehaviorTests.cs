using FluentAssertions;
using Meridian.Contracts.Workstation;
using Meridian.Ui.Shared.Evidence;

namespace Meridian.Tests.Ui;

/// <summary>
/// Behaviour-preservation tests for <see cref="EvidenceTemplateRegistry"/> after its migration
/// onto the shared <c>ExtensionRegistry</c> primitive. Pins the exact built-in catalog and the
/// public accessor semantics so the migration is provably non-breaking, and exercises the new
/// DI extensibility seam.
/// </summary>
public sealed class EvidenceTemplateRegistryBehaviorTests
{
    private static readonly string[] ExpectedBuiltInWorkflowIds =
    [
        "strategy-to-paper-review",
        "paper-trading-readiness",
        "accounting-reconciliation-review",
        "portfolio-reporting-output",
        "report-pack-delivery-review",
        "security-master-conflict-review",
        "operations-approval-review",
        "accounting-records-evidence-review",
        "private-capital-fund-event-review",
        "payment-intent-cash-evidence-review",
        "retained-vault-artifact-review"
    ];

    [Fact]
    public void GetTemplates_ReturnsBuiltInSet_InDeclaredOrder()
    {
        var registry = new EvidenceTemplateRegistry();

        registry.GetTemplates().Select(t => t.WorkflowId)
            .Should().Equal(ExpectedBuiltInWorkflowIds);
    }

    [Fact]
    public void GetTemplate_IsCaseInsensitive()
    {
        var registry = new EvidenceTemplateRegistry();

        registry.GetTemplate("OPERATIONS-APPROVAL-REVIEW")
            .Should().NotBeNull()
            .And.BeEquivalentTo(registry.GetTemplate("operations-approval-review"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("does-not-exist")]
    public void GetTemplate_ReturnsNull_ForMissingOrBlank(string? workflowId)
    {
        new EvidenceTemplateRegistry().GetTemplate(workflowId).Should().BeNull();
    }

    [Fact]
    public void AdditionalTemplates_ExtendCatalog()
    {
        var extra = new EvidenceTemplateDto(
            WorkflowId: "custom-extension-review",
            RequiredEvidenceKinds: ["custom-evidence"],
            OptionalEvidenceKinds: [],
            NoOrphanRule: true,
            ExportSettings: new EvidenceTemplateExportSettingsDto(1, true, "json"));

        var registry = new EvidenceTemplateRegistry([extra]);

        registry.GetTemplates().Should().HaveCount(ExpectedBuiltInWorkflowIds.Length + 1);
        registry.GetTemplate("custom-extension-review").Should().Be(extra);
    }

    [Fact]
    public void AdditionalTemplates_DoNotOverrideBuiltIns_OnConflict()
    {
        var conflicting = new EvidenceTemplateDto(
            WorkflowId: "operations-approval-review",
            RequiredEvidenceKinds: ["tampered"],
            OptionalEvidenceKinds: [],
            NoOrphanRule: false,
            ExportSettings: new EvidenceTemplateExportSettingsDto(1, true, "json"));

        var registry = new EvidenceTemplateRegistry([conflicting]);

        registry.GetTemplates().Should().HaveCount(ExpectedBuiltInWorkflowIds.Length);
        registry.GetTemplate("operations-approval-review")!.RequiredEvidenceKinds
            .Should().Contain("approval").And.NotContain("tampered");
    }
}

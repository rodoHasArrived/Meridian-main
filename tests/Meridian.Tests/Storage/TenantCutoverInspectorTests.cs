using FluentAssertions;
using Meridian.Storage.Tenancy;

namespace Meridian.Tests.Storage;

public sealed class TenantCutoverInspectorTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("all")]
    [InlineData(" ALL ")]
    public void Evaluate_MissingOrUnscopedLegacyOwner_BlocksCutover(string? tenant)
    {
        var result = PostgresTenantCutoverInspector.Evaluate([
            new("ledger", "ledger_books", "book:1", tenant, []),
            new("ledger", "accounting_periods", "period:1", "tenant-a", ["book:1"], true)]);
        result.IsReady.Should().BeFalse();
        result.Findings.Should().Contain(new TenantCutoverFinding("ledger", "ledger_books", "MissingTenantAttribution", 1));
        result.Findings.Should().Contain(new TenantCutoverFinding("ledger", "accounting_periods", "MissingOwnerAuthority", 1));
    }

    [Fact]
    public void Evaluate_MismatchedAccountOrWorkflowAuthority_BlocksRetainedRows()
    {
        var result = PostgresTenantCutoverInspector.Evaluate([
            new("fund-structure", "fund", "node:1", "tenant-a", []),
            new("ledger", "ledger_books", "book:1", "tenant-a", ["node:1"], true),
            new("fund-accounts", "account_definition", "account:1", "tenant-b", ["node:1"]),
            new("ledger", "operations_continuity_workflows", "workflow:1", "tenant-b", ["book:1"], true)]);
        result.IsReady.Should().BeFalse();
        result.Findings.Should().HaveCount(2).And.OnlyContain(finding => finding.Reason == "TenantAuthorityMismatch");
    }

    [Fact]
    public void Evaluate_MissingBookReferenceAndDanglingGraphReference_RemainExplicitBlockers()
    {
        var result = PostgresTenantCutoverInspector.Evaluate([
            new("ledger", "accounting_periods", "period:1", "tenant-a", [], true),
            new("fund-structure", "ownership_link", "link:1", "tenant-a", ["node:missing"], true)]);
        result.IsReady.Should().BeFalse();
        result.Findings.Should().HaveCount(2).And.OnlyContain(finding => finding.Reason == "MissingOwnerAuthority");
    }

    [Fact]
    public void Evaluate_CompleteAttribution_PreservesTenantCountsAndAcceptsNormalizedIdentity()
    {
        var result = PostgresTenantCutoverInspector.Evaluate([
            new("fund-structure", "fund", "node:a", "tenant-a", []),
            new("fund-structure", "fund", "node:b", "tenant-b", []),
            new("ledger", "ledger_books", "book:a", " Tenant-A ", ["node:a"], true),
            new("ledger", "ledger_books", "book:b", "tenant-b", ["node:b"], true),
            new("ledger", "accounting_periods", "period:a", "tenant-a", ["book:a"], true),
            new("fund-accounts", "account_definition", "account:b", "tenant-b", ["node:b"])]);
        result.IsReady.Should().BeTrue();
        result.Findings.Should().BeEmpty();
    }

    [Fact]
    public async Task Inspect_UnconfiguredStores_AreEmptyWithoutOpeningConnections()
    {
        var result = await PostgresTenantCutoverInspector.InspectAsync(null, null, null);
        result.IsReady.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_AttributedBooklessWorkflow_IsReadyWhileSuppliedMissingBookIsNot()
    {
        var row = new TenantCutoverRecord("ledger", "operations_continuity_workflows", "workflow:a", "tenant-a", []);
        PostgresTenantCutoverInspector.Evaluate([row]).IsReady.Should().BeTrue();
        PostgresTenantCutoverInspector.Evaluate([row with { TenantId = null }]).IsReady.Should().BeFalse();
        PostgresTenantCutoverInspector.Evaluate([row with { OwnerReferences = ["book:missing"] }]).IsReady.Should().BeFalse();
    }
}

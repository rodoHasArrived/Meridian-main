using FluentAssertions;
using Meridian.Contracts.Workstation;
using Meridian.Reporting;
using Meridian.Ui.Shared.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Tests.Reporting;

public sealed class ReportingTemplateRegistryServiceTests
{
    [Fact]
    public void ReportingOwnedRegistry_RequiresIndependentApprovalBeforeRendering()
    {
        var registry = new ReportingTemplateRegistryService();
        var draft = registry.CreateDraft(Template("controller-pack"), "author");
        var request = new RenderReportTemplateRequestDto(
            draft.Definition.TemplateId,
            new Dictionary<string, string> { ["period"] = "2026-11" });

        Action renderDraft = () => registry.Render(request);
        renderDraft.Should().Throw<ArgumentException>().WithMessage("Template not found.");

        registry.Submit(draft.Definition.TemplateId, "author");
        Action selfApprove = () => registry.Approve(
            draft.Definition.TemplateId,
            new ReportTemplateDecisionRequestDto("Reviewed"),
            "author");
        selfApprove.Should().Throw<UnauthorizedAccessException>();

        var approved = registry.Approve(
            draft.Definition.TemplateId,
            new ReportTemplateDecisionRequestDto("Independent review", "APP-1"),
            "controller");

        approved.AuditTrail.Select(entry => entry.Action).Should().Equal("draft", "submit", "approve");
        approved.ApprovalReference.Should().Be("APP-1");
        registry.Render(request).RenderedContent.Should().Contain("template:controller-pack@v1;sections=summary");
    }

    [Fact]
    public void ReportingOwnedRegistry_TenantAndCompanyIsolateVersionsEvenForAdministrator()
    {
        var registry = new ReportingTemplateRegistryService();
        var scopeA = Scope("tenant-a", "company-a");
        var scopeB = Scope("tenant-b", "company-a") with { HasGlobalOverride = true };
        var otherCompany = scopeA with { CompanyId = "company-b", HasGlobalOverride = true };
        var draftA = registry.CreateDraft(Template("scoped-pack"), "author-a", scopeA.CompanyId, tenantId: scopeA.TenantId);
        registry.Submit(draftA.Definition.TemplateId, "author-a", accessContext: scopeA);
        registry.Approve(draftA.Definition.TemplateId, new("Reviewed"), "controller-a", scopeA);

        registry.Get(draftA.Definition.TemplateId, scopeB).Should().BeNull();
        registry.Get(draftA.Definition.TemplateId, otherCompany).Should().BeNull();
        registry.Get(draftA.Definition.TemplateId).Should().BeNull();
        Action crossTenantApproval = () => registry.Approve(
            draftA.Definition.TemplateId, new("Administrator review"), "admin-b", scopeB);
        crossTenantApproval.Should().Throw<KeyNotFoundException>();

        var draftB = registry.CreateDraft(Template("scoped-pack"), "author-b", scopeB.CompanyId, tenantId: scopeB.TenantId);
        draftB.Definition.TemplateId.Version.Should().Be(1);
        registry.Get(draftA.Definition.TemplateId, scopeA).Should().NotBeNull();
        registry.List(scopeB).Where(record => !record.IsBuiltIn).Should().ContainSingle()
            .Which.CreatedBy.Should().Be("author-b");
    }

    [Fact]
    public void ExistingFileStore_RoundTripsBetweenReportingRegistryAndWorkstationFacadeWithoutRewritingOnRead()
    {
        var directory = Path.Combine(Path.GetTempPath(), "meridian-reporting-extraction-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "templates.json");
        var store = new FileReportTemplateGovernanceStore(
            new ReportTemplateGovernanceStoreOptions(path),
            NullLogger<FileReportTemplateGovernanceStore>.Instance);
        var scope = Scope("tenant-a", "company-a");
        var legacyScope = new ReportAccessQueryContext(
            scope.ActorPrincipalId, scope.GroupPrincipalIds, scope.CompanyId,
            scope.HasGlobalOverride, scope.TenantId, scope.RequireBoundScope);

        try
        {
            var canonical = new ReportingTemplateRegistryService(store);
            var draft = canonical.CreateDraft(Template("retained-pack"), "author", scope.CompanyId, tenantId: scope.TenantId);
            canonical.Submit(draft.Definition.TemplateId, "author", accessContext: scope);
            var approved = canonical.Approve(draft.Definition.TemplateId, new("Reviewed", "APP-RETAINED"), "controller", scope);
            var originalBytes = File.ReadAllBytes(path);

            var facade = new ReportTemplateRegistryService(store);
            var reloaded = facade.List(legacyScope).Single(record => !record.IsBuiltIn);
            reloaded.Should().BeEquivalentTo(approved);
            facade.CanAccess(draft.Definition.TemplateId, legacyScope).Should().BeTrue();
            File.ReadAllBytes(path).Should().Equal(originalBytes);

            var revision = facade.CreateDraft(Template("retained-pack"), "author", scope.CompanyId, tenantId: scope.TenantId);
            facade.Submit(revision.Definition.TemplateId, "author", accessContext: legacyScope);
            facade.Approve(revision.Definition.TemplateId, new("Second review", "APP-REVISION"), "controller", legacyScope);

            var canonicalReload = new ReportingTemplateRegistryService(store);
            canonicalReload.Get(revision.Definition.TemplateId, scope).Should().NotBeNull();
            canonicalReload.Get(draft.Definition.TemplateId, scope).Should().BeNull();
            canonicalReload.List(scope, includeSuperseded: true).Where(record => !record.IsBuiltIn)
                .Should().HaveCount(2).And.Contain(record =>
                    record.Definition.TemplateId == draft.Definition.TemplateId
                    && record.Status == ReportTemplateLifecycleStatusDto.Superseded);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void WorkstationAccessAdapter_PreservesBoundScopeAndNamedGroupAuthority()
    {
        var policy = new ReportAccessPolicyDto(
            ReportAccessModeDto.Restricted,
            Principals: [new ReportAccessPrincipalDto(ReportAccessPrincipalKindDto.Group, "controllers")],
            AllowOwnerAccess: false);
        var canonical = Scope("tenant-a", "company-a") with { GroupPrincipalIds = ["controllers"] };
        var legacy = new ReportAccessQueryContext(
            canonical.ActorPrincipalId, canonical.GroupPrincipalIds, canonical.CompanyId,
            canonical.HasGlobalOverride, canonical.TenantId, canonical.RequireBoundScope);

        var allowed = ReportingAccessPolicyEvaluator.Evaluate(policy, canonical);
        allowed.IsAccessible.Should().BeTrue();
        ReportAccessPolicyEvaluator.Evaluate(policy, legacy).Should().BeEquivalentTo(allowed);
        ReportAccessPolicyEvaluator.Evaluate(policy, legacy with { GroupPrincipalIds = [] }).IsAccessible.Should().BeFalse();
        ReportAccessPolicyEvaluator.Evaluate(policy, legacy with { TenantId = null, HasGlobalOverride = true })
            .IsAccessible.Should().BeFalse();
    }

    [Fact]
    public void ReportingOwnedStorePort_PropagatesUnreadableRetainedState()
    {
        Action construct = () => new ReportingTemplateRegistryService(new UnreadableStore());

        construct.Should().Throw<InvalidDataException>().WithMessage("Retained template state is unavailable.");
    }

    [Fact]
    public void WorkstationFacade_PreservesExplicitLegacyStoreImplementations()
    {
        var store = new ExplicitLegacyStore();
        var facade = new ReportTemplateRegistryService(store);
        var draft = facade.CreateDraft(Template("explicit-legacy-store"), "author");

        var reloaded = new ReportTemplateRegistryService(store);

        reloaded.List().Single(record => !record.IsBuiltIn).Should().BeEquivalentTo(draft);
        store.LoadCalls.Should().Be(2);
        store.SaveCalls.Should().Be(1);
    }

    private static ReportingAccessQueryContext Scope(string tenant, string company) =>
        new("operator", CompanyId: company, TenantId: tenant, RequireBoundScope: true);

    private static ReportTemplateDraftRequestDto Template(string name) =>
        new(name, "Controller Pack", ["summary"], [new("period", Required: true)]);

    private sealed class UnreadableStore : IReportingTemplateGovernanceStore
    {
        public IReadOnlyList<ReportTemplateGovernanceRecordDto> Load() =>
            throw new InvalidDataException("Retained template state is unavailable.");

        public void Save(IReadOnlyList<ReportTemplateGovernanceRecordDto> records) =>
            throw new InvalidOperationException("No writes are permitted after a failed load.");
    }

    private sealed class ExplicitLegacyStore : IReportTemplateGovernanceStore
    {
        private IReadOnlyList<ReportTemplateGovernanceRecordDto> _records = [];

        public int LoadCalls { get; private set; }

        public int SaveCalls { get; private set; }

        IReadOnlyList<ReportTemplateGovernanceRecordDto> IReportTemplateGovernanceStore.Load()
        {
            LoadCalls++;
            return _records;
        }

        void IReportTemplateGovernanceStore.Save(IReadOnlyList<ReportTemplateGovernanceRecordDto> records)
        {
            SaveCalls++;
            _records = records;
        }
    }
}

using FluentAssertions;
using Meridian.Backtesting.Sdk;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;
using Meridian.Identity.Auth;
using Meridian.Storage.Export;
using Meridian.Strategies.Models;
using Meridian.Strategies.Services;
using Meridian.Strategies.Storage;
using Meridian.Ui.Shared.Endpoints;
using Meridian.Ui.Shared.Evidence;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Meridian.Tests.Ui.Evidence;

/// <summary>
/// Guards the evidence contributors that project Security Master conflicts, retained
/// vault artifacts, export profiles, and DK1 provider-trust readiness — plus the
/// unregistered-dependency guard of every contributor that resolves services lazily.
/// The failure mode under guard: open data conflicts or missing trust evidence
/// projected as Ready, vault artifacts dropped from the retention graph, or a missing
/// backend service crashing the evidence fabric instead of degrading to a warning.
/// </summary>
public sealed class SecurityMasterAndVaultEvidenceContributorTests
{
    private static readonly EvidenceSubjectDto ConflictQueueSubject = new(
        "open", "security-master-conflict", "Conflict queue", "data", null, "evidence");

    // ── Unregistered-dependency guards across contributors ────────────────────

    public static TheoryData<string, Func<IServiceProvider, IEvidenceContributor>, EvidenceSubjectDto> ContributorsWithLazyDependencies => new()
    {
        {
            "strategy-run",
            sp => new StrategyRunEvidenceContributor(sp),
            new EvidenceSubjectDto("run-1", "strategy-run", "l", "w", null, "p")
        },
        {
            "trading-readiness",
            sp => new TradingReadinessEvidenceContributor(sp),
            new EvidenceSubjectDto("paper", "paper-readiness", "l", "w", null, "p")
        },
        {
            "report-pack",
            sp => new ReportPackEvidenceContributor(sp),
            new EvidenceSubjectDto(Guid.NewGuid().ToString("D"), "report-pack", "l", "w", null, "p")
        },
        {
            "report-pack-delivery",
            sp => new ReportPackDeliveryEvidenceContributor(sp),
            new EvidenceSubjectDto(Guid.NewGuid().ToString("D"), "report-pack-delivery", "l", "w", null, "p")
        },
        {
            "provider-trust",
            sp => new ProviderTrustEvidenceContributor(sp),
            new EvidenceSubjectDto("dk1", "provider-trust", "l", "w", null, "p")
        },
        {
            "security-master-conflict",
            sp => new SecurityMasterConflictEvidenceContributor(sp),
            ConflictQueueSubject
        },
        {
            "evidence-vault",
            sp => new EvidenceVaultEvidenceContributor(sp),
            new EvidenceSubjectDto("vault-1", "evidence-vault", "l", "w", null, "p")
        },
        {
            "private-capital-fund-event",
            sp => new PrivateCapitalFundEventEvidenceContributor(sp),
            new EvidenceSubjectDto("fund-event:alpha:1", "private-capital-fund-event", "l", "w", null, "p")
        },
        {
            "payment-intent",
            sp => new PaymentIntentEvidenceContributor(sp),
            new EvidenceSubjectDto("payment:alpha:1", "payment-intent", "l", "w", null, "p")
        }
    };

    [Theory]
    [MemberData(nameof(ContributorsWithLazyDependencies))]
    public async Task ContributeAsync_BackingServiceNotRegistered_DegradesToSingleWarning(
        string contributorId,
        Func<IServiceProvider, IEvidenceContributor> factory,
        EvidenceSubjectDto subject)
    {
        var contributor = factory(new ServiceCollection().BuildServiceProvider());
        contributor.ContributorId.Should().Be(contributorId);
        contributor.Supports(subject).Should().BeTrue();

        var contribution = await contributor.ContributeAsync(Context(subject));

        contribution.Nodes.Should().BeEmpty(
            "a missing backend service must degrade the contribution, not fabricate evidence");
        contribution.Warnings.Should().ContainSingle()
            .Which.Should().Contain("not registered");
    }

    [Fact]
    public async Task ContributeAsync_ScopedStrategyRunWithMatchingRequestScope_ProjectsEvidence()
    {
        var contributor = await CreateStrategyRunContributorAsync(
            tenantParameter: "tenant-alpha",
            companyParameter: "company-alpha",
            requestTenant: "tenant-alpha",
            requestCompany: "company-alpha");
        var subject = StrategyRunSubject();

        var contribution = await contributor.ContributeAsync(Context(subject));

        contribution.Nodes.Should().Contain(
            node => node.Kind == "strategy-run-detail",
            "matching trusted scope should project the run; warnings: {0}",
            string.Join(" | ", contribution.Warnings));
        contribution.RequiredEvidenceIds.Should().NotBeEmpty();
    }

    [Fact]
    public async Task ContributeAsync_ScopedStrategyRunWithForeignRequestScope_ReturnsNotFoundShape()
    {
        var contributor = await CreateStrategyRunContributorAsync(
            tenantParameter: "tenant-alpha",
            companyParameter: "company-alpha",
            requestTenant: "tenant-foreign",
            requestCompany: "company-foreign");
        var subject = StrategyRunSubject();

        var contribution = await contributor.ContributeAsync(Context(subject));

        contribution.Nodes.Should().BeEmpty();
        contribution.Edges.Should().BeEmpty();
        contribution.RequiredEvidenceIds.Should().BeEmpty();
        contribution.Warnings.Should().ContainSingle()
            .Which.Should().Be("Strategy run 'covered-call-run' was not found.");
    }

    [Fact]
    public async Task ContributeAsync_StrategyRunWithIncompleteDeclaredScope_FailsClosed()
    {
        var contributor = await CreateStrategyRunContributorAsync(
            tenantParameter: "tenant-alpha",
            companyParameter: null,
            requestTenant: "tenant-alpha",
            requestCompany: "company-alpha");
        var subject = StrategyRunSubject();

        var contribution = await contributor.ContributeAsync(Context(subject));

        contribution.Nodes.Should().BeEmpty();
        contribution.Warnings.Should().ContainSingle()
            .Which.Should().Be("Strategy run 'covered-call-run' was not found.");
    }

    [Fact]
    public async Task ContributeAsync_LegacyUnscopedStrategyRun_PreservesCompatibilityWithoutRequestScope()
    {
        var contributor = await CreateStrategyRunContributorAsync(
            tenantParameter: null,
            companyParameter: null,
            requestTenant: null,
            requestCompany: null);
        var subject = StrategyRunSubject();

        var contribution = await contributor.ContributeAsync(Context(subject));

        contribution.Nodes.Should().Contain(node => node.Kind == "strategy-run-detail");
    }

    // ── Export contributor ────────────────────────────────────────────────────

    [Fact]
    public async Task ContributeAsync_ExportContributor_ProjectsBuiltInProfilesAsReadyArtifacts()
    {
        var contributor = new ExportEvidenceContributor();
        var subject = new EvidenceSubjectDto("current", "analysis-export", "Exports", "reporting", null, "evidence");
        var profiles = ExportProfile.GetBuiltInProfiles();

        var contribution = await contributor.ContributeAsync(Context(subject));

        var node = contribution.Nodes.Should().ContainSingle().Which;
        node.Kind.Should().Be("analysis-export");
        node.Status.Should().Be(EvidenceStatusDto.Ready);
        node.ArtifactRefs.Should().HaveCount(profiles.Count);
        node.ArtifactRefs.Select(a => a.Route)
            .Should().BeEquivalentTo(profiles.Select(p => $"/api/export/analysis/{Uri.EscapeDataString(p.Id)}"));
        contributor.Supports(subject).Should().BeTrue();
        contributor.Supports(new EvidenceSubjectDto("s", "report-pack", "l", "w", null, "p")).Should().BeTrue();
        contributor.Supports(new EvidenceSubjectDto("s", "strategy-run", "l", "w", null, "p")).Should().BeFalse();
    }

    // ── Provider-trust contributor with the real DK1 readiness service ────────

    [Fact]
    public async Task ContributeAsync_ProviderTrustWithNoParityPacket_ReturnsReviewRequiredWithBlockerWorkItems()
    {
        // A never-created automation root gives the deterministic "no packet" readiness
        // (Dk1TrustGateReadinessService.cs builds the blocker below in that case).
        var automationRoot = Path.Combine(
            Path.GetTempPath(), "meridian-tests", $"dk1-{Guid.NewGuid():N}");
        var services = new ServiceCollection();
        services.AddSingleton(new Dk1TrustGateReadinessService(
            new Dk1TrustGateReadinessOptions(automationRoot),
            NullLogger<Dk1TrustGateReadinessService>.Instance));
        var contributor = new ProviderTrustEvidenceContributor(services.BuildServiceProvider());
        var subject = new EvidenceSubjectDto("dk1", "provider-trust", "DK1", "data", null, "evidence");
        const string expectedBlocker = "No DK1 pilot parity packet is available to the workstation.";

        var contribution = await contributor.ContributeAsync(Context(subject));

        var node = contribution.Nodes.Should().ContainSingle().Which;
        node.Status.Should().Be(EvidenceStatusDto.ReviewRequired,
            "a trust gate without a parity packet must never look Ready");
        node.ArtifactRefs.Should().BeEmpty("there is no packet path to reference");
        node.RelatedWorkItemIds.Should().ContainSingle()
            .Which.Should().Be($"provider-trust:{expectedBlocker}");
        contribution.Warnings.Should().ContainSingle().Which.Should().Be(expectedBlocker);
    }

    // ── Security Master conflict contributor ──────────────────────────────────

    [Fact]
    public async Task ContributeAsync_OpenQueueWithNoConflicts_ReturnsReadyQueueNode()
    {
        var contributor = CreateConflictContributor(openConflicts: Array.Empty<SecurityMasterConflict>());

        var contribution = await contributor.ContributeAsync(Context(ConflictQueueSubject));

        var node = contribution.Nodes.Should().ContainSingle().Which;
        node.Kind.Should().Be("security-master-conflict-queue");
        node.Status.Should().Be(EvidenceStatusDto.Ready);
        node.ArtifactRefs.Should().ContainSingle().Which.Kind.Should().Be("security-master-conflicts-route");
        contribution.RequiredEvidenceIds.Should().ContainSingle().Which.Should().Be(node.EvidenceId);
    }

    [Fact]
    public async Task ContributeAsync_GuidSubject_LooksUpSingleConflict()
    {
        var resolved = MakeConflict("Resolved", DateTimeOffset.UtcNow.AddHours(-2));
        var service = new Mock<ISecurityMasterConflictService>();
        service
            .Setup(s => s.GetConflictAsync(resolved.ConflictId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(resolved);
        var contributor = new SecurityMasterConflictEvidenceContributor(Provider(service.Object));
        var subject = ConflictQueueSubject with { SubjectId = resolved.ConflictId.ToString("D") };

        var contribution = await contributor.ContributeAsync(Context(subject));

        contribution.Nodes.Should().HaveCount(2);
        var queue = contribution.Nodes.Single(n => n.Kind == "security-master-conflict-queue");
        queue.Status.Should().Be(EvidenceStatusDto.Ready);
        queue.Summary.Should().Contain("0 remain open");
        var conflictNode = contribution.Nodes.Single(n => n.Kind == "security-master-conflict");
        conflictNode.Status.Should().Be(EvidenceStatusDto.Ready);
        conflictNode.RelatedWorkItemIds.Should().BeEmpty("resolved conflicts produce no casework items");
        service.Verify(s => s.GetOpenConflictsAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ContributeAsync_MixedConflicts_OrdersByDetectedAtAndMapsStatuses()
    {
        var older = MakeConflict("Open", DateTimeOffset.UtcNow.AddHours(-3));
        var newer = MakeConflict("Resolved", DateTimeOffset.UtcNow.AddHours(-1));
        var contributor = CreateConflictContributor(openConflicts: new[] { newer, older });

        var contribution = await contributor.ContributeAsync(Context(ConflictQueueSubject));

        var queue = contribution.Nodes.Single(n => n.Kind == "security-master-conflict-queue");
        queue.Status.Should().Be(EvidenceStatusDto.ReviewRequired, "one conflict is still open");
        queue.Freshness.AsOf.Should().Be(newer.DetectedAt, "the queue's as-of is the newest detection");
        queue.RelatedWorkItemIds.Should().ContainSingle()
            .Which.Should().Be($"security-master:conflict:{older.ConflictId:N}",
                "only open conflicts produce casework items");

        var conflictNodes = contribution.Nodes.Where(n => n.Kind == "security-master-conflict").ToArray();
        conflictNodes.Select(n => n.Freshness.AsOf)
            .Should().Equal(new DateTimeOffset?[] { older.DetectedAt, newer.DetectedAt });
        conflictNodes[0].Status.Should().Be(EvidenceStatusDto.ReviewRequired);
        conflictNodes[1].Status.Should().Be(EvidenceStatusDto.Ready);
        conflictNodes[0].ArtifactRefs.Should().ContainSingle()
            .Which.Route.Should().Contain(older.ConflictId.ToString("D"));
        contribution.Edges.Should().HaveCount(2)
            .And.OnlyContain(e => e.FromId == queue.EvidenceId && e.Relationship == "contains");
    }

    // ── Evidence vault contributor ────────────────────────────────────────────

    [Fact]
    public async Task ContributeAsync_VaultLookupSubject_ReturnsGuidanceWarningWithoutTouchingStore()
    {
        var store = new Mock<IEvidenceArtifactStore>(MockBehavior.Strict);
        var contributor = new EvidenceVaultEvidenceContributor(
            ProviderWithScope(store.Object, "tenant-alpha", "company-alpha"));
        var subject = new EvidenceSubjectDto("lookup", "evidence-vault", "Vault", "data", null, "evidence");

        var contribution = await contributor.ContributeAsync(Context(subject));

        contribution.Warnings.Should().ContainSingle()
            .Which.Should().Be("Open a specific vault id to inspect retained vault artifacts.");
        store.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ContributeAsync_UnknownVaultId_ReturnsNotFoundWarning()
    {
        var store = new Mock<IEvidenceArtifactStore>();
        store
            .Setup(s => s.TryGetVaultIdentityAsync(
                "vault-404",
                "tenant-alpha",
                "company-alpha",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((EvidenceVaultIdentityDto?)null);
        var contributor = new EvidenceVaultEvidenceContributor(
            ProviderWithScope(store.Object, "tenant-alpha", "company-alpha"));
        var subject = new EvidenceSubjectDto("vault-404", "evidence-vault", "Vault", "data", null, "evidence");

        var contribution = await contributor.ContributeAsync(Context(subject));

        contribution.Nodes.Should().BeEmpty();
        contribution.Warnings.Should().ContainSingle()
            .Which.Should().Be("No accessible Evidence Vault record was found.");
    }

    [Fact]
    public async Task ContributeAsync_VaultIdentity_ProjectsManifestAndOrderedArtifactNodes()
    {
        var retainedAt = DateTimeOffset.UtcNow.AddDays(-1);
        var identity = new EvidenceVaultIdentityDto(
            VaultId: "vault-1",
            SubjectKind: "report-pack",
            SubjectId: "pack-9",
            ManifestPath: "vault/vault-1/manifest.json",
            ManifestRoute: "/api/workstation/evidence/vault/vault-1",
            RetainedAt: retainedAt,
            ContentHashSha256: "hash-manifest",
            SchemaVersion: 1,
            StorageKind: "file")
        {
            TenantId = "tenant-alpha",
            Scope = "company-alpha",
            Artifacts =
            [
                MakeVaultArtifact("b", retainedAt),
                MakeVaultArtifact("A", retainedAt)
            ]
        };
        var store = new Mock<IEvidenceArtifactStore>();
        store
            .Setup(s => s.TryGetVaultIdentityAsync(
                "vault-1",
                "tenant-alpha",
                "company-alpha",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(identity);
        var contributor = new EvidenceVaultEvidenceContributor(
            ProviderWithScope(store.Object, "tenant-alpha", "company-alpha"));
        var subject = new EvidenceSubjectDto("vault-1", "evidence-vault", "Vault", "data", null, "evidence");

        var contribution = await contributor.ContributeAsync(Context(subject));

        contribution.Nodes.Should().HaveCount(3);
        var manifest = contribution.Nodes.Single(n => n.Kind == "evidence-vault-manifest");
        manifest.Status.Should().Be(EvidenceStatusDto.Ready);
        var manifestArtifact = manifest.ArtifactRefs.Should().ContainSingle().Which;
        manifestArtifact.Hash.Should().Be("hash-manifest");
        manifestArtifact.CanonicalSubjectKind.Should().Be("report-pack");

        var artifactNodes = contribution.Nodes.Where(n => n.Kind == "retained-vault-artifact").ToArray();
        artifactNodes.Should().HaveCount(2);
        artifactNodes[0].Summary.Should().Contain("artifact A",
            "vault artifacts are ordered case-insensitively by artifact id");
        artifactNodes[1].Summary.Should().Contain("artifact b");
        contribution.Edges.Should().HaveCount(2)
            .And.OnlyContain(e => e.FromId == manifest.EvidenceId && e.Relationship == "retains");
        contribution.RequiredEvidenceIds.Should().HaveCount(3,
            "the manifest and every retained artifact are required evidence");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static EvidenceContributionContext Context(EvidenceSubjectDto subject) =>
        new(subject, new CancellationTokenSource(TimeSpan.FromSeconds(5)).Token);

    private static EvidenceSubjectDto StrategyRunSubject() =>
        new(
            "covered-call-run",
            EvidenceSubjectResolver.StrategyRunKind,
            "Covered call run",
            "strategy",
            null,
            "evidence");

    private static async Task<StrategyRunEvidenceContributor> CreateStrategyRunContributorAsync(
        string? tenantParameter,
        string? companyParameter,
        string? requestTenant,
        string? requestCompany)
    {
        var parameters = new Dictionary<string, string>(StringComparer.Ordinal);
        if (tenantParameter is not null)
        {
            parameters["workstationTenantId"] = tenantParameter;
        }

        if (companyParameter is not null)
        {
            parameters["workstationCompanyId"] = companyParameter;
        }

        var store = new StrategyRunStore();
        await store.RecordRunAsync(StrategyRunEntry.Start(
            "covered-call-strategy",
            "Covered Call",
            RunType.Backtest,
            "covered-call-run",
            parameterSet: parameters));
        var services = new ServiceCollection();
        services.AddSingleton(new StrategyRunReadService(
            store,
            new PortfolioReadService(),
            new LedgerReadService()));
        if (requestTenant is not null || requestCompany is not null)
        {
            AddRequestScope(services, requestTenant, requestCompany);
        }

        return new StrategyRunEvidenceContributor(services.BuildServiceProvider());
    }

    private static IServiceProvider Provider<T>(T service) where T : class
    {
        var services = new ServiceCollection();
        services.AddSingleton(service);
        return services.BuildServiceProvider();
    }

    private static IServiceProvider ProviderWithScope<T>(
        T service,
        string? tenantId,
        string? companyId)
        where T : class
    {
        var services = new ServiceCollection();
        services.AddSingleton(service);
        AddRequestScope(services, tenantId, companyId);
        return services.BuildServiceProvider();
    }

    private static void AddRequestScope(
        IServiceCollection services,
        string? tenantId,
        string? companyId)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Items[LoginSessionMiddleware.CurrentUserKey] = "evidence-controller";
        if (tenantId is not null)
        {
            httpContext.Items[LoginSessionMiddleware.CurrentTenantIdKey] = tenantId;
        }

        if (companyId is not null)
        {
            httpContext.Items[LoginSessionMiddleware.CurrentUserCompanyIdKey] = companyId;
        }

        var httpContextAccessor = new HttpContextAccessor { HttpContext = httpContext };
        services.AddSingleton<IHttpContextAccessor>(httpContextAccessor);
        services.AddSingleton<IWorkstationTenantContextAccessor>(
            new FixedWorkstationTenantContextAccessor(new WorkstationTenantContext(
                tenantId,
                companyId,
                "evidence-controller",
                null,
                UserPermission.None)));
    }

    private sealed class FixedWorkstationTenantContextAccessor(WorkstationTenantContext context)
        : IWorkstationTenantContextAccessor
    {
        public bool TryGetCurrent(out WorkstationTenantContext current)
        {
            current = context;
            return !string.IsNullOrWhiteSpace(current.Actor) || current.HasTenantScope;
        }

        public WorkstationTenantContext GetRequired() => context;
    }

    private static SecurityMasterConflictEvidenceContributor CreateConflictContributor(
        IReadOnlyList<SecurityMasterConflict> openConflicts)
    {
        var service = new Mock<ISecurityMasterConflictService>();
        service
            .Setup(s => s.GetOpenConflictsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(openConflicts);
        return new SecurityMasterConflictEvidenceContributor(Provider(service.Object));
    }

    private static SecurityMasterConflict MakeConflict(string status, DateTimeOffset detectedAt) => new(
        ConflictId: Guid.NewGuid(),
        SecurityId: Guid.NewGuid(),
        ConflictKind: "IdentifierMismatch",
        FieldPath: "identifiers.cusip",
        ProviderA: "bloomberg",
        ValueA: "037833100",
        ProviderB: "refinitiv",
        ValueB: "037833109",
        DetectedAt: detectedAt,
        Status: status);

    private static EvidenceVaultArtifactDto MakeVaultArtifact(string artifactId, DateTimeOffset retainedAt) => new(
        ArtifactId: artifactId,
        Kind: "retained-json",
        RelativePath: $"vault/vault-1/{artifactId}.json",
        ContentHashSha256: $"hash-{artifactId}",
        SizeBytes: 128,
        RetainedAt: retainedAt,
        SourcePath: null,
        SourceRoute: null,
        CanonicalSubjectKind: "report-pack",
        CanonicalSubjectId: "pack-9");
}

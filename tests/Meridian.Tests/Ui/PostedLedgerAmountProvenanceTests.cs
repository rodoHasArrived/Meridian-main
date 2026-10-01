using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Tenancy;
using Meridian.Contracts.Workstation;
using Meridian.Identity.Auth;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using Meridian.Ui.Shared.Endpoints;
using Meridian.Ui.Shared.Evidence;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Meridian.Tests.Ui;

public sealed class PostedLedgerAmountProvenanceTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task RetainedIntakeAndReview_AmountPacketContainsOnlyItsSupportingEvidence()
    {
        using var fixture = new Fixture();
        var selected = await fixture.RetainAsync(fixture.SubjectId, "selected-source");
        var sibling = await fixture.RetainAsync(fixture.CreditSubjectId, "unrelated-credit-source");
        fixture.SetReferences(selected, sibling);
        await using var app = await fixture.CreateAppAsync();

        var response = await app.GetTestClient().GetAsync(fixture.PacketRoute);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var packet = await response.Content.ReadFromJsonAsync<EvidencePacketDto>(Json);
        packet!.LedgerAmount!.Status.Should().Be(EvidenceStatusDto.Ready);
        packet.LedgerAmount.Amount.Should().Be(125m);
        packet.LedgerAmount.Scope.Should().Be(fixture.Scope);
        packet.LedgerAmount.Evidence.Select(item => item.EvidenceId).Should().Contain("selected-source")
            .And.NotContain("unrelated-credit-source").And.NotContain("unrelated-case");
        packet.Nodes.Select(node => node.EvidenceId).Should().NotContain("unrelated-case");
        var proofRoute = packet.LedgerAmount.Evidence.Single(item => item.EvidenceId == "selected-source").Route;
        var manifest = await app.GetTestClient().GetStringAsync(proofRoute);
        manifest.Should().Contain("selected-source").And.NotContain("unrelated-credit-source");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SameNameAndSameSymbolAcrossFunds_CannotBecomeAmountProof(bool foreignTenant)
    {
        using var fixture = new Fixture();
        var selected = await fixture.RetainAsync(fixture.SubjectId, "selected-source");
        var foreignScope = fixture.Scope with { FundProfileId = "fund-beta", LedgerBookId = Guid.NewGuid(), PeriodId = Guid.NewGuid() };
        if (foreignTenant)
            foreignScope = foreignScope with { TenantId = "tenant-beta", CompanyId = "company-beta" };
        var foreign = await fixture.RetainAsync(fixture.SubjectId, "foreign-source", foreignScope);
        fixture.SetReferences(selected, foreign);

        var packet = await fixture.Service.GetPacketAsync(fixture.SubjectId, fixture.Scope);

        packet!.LedgerAmount!.Status.Should().Be(EvidenceStatusDto.Blocked);
        packet.LedgerAmount.Evidence.Select(item => item.EvidenceId).Should().NotContain("foreign-source");
        // Both posted lines deliberately share the same Cash/XYZ display identity.
        packet.LedgerAmount.Evidence.Should().Contain(item => item.EvidenceId == "selected-source");
        packet.Nodes.Should().OnlyContain(node => !node.Summary.Contains("foreign-source", StringComparison.Ordinal));
        (await fixture.Service.GetPacketAsync(fixture.SubjectId, foreignScope)).Should().BeNull();
    }

    [Fact]
    public async Task MissingSourceEvidence_NeverMakesRootLedgerRecordReadyProof()
    {
        using var fixture = new Fixture();
        fixture.SetReferences();

        var packet = await fixture.Service.GetPacketAsync(fixture.SubjectId, fixture.Scope);

        packet!.LedgerAmount!.Status.Should().Be(EvidenceStatusDto.ReviewRequired);
        packet.LedgerAmount.Evidence.Should().ContainSingle(item => item.Kind == "ledger-record");
        packet.Completeness.Status.Should().Be(EvidenceStatusDto.ReviewRequired);
        packet.ProofChain.Status.Should().Be(EvidenceStatusDto.ReviewRequired);
    }

    [Theory]
    [InlineData("missing-manifest")]
    [InlineData("missing-source")]
    [InlineData("changed-source")]
    [InlineData("expired")]
    [InlineData("stale-retention")]
    [InlineData("unreviewed")]
    [InlineData("unscoped")]
    public async Task UnverifiableSource_IsWithheldAndRequiresReview(string condition)
    {
        using var fixture = new Fixture();
        var reference = await fixture.RetainAsync(fixture.SubjectId, "selected-source", expiresAt:
            condition == "expired" ? DateTimeOffset.UtcNow.AddDays(-1) : null);
        if (condition == "stale-retention")
            reference = reference with { RetainedAtUtc = reference.RetainedAtUtc.AddMinutes(-1) };
        if (condition == "unreviewed")
            reference = reference with { ReviewStatus = "NeedsReview" };
        if (condition == "unscoped")
            reference = reference with { SubjectId = null, SubjectType = null };
        fixture.SetReferences(reference);
        if (condition == "missing-manifest")
            File.Delete(fixture.LastManifestPath!);
        if (condition == "missing-source")
            File.Delete(fixture.LastSourcePath!);
        if (condition == "changed-source")
            await File.WriteAllTextAsync(fixture.LastSourcePath!, "tampered");

        var packet = await fixture.Service.GetPacketAsync(fixture.SubjectId, fixture.Scope);

        packet!.LedgerAmount!.Status.Should().Be(EvidenceStatusDto.ReviewRequired);
        packet.LedgerAmount.Evidence.Should().ContainSingle(item => item.Kind == "ledger-record");
        packet.Nodes.SelectMany(node => node.ArtifactRefs).Should().BeEmpty();
    }

    [Theory]
    [InlineData("duplicate-reference")]
    [InlineData("changed-digest")]
    [InlineData("foreign-vault-subject")]
    [InlineData("duplicate-journal")]
    public async Task AmbiguousOrForeignEvidence_IsBlockedWithoutAProofRoute(string condition)
    {
        using var fixture = new Fixture();
        var reference = await fixture.RetainAsync(fixture.SubjectId, "selected-source",
            condition == "foreign-vault-subject" ? fixture.Scope with { PeriodId = Guid.NewGuid() } : null);
        if (condition == "foreign-vault-subject")
            reference = reference with { SubjectId = PostedLedgerAmountProvenanceService.BuildRetainedSubjectId(fixture.SubjectId, fixture.Scope) };
        if (condition == "changed-digest")
            reference = reference with { ContentHash = new string('a', 64) };
        fixture.SetReferences(condition == "duplicate-reference" ? [reference, reference] : [reference]);
        if (condition == "duplicate-journal")
            fixture.Records.Add(fixture.Records[0]);

        var packet = await fixture.Service.GetPacketAsync(fixture.SubjectId, fixture.Scope);

        packet!.LedgerAmount!.Status.Should().Be(EvidenceStatusDto.Blocked);
        packet.Nodes.SelectMany(node => node.ArtifactRefs).Should().BeEmpty();
    }

    [Theory]
    [InlineData("fund")]
    [InlineData("book")]
    [InlineData("period")]
    [InlineData("tenant")]
    [InlineData("company")]
    public async Task ExactAccountingScope_IsRequired(string dimension)
    {
        using var fixture = new Fixture();
        fixture.SetReferences(await fixture.RetainAsync(fixture.SubjectId, "selected-source"));
        var scope = dimension switch
        {
            "fund" => fixture.Scope with { FundProfileId = "fund-beta" },
            "book" => fixture.Scope with { LedgerBookId = Guid.NewGuid() },
            "period" => fixture.Scope with { PeriodId = Guid.NewGuid() },
            "tenant" => fixture.Scope with { TenantId = "tenant-beta" },
            _ => fixture.Scope with { CompanyId = "company-beta" }
        };

        (await fixture.Service.GetPacketAsync(fixture.SubjectId, scope)).Should().BeNull();
    }

    [Fact]
    public async Task EndpointRejectsMissingDuplicateAndForeignScope_AndIgnoresSpoofedTenant()
    {
        using var fixture = new Fixture();
        fixture.SetReferences(await fixture.RetainAsync(fixture.SubjectId, "selected-source"));
        await using var app = await fixture.CreateAppAsync();
        var client = app.GetTestClient();

        (await client.GetAsync(fixture.PacketRoute.Split('?')[0])).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.GetAsync(fixture.PacketRoute + "&periodId=" + fixture.Scope.PeriodId)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.GetAsync(fixture.PacketRoute.Replace("fund-alpha", "fund-beta", StringComparison.Ordinal))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var spoof = await client.GetFromJsonAsync<EvidencePacketDto>(fixture.PacketRoute + "&tenantId=foreign&companyId=foreign", Json);
        spoof!.LedgerAmount!.Scope.TenantId.Should().Be("tenant-alpha");
        spoof.LedgerAmount.Scope.CompanyId.Should().Be("company-alpha");
        (await client.GetAsync(fixture.PacketRoute.Replace(":debit", ":credit", StringComparison.Ordinal))).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task LedgerReadPermissionOpensAmountAndRetainedManifest_ReportingOnlyCannotReadAmount()
    {
        using var fixture = new Fixture();
        fixture.SetReferences(await fixture.RetainAsync(fixture.SubjectId, "selected-source"));
        await using var app = await fixture.CreateAppAsync(UserPermission.ViewLedgerReports);
        var packet = await app.GetTestClient().GetFromJsonAsync<EvidencePacketDto>(fixture.PacketRoute, Json);
        var source = packet!.LedgerAmount!.Evidence.Single(item => item.EvidenceId == "selected-source");
        (await app.GetTestClient().GetAsync(source.Route)).StatusCode.Should().Be(HttpStatusCode.OK);
        await using var reportingApp = await fixture.CreateAppAsync(UserPermission.ViewReporting);
        (await reportingApp.GetTestClient().GetAsync(fixture.PacketRoute)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await reportingApp.GetTestClient().GetAsync(source.Route)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("changed-source")]
    [InlineData("deleted-source")]
    [InlineData("changed-review")]
    [InlineData("foreign-fund")]
    [InlineData("changed-digest")]
    public async Task ManifestOpenRevalidatesSelectedProof_AfterPacketLoad(string change)
    {
        using var fixture = new Fixture();
        fixture.SetReferences(await fixture.RetainAsync(fixture.SubjectId, "selected-source"));
        await using var app = await fixture.CreateAppAsync();
        var client = app.GetTestClient();
        var packet = await client.GetFromJsonAsync<EvidencePacketDto>(fixture.PacketRoute, Json);
        packet!.LedgerAmount!.Status.Should().Be(EvidenceStatusDto.Ready);
        var source = packet.LedgerAmount.Evidence.Single(item => item.EvidenceId == "selected-source");
        var route = source.Route!;
        if (change == "changed-source")
            await File.WriteAllTextAsync(fixture.LastSourcePath!, "changed source");
        if (change == "deleted-source")
            File.Delete(fixture.LastSourcePath!);
        if (change == "changed-review")
            await fixture.ReviewLastAgainAsync();
        if (change == "foreign-fund")
            route = route.Replace("fund-alpha", "fund-beta", StringComparison.Ordinal);
        if (change == "changed-digest")
            route = route.Replace(source.ContentHash!, new string('a', 64), StringComparison.Ordinal);

        var response = await client.GetAsync(route);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).Should().Contain("ledger-amount-proof-stale");
    }

    [Fact]
    public async Task GuardedManifestRequiresCompleteUnambiguousQuery()
    {
        using var fixture = new Fixture();
        fixture.SetReferences(await fixture.RetainAsync(fixture.SubjectId, "selected-source"));
        await using var app = await fixture.CreateAppAsync();
        var client = app.GetTestClient();
        var packet = await client.GetFromJsonAsync<EvidencePacketDto>(fixture.PacketRoute, Json);
        var route = packet!.LedgerAmount!.Evidence.Single(item => item.EvidenceId == "selected-source").Route!;
        var incomplete = route[..route.IndexOf("&expectedContentHash=", StringComparison.Ordinal)];

        (await client.GetAsync(incomplete)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.GetAsync(route + "&periodId=" + fixture.Scope.PeriodId)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "ledger-amount-proof-" + Guid.NewGuid().ToString("N"));
        private readonly Guid _journalId = Guid.NewGuid();
        private readonly Guid _debitId = Guid.NewGuid();
        private readonly Guid _creditId = Guid.NewGuid();
        private readonly Guid _aggregateId = Guid.NewGuid();
        private readonly Mock<ILedgerJournalStore> _journals = new();
        private readonly Mock<ILedgerBookService> _books = new();
        private readonly Mock<IFundProfileTenancyRegistry> _tenancy = new();
        public LedgerAmountScopeDto Scope { get; } = new("tenant-alpha", "company-alpha", "fund-alpha", Guid.NewGuid(), Guid.NewGuid());
        public List<LedgerJournalEntryRecord> Records { get; } = [];
        public string SubjectId => $"{_journalId:D}:{_debitId:D}:debit";
        public string CreditSubjectId => $"{_journalId:D}:{_creditId:D}:credit";
        public string PacketRoute => $"/api/workstation/evidence/subjects/ledger-amount/{SubjectId}/packet?fundProfileId={Scope.FundProfileId}&ledgerBookId={Scope.LedgerBookId:D}&periodId={Scope.PeriodId:D}";
        public FileEvidenceArtifactStore Artifacts { get; }
        public PostedLedgerAmountProvenanceService Service { get; }
        public string? LastManifestPath { get; private set; }
        public string? LastSourcePath { get; private set; }
        private string? _lastVaultId;
        private string? _lastDocumentId;

        public Fixture()
        {
            Directory.CreateDirectory(_root);
            Artifacts = new FileEvidenceArtifactStore(_root, NullLogger<FileEvidenceArtifactStore>.Instance);
            var now = DateTimeOffset.UtcNow;
            _tenancy.Setup(store => store.ResolveAsync(Scope.FundProfileId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new FundProfileOwnership(Scope.FundProfileId, Scope.TenantId, Scope.CompanyId));
            _books.Setup(store => store.GetBookAsync(Scope.LedgerBookId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new LedgerBookDto(Scope.LedgerBookId, Scope.FundProfileId, _aggregateId,
                    FundStructureNodeKindDto.Fund, "Same fund name", "USD", now, now));
            _journals.Setup(store => store.GetPeriodAsync(Scope.PeriodId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new LedgerAccountingPeriod(Scope.PeriodId, Scope.LedgerBookId, 2026, 10, "October",
                    new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), "Open", now, null, 1));
            _journals.Setup(store => store.QueryAsync(It.Is<LedgerJournalEntryQuery>(query =>
                    query.LedgerBookId == Scope.LedgerBookId && query.PeriodId == Scope.PeriodId), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => Records);
            Service = new PostedLedgerAmountProvenanceService(_journals.Object, _books.Object, _tenancy.Object, Artifacts);
            SetReferences();
        }

        public async Task<JournalEvidenceReference> RetainAsync(string subjectId, string evidenceId,
            LedgerAmountScopeDto? scope = null, DateTimeOffset? expiresAt = null)
        {
            scope ??= Scope;
            var retainedId = PostedLedgerAmountProvenanceService.BuildRetainedSubjectId(subjectId, scope);
            var intake = await Artifacts.WriteIntakeArtifactAsync(new EvidenceVaultIntakeRequestDto(
                "ledger-amount", retainedId, "upload", evidenceId + ".txt",
                Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(evidenceId)), "text/plain", "bank",
                Lifecycle: new EvidenceLifecycleMetadataDto(null, false, expiresAt, []))
            {
                TenantId = scope.TenantId,
                Scope = scope.CompanyId,
                Actor = "preparer"
            });
            var review = await Artifacts.ReviewDocumentAsync(intake.VaultIdentity.VaultId, intake.Document!.DocumentId,
                scope.TenantId, scope.CompanyId, new EvidenceVaultDocumentReviewRequestDto(
                    EvidenceDocumentReviewStatusDto.Accepted, "reviewer", ExtractionStatus: EvidenceExtractionStatusDto.Accepted)
                {
                    ConfirmedFields = [new EvidenceDocumentConfirmedFieldDto("amount", "125", "reviewer", DateTimeOffset.UtcNow)]
                });
            review.Should().NotBeNull();
            var identity = await Artifacts.TryGetVaultIdentityAsync(intake.VaultIdentity.VaultId, scope.TenantId, scope.CompanyId);
            LastManifestPath = Path.Combine(_root, identity!.ManifestPath);
            LastSourcePath = Path.Combine(_root, identity.Artifacts[0].RelativePath);
            _lastVaultId = identity.VaultId;
            _lastDocumentId = intake.Document.DocumentId;
            return new JournalEvidenceReference(evidenceId, "vault:" + identity.VaultId, "source-document", "bank",
                identity.RetainedAt, "preparer", retainedId, identity.ContentHashSha256, evidenceId,
                ReviewStatus: "Accepted", ReviewedBy: "reviewer", ReviewedAtUtc: review!.AuditEvent.RecordedAt,
                SubjectType: "ledger-amount");
        }

        public Task<EvidenceVaultDocumentReviewResponseDto?> ReviewLastAgainAsync()
            => Artifacts.ReviewDocumentAsync(_lastVaultId!, _lastDocumentId!, Scope.TenantId, Scope.CompanyId,
                new EvidenceVaultDocumentReviewRequestDto(EvidenceDocumentReviewStatusDto.NeedsReview, "other-reviewer"));

        public void SetReferences(params JournalEvidenceReference[] references)
        {
            var now = DateTimeOffset.UtcNow;
            var account = new LedgerAccount("Cash", LedgerAccountType.Asset, "XYZ");
            var entry = new JournalEntry(_journalId, now, "Same name and symbol",
                [new LedgerEntry(_debitId, _journalId, now, account, 125m, 0m, "Same name and symbol"),
                 new LedgerEntry(_creditId, _journalId, now, account, 0m, 125m, "Same name and symbol")],
                new JournalEntryMetadata(EvidenceReferences: references));
            Records.Clear();
            Records.Add(new LedgerJournalEntryRecord(entry, _aggregateId, Scope.PeriodId, null, null, 1, now));
        }

        public async Task<WebApplication> CreateAppAsync(UserPermission? permissions = null)
        {
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();
            builder.Services.AddSingleton(_journals.Object);
            builder.Services.AddSingleton(_books.Object);
            builder.Services.AddSingleton(_tenancy.Object);
            builder.Services.AddSingleton<IEvidenceArtifactStore>(Artifacts);
            builder.Services.AddSingleton<IEvidenceContributor>(new UnrelatedContributor());
            builder.Services.AddEvidenceWorkflowFabric();
            var app = builder.Build();
            app.Use(async (context, next) =>
            {
                context.Items[LoginSessionMiddleware.CurrentUserKey] = "controller";
                context.Items[LoginSessionMiddleware.CurrentUserRoleKey] = UserRole.Controller;
                if (permissions.HasValue)
                    context.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] = permissions.Value;
                context.Items[LoginSessionMiddleware.CurrentTenantIdKey] = Scope.TenantId;
                context.Items[LoginSessionMiddleware.CurrentUserCompanyIdKey] = Scope.CompanyId;
                await next();
            });
            app.MapEvidenceEndpoints(Json);
            await app.StartAsync();
            return app;
        }

        public void Dispose() => Directory.Delete(_root, recursive: true);
    }

    private sealed class UnrelatedContributor : IEvidenceContributor
    {
        public string ContributorId => "unrelated-cases";
        public bool Supports(EvidenceSubjectDto subject) => true;
        public Task<EvidenceContribution> ContributeAsync(EvidenceContributionContext context)
            => Task.FromResult(new EvidenceContribution(
                [new EvidenceNodeDto("unrelated-case", context.Subject, "reconciliation-case", EvidenceStatusDto.Ready,
                    new EvidenceFreshnessDto(null, false, null), "casework", "Unrelated case", [], [])], [], [], [], []));
    }
}

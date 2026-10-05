using System.Collections.Immutable;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using FluentAssertions;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Workstation;
using Meridian.Identity.Auth;
using Meridian.Reporting;
using Meridian.Ui.Shared.Endpoints;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using Xunit;

namespace Meridian.Tests.Ui;

public sealed class ReportingIncomeComparisonServiceTests
{
    private const string Tenant = "tenant-income";
    private const string Company = "company-income";
    private const string BaselineRun = "published-income";
    private const string CurrentRun = "restated-income";
    private const string GridId = "investment-income";
    private const string Metric = "income";
    private static readonly DateOnly PeriodEnd = new(2026, 5, 31);
    private static readonly DateTimeOffset Now = new(2026, 6, 5, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Book = Guid.Parse("0b3a97ce-4d75-4756-9bbe-038ac16548ef");
    private static readonly ReportAccessQueryContext Owner = new(
        "owner", CompanyId: Company, TenantId: Tenant, RequireBoundScope: true);
    private static readonly ReportingIncomeComparisonRequestDto Request = new(BaselineRun, CurrentRun, GridId, Metric);
    private static readonly JsonSerializerOptions HttpJson = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    [Fact]
    public async Task Endpoints_CandidatesCreateReadAndSupportReturnJsonWithRetainedContext()
    {
        var fixture = new Fixture();
        await using var app = await CreateAppAsync(fixture);
        using var client = app.GetTestClient();
        const string route = "/api/fund-structure/reporting/comparisons";

        var candidatesResponse = await client.GetAsync($"{route}/candidates");
        candidatesResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        candidatesResponse.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
        var candidates = await candidatesResponse.Content.ReadFromJsonAsync<ReportingIncomeComparisonRunDto[]>(HttpJson);
        candidates.Should().HaveCount(2);
        candidates!.Select(candidate => candidate.RunId).Should().BeEquivalentTo([BaselineRun, CurrentRun]);

        var createResponse = await client.PostAsJsonAsync($"{route}/", Request, HttpJson);
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createResponse.Content.ReadFromJsonAsync<ReportingIncomeComparisonDto>(HttpJson);
        created.Should().NotBeNull();
        created!.Movement.Should().Be(-125m);
        created.Status.Should().Be("Reconciled");

        var readResponse = await client.GetAsync($"{route}/{created.ComparisonId}");
        readResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var read = await readResponse.Content.ReadFromJsonAsync<ReportingIncomeComparisonDto>(HttpJson);
        read.Should().BeEquivalentTo(created);

        var contribution = created.Contributions.Single();
        var supportResponse = await client.GetAsync($"{route}/{created.ComparisonId}/contributions/{contribution.ContributionId}");
        supportResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var support = await supportResponse.Content.ReadFromJsonAsync<ReportingIncomeContributionSupportDto>(HttpJson);
        support.Should().NotBeNull();
        support!.ComparisonId.Should().Be(created.ComparisonId);
        support.BaselineRunId.Should().Be(BaselineRun);
        support.CurrentRunId.Should().Be(CurrentRun);
        support.CurrentRecords.Should().ContainSingle(row => row["journalEntryId"] == "late-accrual-journal");
    }

    [Fact]
    public async Task Endpoints_UnreadyDeploymentRejectsCandidateReadAndComparisonCreationBeforeStorage()
    {
        var fixture = new Fixture();
        await using var app = await CreateAppAsync(fixture, deploymentReady: false);
        using var client = app.GetTestClient();

        var candidates = await client.GetAsync("/api/fund-structure/reporting/comparisons/candidates");
        var create = await client.PostAsJsonAsync("/api/fund-structure/reporting/comparisons/", Request, HttpJson);

        candidates.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        create.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await candidates.Content.ReadAsStringAsync()).Should().Contain("authoritative reporting deployment");
        (await create.Content.ReadAsStringAsync()).Should().Contain("authoritative reporting deployment");
        fixture.RequestedOffsets.Should().BeEmpty();
        fixture.Artifacts.StoreCalls.Should().Be(0);
        fixture.Artifacts.ReadIdentities.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateAndReload_AfterSourceChangesAndRunsDisappear_RetainsExactJournalAndComparisonContext()
    {
        var fixture = new Fixture();
        var created = await fixture.Service().CreateAsync(Request, Owner);
        var contribution = created.Contributions.Should().ContainSingle(c => c.Kind == "Journal").Subject;
        Sha256Digest.IsCanonical(created.ComparisonId).Should().BeTrue();
        created.Status.Should().Be("Reconciled");
        created.Movement.Should().Be(-125m);

        fixture.LateAccrual["netAmount"] = "-9999";
        fixture.LateAccrual["journalEntryId"] = "replaced-live-journal";
        fixture.Manifests.Clear();
        var reloadedService = fixture.Service();
        var reloaded = await reloadedService.GetAsync(created.ComparisonId, Owner);
        var support = await reloadedService.GetSupportAsync(created.ComparisonId, contribution.ContributionId, Owner);

        reloaded.Should().BeEquivalentTo(created);
        support.ComparisonId.Should().Be(created.ComparisonId);
        support.BaselineRunId.Should().Be(BaselineRun);
        support.CurrentRunId.Should().Be(CurrentRun);
        support.Contribution.Should().BeEquivalentTo(contribution);
        support.Contribution.RecordId.Should().Be("late-accrual-journal");
        support.Contribution.SourceRunId.Should().Be(CurrentRun);
        support.BaselineRecords.Should().BeEmpty();
        var line = support.CurrentRecords.Should().ContainSingle().Subject;
        line["entryId"].Should().Be("late-accrual-line");
        line["journalEntryId"].Should().Be("late-accrual-journal");
        line["netAmount"].Should().Be("-125");
        line["recordedAtUtc"].Should().Be("2026-06-03T09:00:00.0000000+00:00");
        fixture.Artifacts.StoreCalls.Should().Be(1, "reload must use retained bytes without creating a replacement explanation");
    }

    [Fact]
    public async Task GetAndSupport_CrossTenantCannotUseAValidComparisonHash()
    {
        var fixture = new Fixture();
        var created = await fixture.Service().CreateAsync(Request, Owner);
        var foreign = Owner with { TenantId = "other-tenant" };
        var get = () => fixture.Service().GetAsync(created.ComparisonId, foreign);
        var support = () => fixture.Service().GetSupportAsync(created.ComparisonId, created.Contributions[0].ContributionId, foreign);

        await get.Should().ThrowAsync<ReportingArtifactNotFoundException>();
        await support.Should().ThrowAsync<ReportingArtifactNotFoundException>();
        fixture.Artifacts.ReadIdentities.TakeLast(2).Should().OnlyContain(identity =>
            identity.TenantId == foreign.TenantId && identity.ContentHashSha256 == created.ComparisonId);
    }

    [Theory]
    [InlineData(BaselineRun)]
    [InlineData(CurrentRun)]
    public async Task EveryReadAndCreate_RequiresAccessToBothRetainedInputs(string privateRun)
    {
        var fixture = new Fixture();
        fixture.Manifests[privateRun] = fixture.Manifests[privateRun] with
        {
            ImmutableAccessScope = Access(ReportingGovernanceAccessMode.Private)
        };
        var service = fixture.Service();
        var created = await service.CreateAsync(Request, Owner);
        var outsider = Owner with { ActorPrincipalId = "another-user" };
        var create = () => service.CreateAsync(Request, outsider);
        var get = () => service.GetAsync(created.ComparisonId, outsider);
        var support = () => service.GetSupportAsync(created.ComparisonId, created.Contributions[0].ContributionId, outsider);

        await create.Should().ThrowAsync<UnauthorizedAccessException>();
        await get.Should().ThrowAsync<UnauthorizedAccessException>();
        await support.Should().ThrowAsync<UnauthorizedAccessException>();
        var candidates = await service.ListCandidatesAsync(outsider);
        candidates.Should().NotContain(candidate => candidate.RunId == privateRun);
        fixture.Artifacts.StoreCalls.Should().Be(1, "denied comparisons must never publish artifacts");
    }

    [Fact]
    public async Task GetAndSupport_ReevaluateCurrentGroupClaimsAgainstRetainedAccess()
    {
        var fixture = new Fixture();
        fixture.Manifests[BaselineRun] = fixture.Manifests[BaselineRun] with
        {
            ImmutableAccessScope = Access(ReportingGovernanceAccessMode.Restricted) with
            {
                AllowOwnerAccess = false,
                Principals = [new(ReportingAccessPrincipalKind.Group, "income-reviewers")]
            }
        };
        var reviewer = Owner with { ActorPrincipalId = "reviewer", GroupPrincipalIds = ["income-reviewers"] };
        var created = await fixture.Service().CreateAsync(Request, reviewer);
        var revoked = reviewer with { GroupPrincipalIds = [] };

        var get = () => fixture.Service().GetAsync(created.ComparisonId, revoked);
        var support = () => fixture.Service().GetSupportAsync(created.ComparisonId, created.Contributions[0].ContributionId, revoked);

        await get.Should().ThrowAsync<UnauthorizedAccessException>();
        await support.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Theory]
    [InlineData("baselineManifest", "tenantId")]
    [InlineData("currentManifest", "tenantId")]
    [InlineData("baselineManifest", "companyId")]
    [InlineData("currentManifest", "companyId")]
    public async Task GetAndSupport_RejectCrossScopeManifestInsideTenantArtifact(string manifest, string scope)
    {
        var fixture = new Fixture();
        var created = await fixture.Service().CreateAsync(Request, Owner);
        var content = await fixture.EnvelopeAsync(created.ComparisonId);
        content[manifest]!["operationalScope"]![scope] = "foreign-scope";
        var identity = await fixture.RetainAsync(content.ToJsonString());
        var get = () => fixture.Service().GetAsync(identity, Owner);
        var support = () => fixture.Service().GetSupportAsync(identity, created.Contributions[0].ContributionId, Owner);

        await get.Should().ThrowAsync<UnauthorizedAccessException>();
        await support.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Theory]
    [InlineData("tenant")]
    [InlineData("size")]
    [InlineData("alternate-comparison")]
    public async Task Create_RejectsWriteReceiptThatDoesNotIdentifyItsExactBytes(string fault)
    {
        var fixture = new Fixture();
        var first = await fixture.Service().CreateAsync(Request, Owner);
        var originalBytes = await fixture.Artifacts.ReadAsync(new(Tenant, first.ComparisonId));
        fixture.Manifests[CurrentRun] = Manifest(CurrentRun, [IncomeLine("coupon-journal", "coupon-line", -1000m),
            IncomeLine("different-accrual", "different-line", -250m)]);
        fixture.Artifacts.RewriteReceipt = receipt => fault switch
        {
            "tenant" => receipt with { Identity = receipt.Identity with { TenantId = "other-tenant" } },
            "size" => receipt with { ByteSize = receipt.ByteSize + 1 },
            _ => new(new(Tenant, first.ComparisonId), originalBytes.ByteSize, Now, true)
        };

        var create = () => fixture.Service().CreateAsync(Request, Owner);

        await create.Should().ThrowAsync<InvalidDataException>().WithMessage("*receipt*");
    }

    [Theory]
    [InlineData("tenant")]
    [InlineData("hash")]
    [InlineData("size")]
    [InlineData("content")]
    public async Task Get_RejectsCorruptedArtifactReadResult(string fault)
    {
        var fixture = new Fixture();
        var created = await fixture.Service().CreateAsync(Request, Owner);
        fixture.Artifacts.RewriteRead = result => fault switch
        {
            "tenant" => result with { Identity = result.Identity with { TenantId = "other-tenant" } },
            "hash" => result with { Identity = result.Identity with { ContentHashSha256 = Sha256Digest.ComputeUtf8("other") } },
            "size" => result with { ByteSize = result.ByteSize + 1 },
            _ => result with { Content = [.. result.Content, 0] }
        };

        var read = () => fixture.Service().GetAsync(created.ComparisonId, Owner);

        await read.Should().ThrowAsync<InvalidDataException>().WithMessage("*integrity*");
    }

    [Theory]
    [InlineData("invalid-json")]
    [InlineData("unrelated-artifact")]
    [InlineData("comparison")]
    [InlineData("baselineManifest")]
    [InlineData("currentManifest")]
    [InlineData("support")]
    [InlineData("baseline-description")]
    [InlineData("current-description")]
    [InlineData("null-support-entry")]
    [InlineData("null-contribution")]
    [InlineData("wrong-support-context")]
    public async Task Get_MalformedOrUnrelatedArtifactIsSafelyRejected(string fault)
    {
        var fixture = new Fixture();
        var created = await fixture.Service().CreateAsync(Request, Owner);
        var content = await fixture.EnvelopeAsync(created.ComparisonId);
        switch (fault)
        {
            case "baseline-description":
                content["comparison"]!["baseline"] = null;
                break;
            case "current-description":
                content["comparison"]!["current"] = null;
                break;
            case "null-support-entry":
                content["support"]![0] = null;
                break;
            case "null-contribution":
                content["support"]![0]!["contribution"] = null;
                break;
            case "wrong-support-context":
                content["support"]![0]!["baselineRunId"] = "unrelated-run";
                break;
            default:
                content[fault] = null;
                break;
        }
        var bytes = fault switch
        {
            "invalid-json" => "not-json",
            "unrelated-artifact" => "{\"document\":\"other retained artifact\"}",
            _ => content.ToJsonString()
        };
        var identity = await fixture.RetainAsync(bytes);

        var read = () => fixture.Service().GetAsync(identity, Owner);

        await read.Should().ThrowAsync<KeyNotFoundException>().WithMessage("*retained income comparison*");
    }

    [Fact]
    public async Task ListCandidates_KeepsOlderBaselineAvailableBeyondFirstTwoHundredRuns()
    {
        var fixture = new Fixture();
        var baseline = fixture.Manifests[BaselineRun];
        fixture.Manifests.Clear();
        for (var index = 0; index < 201; index++)
        {
            var id = $"restated-{index}";
            fixture.Manifests.Add(id, baseline with { RunId = id });
        }
        fixture.Manifests.Add(BaselineRun, baseline);
        fixture.GovernedRuns.Add(BaselineRun, Published(baseline, revision: 1));
        fixture.GovernedRuns.Add("restated-0", Published(fixture.Manifests["restated-0"], revision: 2, restatementOf: BaselineRun));

        var candidates = await fixture.Service().ListCandidatesAsync(Owner);

        candidates.Should().HaveCount(202);
        candidates.Should().ContainSingle(candidate => candidate.RunId == BaselineRun);
        candidates.Single(candidate => candidate.RunId == BaselineRun).PublicationLabel.Should().Be("Originally published");
        candidates.Single(candidate => candidate.RunId == "restated-0").PublicationLabel.Should().Be("Restated published");
        fixture.RequestedOffsets.Should().Equal(0, 200);
        fixture.GovernanceTransactions.Should().Be(2);
        fixture.GovernanceReadBatches.Select(batch => batch.RunIds.Length).Should().Equal(200, 2);
        fixture.GovernanceReadBatches.Should().OnlyContain(batch => batch.TenantId == Tenant);
        fixture.GovernanceSingleReads.Should().Be(0);
    }

    [Fact]
    public async Task ListCandidates_BatchesOnlyAuthorizedRenderedRunsAndSkipsEmptyPages()
    {
        var fixture = new Fixture();
        fixture.Manifests[BaselineRun] = fixture.Manifests[BaselineRun] with
        {
            ImmutableAccessScope = Access(ReportingGovernanceAccessMode.Private)
        };
        fixture.Manifests.Add("without-retained-grid", fixture.Manifests[CurrentRun] with
        {
            RunId = "without-retained-grid", RenderedReportWriterGrids = []
        });
        var outsider = Owner with { ActorPrincipalId = "another-user" };

        var candidates = await fixture.Service().ListCandidatesAsync(outsider);

        candidates.Should().ContainSingle(candidate => candidate.RunId == CurrentRun);
        fixture.GovernanceTransactions.Should().Be(1);
        fixture.GovernanceReadBatches.Should().ContainSingle().Subject.RunIds.Should().Equal(CurrentRun);
        fixture.GovernanceSingleReads.Should().Be(0);

        fixture.Manifests.Remove(CurrentRun);
        (await fixture.Service().ListCandidatesAsync(outsider)).Should().BeEmpty();
        fixture.GovernanceTransactions.Should().Be(1, "a page without visible retained grids needs no governance transaction");
    }

    [Fact]
    public async Task Create_ReadsBothExplicitPublicationStatesInOneBoundedTransaction()
    {
        var fixture = new Fixture();
        fixture.GovernedRuns.Add(BaselineRun, Published(fixture.Manifests[BaselineRun], revision: 1));
        fixture.GovernedRuns.Add(CurrentRun, Published(fixture.Manifests[CurrentRun], revision: 2, restatementOf: BaselineRun));

        var comparison = await fixture.Service().CreateAsync(Request, Owner);

        comparison.Baseline.PublicationLabel.Should().Be("Originally published");
        comparison.Current.PublicationLabel.Should().Be("Restated published");
        fixture.GovernanceTransactions.Should().Be(1);
        fixture.GovernanceReadBatches.Should().ContainSingle().Subject.RunIds.Should().Equal(BaselineRun, CurrentRun);
        fixture.GovernanceSingleReads.Should().Be(0);
    }

    [Fact]
    public async Task CandidatesAndRetainedComparison_DistinguishGovernedOriginalAndRestatedPublication()
    {
        var fixture = new Fixture();
        fixture.GovernedRuns.Add(BaselineRun, Published(fixture.Manifests[BaselineRun], revision: 1));
        fixture.GovernedRuns.Add(CurrentRun, Published(fixture.Manifests[CurrentRun], revision: 2, restatementOf: BaselineRun));
        fixture.Manifests.Add("ungoverned-attempt", fixture.Manifests[CurrentRun] with { RunId = "ungoverned-attempt" });
        var service = fixture.Service();

        var candidates = await service.ListCandidatesAsync(Owner);
        var original = candidates.Single(candidate => candidate.RunId == BaselineRun);
        var restated = candidates.Single(candidate => candidate.RunId == CurrentRun);
        original.PublicationLabel.Should().Be("Originally published");
        original.Revision.Should().Be(1);
        original.RestatementOfRunId.Should().BeNull();
        restated.PublicationLabel.Should().Be("Restated published");
        restated.Revision.Should().Be(2);
        restated.RestatementOfRunId.Should().Be(BaselineRun);
        candidates.Single(candidate => candidate.RunId == "ungoverned-attempt").PublicationLabel.Should().Be("Publication not verified",
            "an operational Released label without governed evidence proves neither publication nor draft status");

        var created = await service.CreateAsync(Request, Owner);
        fixture.GovernedRuns.Clear();
        var replayed = await fixture.Service().GetAsync(created.ComparisonId, Owner);

        replayed.Baseline.Should().BeEquivalentTo(original);
        replayed.Current.Should().BeEquivalentTo(restated);
    }

    private static GovernedReportingRun Published(ReportingOutputManifest manifest, int revision, string? restatementOf = null)
    {
        var scope = manifest.OperationalScope!;
        var authority = new ReportingAuthorityScope("owner", scope.TenantId, scope.OrganizationId, scope.CompanyId,
            [ReportingGovernancePermission.ReleaseRun], ReportingCommandOrigin.HumanOperator, "publication-correlation");
        var snapshot = new ReportingCertifiedSnapshotScope(scope.TenantId, scope.OrganizationId, scope.CompanyId,
            scope.FundId, scope.BookId, scope.PeriodId, $"snapshot-{manifest.RunId}",
            Sha256Digest.ComputeUtf8($"snapshot-{manifest.RunId}"), "reconciled-may", Now);
        var release = new ReportingReleaseReceipt(authority, Now, $"manifest-{manifest.RunId}",
            Sha256Digest.ComputeUtf8($"manifest-{manifest.RunId}"),
            [new("income.pdf", Sha256Digest.ComputeUtf8("income.pdf"), 10)], ["publication-evidence"]);
        return new(manifest.RunId, "income-series", revision, manifest.TemplateId, "1", scope,
            manifest.ImmutableAccessScope!, snapshot, authority, Now, restatementOf,
            GovernedReportingExecutionState.Succeeded, GovernedReportingState.Released, 4,
            Readiness: null, Approval: new(authority, Now, "Approved for publication"), Release: release, AuditTrail: []);
    }

    private static ReportingAccessScope Access(ReportingGovernanceAccessMode mode = ReportingGovernanceAccessMode.CompanyWide) =>
        new("income-policy", "1", mode, "owner", true, [], Sha256Digest.ComputeUtf8("income-policy"));

    private static async Task<WebApplication> CreateAppAsync(Fixture fixture, bool deploymentReady = true)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(fixture.Service());
        var deployment = Substitute.For<IReportingDeploymentReadinessService>();
        deployment.Evaluate().Returns(new ReportingDeploymentCapabilityDto(
            IsReady: deploymentReady, DurableGovernance: deploymentReady, DurableArtifacts: deploymentReady,
            DurableReconciliationEvidence: deploymentReady, DurableRuns: deploymentReady, DurableScheduling: deploymentReady,
            DurableDelivery: deploymentReady, RecipientDestinationsConfigured: deploymentReady,
            ClientDocumentsConfigured: deploymentReady, MigrationsManaged: deploymentReady, Components: [],
            BlockingReasons: deploymentReady ? [] : ["Retained report authority is unavailable."]));
        builder.Services.AddSingleton(deployment);
        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            context.TraceIdentifier = "income-comparison-correlation";
            context.Items[LoginSessionMiddleware.CurrentUserKey] = Owner.ActorPrincipalId;
            context.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] = UserPermission.ManageReporting;
            context.Items[LoginSessionMiddleware.CurrentUserRoleKey] = UserRole.ReportingAnalyst;
            context.Items[LoginSessionMiddleware.CurrentTenantIdKey] = Tenant;
            context.Items[LoginSessionMiddleware.CurrentUserCompanyIdKey] = Company;
            await next();
        });
        app.MapReportingIncomeComparisonEndpoints(HttpJson);
        await app.StartAsync();
        return app;
    }

    private static ReportingOutputManifest Manifest(string id, IReadOnlyList<IReadOnlyDictionary<string, string>> rows)
    {
        var grids = ReportWriterGridEngine.RenderGrids([
            new(GridId, "Investment income", ReportWriterGridKindDto.Pivot,
                RowFields: ["entityId"], Metrics: [new(Metric, "netAmount")])
        ], rows);
        return new(id, "investment-income", PeriodEnd, ReportingRunStatus.Released, [], [], 1,
            ReportingRunTrigger.AdHoc, RenderedReportWriterGrids: grids.ToImmutableArray(),
            ResolvedParameters: new(new("fund-income"), "period-may", PeriodEnd, new(Book),
                ReportingAccountingBasisDto.Gaap, "USD", ReportingConsolidationLevelDto.Fund,
                ReportingOutputFormatDto.Pdf, ReportingFinalityDto.Final,
                IncludeSupportingSchedules: true, IncludeEvidenceAppendix: true),
            OperationalScope: new(Tenant, "organization-income", Company, "fund-income", Book.ToString("D"), "period-may"),
            ImmutableAccessScope: Access(), CertifiedDatasetRows: rows.ToImmutableArray());
    }

    private static Dictionary<string, string> IncomeLine(string journal, string entry, decimal amount) => new(StringComparer.Ordinal)
    {
        ["journalEntryId"] = journal,
        ["entryId"] = entry,
        ["fundId"] = "fund-income",
        ["entityId"] = "entity-income",
        ["account"] = "Coupon Income",
        ["accountType"] = "Revenue",
        ["netAmount"] = amount.ToString("G29", CultureInfo.InvariantCulture),
        ["debit"] = "0",
        ["credit"] = (-amount).ToString("G29", CultureInfo.InvariantCulture),
        ["currency"] = "USD",
        ["accountingBasis"] = "Gaap",
        ["periodId"] = "period-may",
        ["timestampUtc"] = "2026-05-31T23:59:00.0000000+00:00",
        ["recordedAtUtc"] = "2026-06-03T09:00:00.0000000+00:00"
    };

    private sealed class Fixture
    {
        private readonly IReportingRunStore _runs = Substitute.For<IReportingRunStore>();
        private readonly StubGovernanceRepository _governance = new();
        public Dictionary<string, ReportingOutputManifest> Manifests { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, GovernedReportingRun> GovernedRuns => _governance.Runs;
        public Dictionary<string, string> LateAccrual { get; } = IncomeLine("late-accrual-journal", "late-accrual-line", -125m);
        public MemoryArtifactStore Artifacts { get; } = new();
        public List<int> RequestedOffsets { get; } = [];
        public int GovernanceTransactions => _governance.TransactionCount;
        public int GovernanceSingleReads => _governance.SingleReadCount;
        public List<(string TenantId, string[] RunIds)> GovernanceReadBatches => _governance.ReadBatches;

        public Fixture()
        {
            Manifests.Add(BaselineRun, Manifest(BaselineRun, [IncomeLine("coupon-journal", "coupon-line", -1000m)]));
            Manifests.Add(CurrentRun, Manifest(CurrentRun, [IncomeLine("coupon-journal", "coupon-line", -1000m), LateAccrual]));
            _runs.GetManifest(Arg.Any<string>(), Arg.Any<string>()).Returns(call =>
                Manifests.TryGetValue(call.ArgAt<string>(1), out var manifest)
                    && manifest.OperationalScope!.TenantId == call.ArgAt<string>(0) ? manifest : null);
            _runs.ListRuns(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<int>()).Returns(call =>
            {
                var offset = call.ArgAt<int>(2);
                RequestedOffsets.Add(offset);
                return Manifests.Values.Where(manifest => manifest.OperationalScope!.TenantId == call.ArgAt<string>(0)
                    && manifest.OperationalScope.CompanyId == call.ArgAt<string?>(1))
                    .Skip(offset).Take(call.ArgAt<int>(3)).Select(manifest => new ReportingRunSnapshot(manifest, [], Now)).ToArray();
            });
        }

        public ReportingIncomeComparisonService Service() => new(_runs, Artifacts, _governance);

        public async Task<JsonNode> EnvelopeAsync(string id) =>
            JsonNode.Parse((await Artifacts.ReadAsync(new(Tenant, id))).Content)!;

        public async Task<string> RetainAsync(string content) =>
            (await Artifacts.StoreAsync(new(Tenant, Encoding.UTF8.GetBytes(content)))).Identity.ContentHashSha256;
    }

    private sealed class StubGovernanceRepository : IReportingGovernanceRepository
    {
        private readonly IReportingGovernanceTransaction _transaction = Substitute.For<IReportingGovernanceTransaction>();
        public Dictionary<string, GovernedReportingRun> Runs { get; } = new(StringComparer.Ordinal);
        public int TransactionCount { get; private set; }
        public int SingleReadCount { get; private set; }
        public List<(string TenantId, string[] RunIds)> ReadBatches { get; } = [];

        public StubGovernanceRepository()
        {
            _transaction.GetRunAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(call =>
            {
                SingleReadCount++;
                return ValueTask.FromResult(Runs.TryGetValue(call.ArgAt<string>(1), out var run)
                    && run.Scope.TenantId == call.ArgAt<string>(0) ? run : null);
            });
            _transaction.GetRunsAsync(Arg.Any<string>(), Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>()).Returns(call =>
            {
                var tenantId = call.ArgAt<string>(0);
                var ids = call.ArgAt<IReadOnlyCollection<string>>(1).ToArray();
                ids.Length.Should().BeLessThanOrEqualTo(IReportingGovernanceTransaction.MaximumRunReadBatchSize);
                ReadBatches.Add((tenantId, ids));
                return ValueTask.FromResult<IReadOnlyList<GovernedReportingRun>>(Runs.Values
                    .Where(run => run.Scope.TenantId == tenantId && ids.Contains(run.RunId, StringComparer.Ordinal)).ToArray());
            });
        }

        public ValueTask<TResult> ExecuteTransactionAsync<TResult>(
            Func<IReportingGovernanceTransaction, CancellationToken, ValueTask<TResult>> operation,
            CancellationToken cancellationToken = default)
        {
            TransactionCount++;
            return operation(_transaction, cancellationToken);
        }
    }

    private sealed class MemoryArtifactStore : IReportingArtifactStore
    {
        private readonly Dictionary<ReportingArtifactIdentity, byte[]> _content = [];
        public int StoreCalls { get; private set; }
        public List<ReportingArtifactIdentity> ReadIdentities { get; } = [];
        public Func<ReportingArtifactWriteResult, ReportingArtifactWriteResult>? RewriteReceipt { get; set; }
        public Func<ReportingArtifactReadResult, ReportingArtifactReadResult>? RewriteRead { get; set; }

        public Task<ReportingArtifactWriteResult> StoreAsync(ReportingArtifactWriteRequest request, CancellationToken ct = default)
        {
            StoreCalls++;
            var bytes = request.Content.ToArray();
            var identity = new ReportingArtifactIdentity(request.TenantId, Sha256Digest.Compute(bytes));
            var existed = !_content.TryAdd(identity, bytes);
            var receipt = new ReportingArtifactWriteResult(identity, bytes.LongLength, Now, existed);
            return Task.FromResult(RewriteReceipt?.Invoke(receipt) ?? receipt);
        }

        public Task<ReportingArtifactReadResult> ReadAsync(ReportingArtifactIdentity identity, CancellationToken ct = default)
        {
            ReadIdentities.Add(identity);
            if (!_content.TryGetValue(identity, out var bytes))
                throw new ReportingArtifactNotFoundException(identity);
            var result = new ReportingArtifactReadResult(identity, bytes.LongLength, Now, bytes.ToArray());
            return Task.FromResult(RewriteRead?.Invoke(result) ?? result);
        }
    }
}

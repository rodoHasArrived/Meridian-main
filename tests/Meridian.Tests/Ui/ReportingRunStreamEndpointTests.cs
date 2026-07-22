using System.Net;
using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Meridian.Contracts.Workstation;
using Meridian.Identity.Auth;
using Meridian.Reporting;
using Meridian.Ui.Shared.Endpoints;
using Meridian.Ui.Shared.Streaming;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Meridian.Tests.Ui;

public sealed class ReportingRunStreamEndpointTests
{
    private static readonly JsonSerializerOptions ServerJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly DateTimeOffset FixedNow = new(2026, 5, 1, 12, 0, 0, TimeSpan.Zero);
    private const string SeededRunId = "job-stream-20260501";
    private const string SeededTenantId = "tenant-a";
    private const string SeededCompanyId = "company-a";
    private const string SeededOrganizationId = "organization-a";
    private const string SeededFundId = "fund-a";
    private const string SeededBookId = "book-a";
    private const string SeededPeriodId = "2026-05";
    private static readonly DateOnly SeededAsOfDate = new(2026, 5, 1);
    private const string SeededCheckpointId = "ledger-checkpoint-stream-a";
    private static readonly string SeededCheckpointHash = new('a', 64);
    private static readonly string SeededSnapshotHash = new('b', 64);
    private static readonly string SeededReconciliationHash = new('c', 64);
    private static readonly string SeededReadinessEvidenceHash = new('d', 64);
    // Governed reporting (a67c21f9) requires a fully certified contract before rendering, so the
    // canonical parameters JSON and its SHA-256 must agree. This hash is sha256(SeededParametersJson).
    private const string SeededParametersJson =
        "{\"template\":\"investor-monthly-statement\",\"asOf\":\"2026-05-01\",\"basis\":\"Gaap\"}";
    private const string SeededParametersHash =
        "66ad5f72177c2b494a7b8afdf001acdda1126fc80cf6fdcb1d1aab274520e431";
    private static string SeededSourceEvidenceId =>
        $"reporting-source-checkpoint:{SeededCheckpointId}:{SeededCheckpointHash}";

    [Fact]
    public async Task WithoutReadPermission_Returns403()
    {
        await using var app = await CreateStreamAppAsync(grantPermission: false);
        var client = app.GetTestClient();

        var response = await client.GetAsync($"/api/fund-structure/reporting/runs/{SeededRunId}/stream");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task WithoutTenantScope_Returns403()
    {
        await using var app = await CreateStreamAppAsync(includeTenantScope: false);
        var client = app.GetTestClient();

        var response = await client.GetAsync($"/api/fund-structure/reporting/runs/{SeededRunId}/stream");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task CrossTenantManifest_Returns403()
    {
        await using var app = await CreateStreamAppAsync(
            requestTenantId: "tenant-b",
            requestCompanyId: "company-b");
        var client = app.GetTestClient();

        var response = await client.GetAsync($"/api/fund-structure/reporting/runs/{SeededRunId}/stream");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task CrossTenantManifest_AdminOverrideStillReturns403()
    {
        await using var app = await CreateStreamAppAsync(
            grantAdminOverride: true,
            requestTenantId: "tenant-b",
            requestCompanyId: "company-b");
        var client = app.GetTestClient();

        var response = await client.GetAsync($"/api/fund-structure/reporting/runs/{SeededRunId}/stream");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task UnknownRun_Returns404()
    {
        await using var app = await CreateStreamAppAsync();
        var client = app.GetTestClient();

        var response = await client.GetAsync("/api/fund-structure/reporting/runs/no-such-run/stream");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task WithoutBroadcaster_Returns503()
    {
        await using var app = await CreateStreamAppAsync(registerBroadcaster: false);
        var client = app.GetTestClient();

        var response = await client.GetAsync($"/api/fund-structure/reporting/runs/{SeededRunId}/stream");

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task EmitsReportRunEventForSeededRun()
    {
        await using var app = await CreateStreamAppAsync();
        var client = app.GetTestClient();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        using var response = await client.GetAsync(
            $"/api/fund-structure/reporting/runs/{SeededRunId}/stream",
            HttpCompletionOption.ResponseHeadersRead,
            cts.Token);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/event-stream");

        var frame = await ReadFirstEventFrameAsync(response, cts.Token);
        frame.Should().StartWith("event: report-run");
        frame.Should().Contain(SeededRunId);
    }

    [Fact]
    public async Task BeyondSessionCap_Returns429()
    {
        await using var app = await CreateStreamAppAsync(maxConcurrentStreams: 1);
        var client = app.GetTestClient();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        try
        {
            // Hold the first stream open (its subscription keeps the session's single slot reserved).
            using var first = await client.GetAsync(
                $"/api/fund-structure/reporting/runs/{SeededRunId}/stream",
                HttpCompletionOption.ResponseHeadersRead,
                cts.Token);
            first.StatusCode.Should().Be(HttpStatusCode.OK);
            await using var firstStream = await first.Content.ReadAsStreamAsync(cts.Token);
            await ReadPastFirstFrameAsync(firstStream, cts.Token);

            using var second = await client.GetAsync(
                $"/api/fund-structure/reporting/runs/{SeededRunId}/stream",
                HttpCompletionOption.ResponseHeadersRead,
                cts.Token);

            second.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
            second.Headers.RetryAfter.Should().NotBeNull();
        }
        finally
        {
            await cts.CancelAsync();
        }
    }

    private static async Task<string> ReadFirstEventFrameAsync(HttpResponseMessage response, CancellationToken ct)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        var buffer = new byte[4096];
        var builder = new StringBuilder();
        while (!ct.IsCancellationRequested)
        {
            var read = await stream.ReadAsync(buffer, ct);
            if (read <= 0)
            {
                break;
            }

            builder.Append(Encoding.UTF8.GetString(buffer, 0, read));
            var text = builder.ToString();
            var frameEnd = text.IndexOf("\n\n", StringComparison.Ordinal);
            if (frameEnd >= 0)
            {
                return text[..frameEnd];
            }
        }

        throw new TimeoutException("No SSE frame arrived before the read deadline.");
    }

    private static async Task ReadPastFirstFrameAsync(Stream stream, CancellationToken ct)
    {
        var buffer = new byte[1024];
        while (!ct.IsCancellationRequested)
        {
            var read = await stream.ReadAsync(buffer, ct);
            if (read <= 0)
            {
                break;
            }

            if (Encoding.UTF8.GetString(buffer, 0, read).Contains("\n\n", StringComparison.Ordinal))
            {
                return;
            }
        }
    }

    // Builds the fully certified ReportingJobContract governed reporting now requires: scope, source,
    // and snapshot agree on tenant/org/company/fund/book/period; snapshot binds the source checkpoint;
    // the parameters JSON matches its SHA-256; and readiness carries an evidence-backed Ready check.
    private static ReportingJobContract BuildCertifiedStreamContract()
    {
        var resolvedTemplate = new VersionedReportTemplateIdDto("investor-monthly-statement", 1);
        var parameters = new ReportingRunParametersDto(
            new ReportingRunScopeDto(SeededFundId),
            SeededPeriodId,
            SeededAsOfDate,
            new ReportingLedgerBookSelectionDto(Guid.Parse("11111111-1111-1111-1111-111111111111")),
            ReportingAccountingBasisDto.Gaap,
            "USD",
            ReportingConsolidationLevelDto.Fund,
            ReportingOutputFormatDto.Pdf,
            ReportingFinalityDto.Draft,
            IncludeSupportingSchedules: true,
            IncludeEvidenceAppendix: true);
        var source = new ReportingAuthoritativeSourceCheckpoint(
            "durable-ledger-journal",
            $"ledger:{SeededBookId}:{SeededPeriodId}",
            SeededTenantId,
            SeededOrganizationId,
            SeededCompanyId,
            SeededFundId,
            SeededBookId,
            SeededPeriodId,
            "Gaap",
            SeededAsOfDate,
            FixedNow,
            0L,
            0,
            0,
            SeededCheckpointId,
            SeededCheckpointHash,
            FixedNow,
            ImmutableArray.Create(SeededSourceEvidenceId));
        var snapshot = new ReportingCertifiedSnapshotScope(
            SeededTenantId,
            SeededOrganizationId,
            SeededCompanyId,
            SeededFundId,
            SeededBookId,
            SeededPeriodId,
            "snapshot-stream-a",
            SeededSnapshotHash,
            "reconciliation-stream-a",
            FixedNow,
            SourceCheckpointId: SeededCheckpointId,
            SourceCheckpointHash: SeededCheckpointHash,
            ReconciliationCheckpointHash: SeededReconciliationHash,
            ParametersCanonicalJson: SeededParametersJson,
            ParametersHash: SeededParametersHash);
        var readiness = new ReportingRunReadinessDto(
            "readiness-stream-a",
            FixedNow,
            resolvedTemplate,
            parameters,
            ReportingRunReadinessStatusDto.Ready,
            CanGenerateDraft: true,
            CanGenerateFinal: true,
            Checks:
            [
                new ReportingRunReadinessCheckDto(
                    "ledger-source",
                    "Ledger source",
                    ReportingRunReadinessStatusDto.Ready,
                    "Certified ledger source is ready.",
                    0,
                    BlocksDraft: false,
                    BlocksFinal: false,
                    EvidenceReferences: [SeededSourceEvidenceId])
            ],
            BlockingReasons: [],
            EvidenceHash: SeededReadinessEvidenceHash);
        return new ReportingJobContract(
            "job-stream",
            "investor-monthly-statement",
            SeededAsOfDate,
            ReportingRunTrigger.AdHoc,
            0,
            "op",
            FixedNow,
            ResolvedTemplate: resolvedTemplate,
            ResolvedParameters: parameters,
            Readiness: readiness,
            OperationalScope: new ReportingOperationalScope(
                SeededTenantId,
                SeededOrganizationId,
                SeededCompanyId,
                SeededFundId,
                SeededBookId,
                SeededPeriodId),
            ImmutableAccessScope: new ReportingAccessScope(
                "policy-a",
                "1",
                ReportingGovernanceAccessMode.CompanyWide,
                OwnerPrincipalId: null,
                AllowOwnerAccess: false,
                Principals: ImmutableArray<ReportingAccessPrincipalScope>.Empty,
                PolicyHash: "policy-hash-a"),
            CertifiedSnapshot: snapshot,
            AuthoritativeSource: source);
    }

    private static async Task<WebApplication> CreateStreamAppAsync(
        bool grantPermission = true,
        bool registerBroadcaster = true,
        int maxConcurrentStreams = 8,
        bool includeTenantScope = true,
        string requestTenantId = SeededTenantId,
        string requestCompanyId = SeededCompanyId,
        bool grantAdminOverride = false)
    {
        var orchestration = new ReportingOrchestrationService(
            new DefaultReportingTemplateCatalog(), new DeterministicReportingSectionRenderer(), () => FixedNow);
        // Exercise the same complete certified-contract path used in production before testing
        // stream authorization and subscription behavior. A partial fixture would be rejected by
        // ValidateCertifiedContract before the endpoint can expose the seeded run.
        await orchestration.ExecuteAsync(BuildCertifiedStreamContract(), CancellationToken.None);

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });
        builder.WebHost.UseTestServer();

        builder.Services.AddSingleton<IReportingOrchestrationService>(orchestration);
        builder.Services.AddSingleton(new QuoteStreamOptions
        {
            CoalesceIntervalMs = 0,
            MaxConcurrentStreamsPerSession = maxConcurrentStreams,
            SubscriberChannelCapacity = 1
        });
        builder.Services.AddSingleton(sp => new StreamConnectionRegistry(
            sp.GetRequiredService<QuoteStreamOptions>().MaxConcurrentStreamsPerSession));
        if (registerBroadcaster)
        {
            builder.Services.AddSingleton(sp => new ReportRunStreamBroadcaster(
                sp,
                sp.GetRequiredService<StreamConnectionRegistry>(),
                sp.GetRequiredService<QuoteStreamOptions>()));
        }

        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            context.Items[LoginSessionMiddleware.CurrentUserKey] = "reporting-op";
            if (grantPermission)
            {
                context.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] = grantAdminOverride
                    ? UserPermission.ViewReporting | UserPermission.AdminMaintenance
                    : UserPermission.ViewReporting;
            }

            if (includeTenantScope)
            {
                context.Items[LoginSessionMiddleware.CurrentUserCompanyIdKey] = requestCompanyId;
                context.Items[LoginSessionMiddleware.CurrentTenantIdKey] = requestTenantId;
            }

            await next();
        });
        app.MapReportingRunStreamEndpoints(ServerJsonOptions);
        await app.StartAsync();
        return app;
    }

    private sealed class SeededRunStore(ReportingRunSnapshot run) : IReportingRunStore
    {
        public IReadOnlyList<ReportingRunSnapshot> ListRuns(int limit = 25) => [run];

        public ReportingOutputManifest? GetManifest(string runId) =>
            string.Equals(run.Manifest.RunId, runId, StringComparison.Ordinal) ? run.Manifest : null;

        public IReadOnlyList<ReportingRunAuditEntry> GetAudit(string runId) =>
            string.Equals(run.Manifest.RunId, runId, StringComparison.Ordinal) ? run.AuditTrail : [];

        public Task SaveAsync(
            ReportingOutputManifest manifest,
            IReadOnlyList<ReportingRunAuditEntry> auditTrail,
            CancellationToken ct = default) =>
            Task.CompletedTask;
    }
}

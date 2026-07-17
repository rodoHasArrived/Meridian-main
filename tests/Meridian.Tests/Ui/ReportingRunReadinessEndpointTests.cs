using System.Collections.Immutable;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
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

public sealed class ReportingRunReadinessEndpointTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    [Fact]
    public async Task PostRun_MissingExactReconciliationReturnsStructuredConflictInsteadOfUnavailable()
    {
        var catalog = new DefaultReportingTemplateCatalog();
        var dependencyEvaluator = Substitute.For<IReportingRunReadinessDependencyEvaluator>();
        dependencyEvaluator.EvaluateAsync(
                Arg.Any<ReportingRunRequestDto>(),
                Arg.Any<ReportingTemplateMetadata>(),
                Arg.Any<ReportingRunParametersDto>(),
                Arg.Any<ReportAccessQueryContext?>(),
                Arg.Any<CancellationToken>())
            .Returns(Array.Empty<ReportingRunReadinessCheckDto>());
        var readiness = new ReportingRunReadinessService(catalog, dependencyEvaluator: dependencyEvaluator);
        var certification = new ReportingRunCertificationService(
            new EndpointAuthoritativeSource(),
            new ReportingReconciliationEvidenceSource(new EmptyReconciliationStore()));
        var orchestration = Substitute.For<IReportingOrchestrationService>();
        var coordinator = Substitute.For<IReportingGovernanceEndpointCoordinator>();
        var command = new ReportingRunCommandService(
            orchestration,
            catalog,
            readinessService: readiness,
            certificationService: certification,
            governanceCoordinator: coordinator);

        await using var app = await CreateAppAsync(command);
        var request = new ReportingRunRequestDto(
            "investor-monthly-statement",
            Parameters: new ReportingRunParametersDto(
                new ReportingRunScopeDto("fund-a"),
                EndpointAuthoritativeSource.PeriodId.ToString("D"),
                new DateOnly(2026, 6, 30),
                new ReportingLedgerBookSelectionDto(EndpointAuthoritativeSource.BookId),
                ReportingAccountingBasisDto.Gaap,
                "USD",
                ReportingConsolidationLevelDto.Fund,
                ReportingOutputFormatDto.Pdf,
                ReportingFinalityDto.Final,
                IncludeSupportingSchedules: true,
                IncludeEvidenceAppendix: true));

        var response = await app.GetTestClient().PostAsJsonAsync(
            "/api/fund-structure/reporting/runs",
            request,
            JsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var payload = await response.Content.ReadFromJsonAsync<ReportingRunReadinessDto>(JsonOptions);
        payload.Should().NotBeNull();
        payload!.Status.Should().Be(ReportingRunReadinessStatusDto.Blocked);
        payload.Checks.Should().ContainSingle(check =>
            check.CheckId == "exact-reconciliation-evidence"
            && check.Status == ReportingRunReadinessStatusDto.Blocked
            && check.BlocksFinal);
        await orchestration.DidNotReceiveWithAnyArgs().ExecuteAsync(default!, default);
        await coordinator.DidNotReceiveWithAnyArgs()
            .CreateFromCompletedCertifiedManifestAsync(default!, default!, default);
    }

    [Fact]
    public async Task PreviewReadiness_MissingExactReconciliationIsVisibleBeforeRun()
    {
        await using var app = await CreatePreviewAppAsync();
        var request = new ReportingRunRequestDto(
            "investor-monthly-statement",
            Parameters: Parameters());

        var response = await app.GetTestClient().PostAsJsonAsync(
            "/api/fund-structure/reporting/runs/readiness",
            request,
            JsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<ReportingRunReadinessDto>(JsonOptions);
        payload.Should().NotBeNull();
        payload!.CanGenerateFinal.Should().BeFalse();
        payload.Status.Should().Be(ReportingRunReadinessStatusDto.Blocked);
        payload.Checks.Should().ContainSingle(check =>
            check.CheckId == "authoritative-report-source"
            && check.Status == ReportingRunReadinessStatusDto.Ready
            && check.EvidenceReferences.Any(reference =>
                reference.StartsWith("reporting-source-checkpoint:", StringComparison.Ordinal)));
        payload.Checks.Should().ContainSingle(check =>
            check.CheckId == "exact-reconciliation-evidence"
            && check.Status == ReportingRunReadinessStatusDto.Blocked
            && check.BlocksDraft
            && check.BlocksFinal
            && check.Summary.Contains("No retained reconciliation/close checkpoint", StringComparison.Ordinal));
    }

    private static async Task<WebApplication> CreateAppAsync(ReportingRunCommandService command)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(command);
        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            context.TraceIdentifier = "reporting-readiness-correlation";
            context.Items[LoginSessionMiddleware.CurrentUserKey] = "server-operator";
            context.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] = UserPermission.ManageReporting;
            context.Items[LoginSessionMiddleware.CurrentUserRoleKey] = UserRole.ReportingAnalyst;
            context.Items[LoginSessionMiddleware.CurrentTenantIdKey] = "tenant-a";
            context.Items[LoginSessionMiddleware.CurrentUserCompanyIdKey] = "company-a";
            await next();
        });
        app.MapFundStructureEndpoints(JsonOptions);
        await app.StartAsync();
        return app;
    }

    private static async Task<WebApplication> CreatePreviewAppAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<IReportingTemplateCatalog, DefaultReportingTemplateCatalog>();
        builder.Services.AddSingleton<IReportingAuthoritativeSource, EndpointAuthoritativeSource>();
        builder.Services.AddSingleton<IReportingReconciliationEvidenceStore, EmptyReconciliationStore>();
        builder.Services.AddSingleton<IReportingReconciliationEvidenceSource, ReportingReconciliationEvidenceSource>();
        builder.Services.AddSingleton<IReportingRunReadinessDependencyEvaluator, ReportingRunReadinessDependencyEvaluator>();
        builder.Services.AddSingleton(sp => new ReportingRunReadinessService(
            sp.GetRequiredService<IReportingTemplateCatalog>(),
            dependencyEvaluator: sp.GetRequiredService<IReportingRunReadinessDependencyEvaluator>()));
        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            context.TraceIdentifier = "reporting-preview-correlation";
            context.Items[LoginSessionMiddleware.CurrentUserKey] = "server-operator";
            context.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] = UserPermission.ManageReporting;
            context.Items[LoginSessionMiddleware.CurrentUserRoleKey] = UserRole.ReportingAnalyst;
            context.Items[LoginSessionMiddleware.CurrentTenantIdKey] = "tenant-a";
            context.Items[LoginSessionMiddleware.CurrentUserCompanyIdKey] = "company-a";
            await next();
        });
        app.MapFundStructureEndpoints(JsonOptions);
        await app.StartAsync();
        return app;
    }

    private static ReportingRunParametersDto Parameters() => new(
        new ReportingRunScopeDto("fund-a"),
        EndpointAuthoritativeSource.PeriodId.ToString("D"),
        new DateOnly(2026, 6, 30),
        new ReportingLedgerBookSelectionDto(EndpointAuthoritativeSource.BookId),
        ReportingAccountingBasisDto.Gaap,
        "USD",
        ReportingConsolidationLevelDto.Fund,
        ReportingOutputFormatDto.Pdf,
        ReportingFinalityDto.Final,
        IncludeSupportingSchedules: true,
        IncludeEvidenceAppendix: true);

    private sealed class EndpointAuthoritativeSource : IReportingAuthoritativeSource
    {
        public static readonly Guid BookId = Guid.Parse("77777777-7777-7777-7777-777777777777");
        public static readonly Guid PeriodId = Guid.Parse("88888888-8888-8888-8888-888888888888");

        public ValueTask<ReportingAuthoritativeSourceCapture> CaptureAsync(
            ReportingRunParametersDto parameters,
            ReportAccessQueryContext accessContext,
            CancellationToken cancellationToken = default)
        {
            var hash = new string('9', 64);
            var checkpointId = "ledger-checkpoint-99999999999999999999999999999999";
            var rows = ImmutableArray.Create<IReadOnlyDictionary<string, string>>(
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["account"] = "Cash",
                    ["debit"] = "100",
                    ["credit"] = "0",
                    ["netAmount"] = "100"
                });
            var checkpoint = new ReportingAuthoritativeSourceCheckpoint(
                "durable-ledger-journal",
                $"ledger:{BookId:D}:{PeriodId:D}",
                "tenant-a",
                "organization-a",
                "company-a",
                "fund-a",
                BookId.ToString("D"),
                PeriodId.ToString("D"),
                "Gaap",
                parameters.AsOfDate,
                new DateTimeOffset(
                    parameters.AsOfDate.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc),
                    TimeSpan.Zero),
                9,
                1,
                1,
                checkpointId,
                hash,
                new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero),
                [$"reporting-source-checkpoint:{checkpointId}:{hash}"]);
            return ValueTask.FromResult(new ReportingAuthoritativeSourceCapture(checkpoint, rows));
        }
    }

    private sealed class EmptyReconciliationStore : IReportingReconciliationEvidenceStore
    {
        public ValueTask<ReportingReconciliationEvidenceReceipt?> GetExactAsync(
            string tenantId,
            string organizationId,
            string? companyId,
            string fundId,
            string ledgerBookId,
            string accountingPeriodId,
            string accountingBasis,
            DateOnly asOfDate,
            string sourceCheckpointId,
            string sourceCheckpointHash,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<ReportingReconciliationEvidenceReceipt?>(null);
    }
}

using System.Text.Json;
using FluentAssertions;
using Meridian.Application.Backfill;
using Meridian.Application.Scheduling;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;
using Meridian.Identity.Auth;
using Meridian.Storage.SecurityMaster;
using Meridian.Ui.Shared.Endpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace Meridian.Tests.Ui;

public sealed class WorkstationDataSourceExportTests
{
    [Fact]
    public async Task CorporateActionExport_UsesCompleteSourceAndDeclaresReferenceAndCaseNamespaces()
    {
        var proposals = Enumerable.Range(0, 510).Select(_ => Proposal()).ToArray();
        var processingCase = Case(Proposal(), "tenant-a", "company-a");
        var store = new Mock<ICorporateActionOperationsStore>();
        store.Setup(value => value.ReadInboxExportSourceAsync("tenant-a", "company-a", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CorporateActionInboxExportSource(proposals, [processingCase]));
        using var services = new ServiceCollection().AddSingleton(store.Object).BuildServiceProvider();
        var result = await WorkstationEndpoints.ExportCorporateActionInboxAsync(new(["event", "value", "case"]),
            Context(services, UserPermission.ViewCorporateActions), new(JsonSerializerDefaults.Web));
        var exported = result.Should().BeOfType<JsonHttpResult<WorkstationTableExportDto>>().Which.Value!;
        exported.RowCount.Should().Be(511, "the 250/500 browse windows are not export limits");
        exported.Csv.Should().Contain("global source reference").And.Contain("tenant/company case").And.Contain("Context · Currency");
        exported.Csv.Should().NotContain("private-recording-actor").And.NotContain("private-provider-event");
    }

    [Fact]
    public async Task CorporateActionExport_MatchesDefaultQueueOrderAndMissingSnapshotLabels()
    {
        var first = Proposal() with { DisplayMetadata = new("ALPHA", "public-source", ["public-source"], []) };
        var second = Proposal() with { DisplayMetadata = new("ZETA", "public-source", ["public-source"], []) };
        var legacyCase = Case(Proposal(), "tenant-a", "company-a") with { SourceSnapshot = null };
        var store = new Mock<ICorporateActionOperationsStore>();
        store.Setup(value => value.ReadInboxExportSourceAsync("tenant-a", "company-a", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CorporateActionInboxExportSource([second, first], [legacyCase]));
        using var services = new ServiceCollection().AddSingleton(store.Object).BuildServiceProvider();
        var result = await WorkstationEndpoints.ExportCorporateActionInboxAsync(new(["event"]),
            Context(services, UserPermission.ViewCorporateActions), new(JsonSerializerDefaults.Web));
        var exported = result.Should().BeOfType<JsonHttpResult<WorkstationTableExportDto>>().Which.Value!;
        exported.Csv.Split("\r\n").Skip(1).Select(line => line.Split(',')[0])
            .Should().Equal("Dividend ALPHA", "Dividend ZETA", "Not supplied Not supplied");
    }

    [Fact]
    public async Task CorporateActionExport_RefusesForeignCasesAndMissingAuthority()
    {
        var store = new Mock<ICorporateActionOperationsStore>();
        store.Setup(value => value.ReadInboxExportSourceAsync("tenant-a", "company-a", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CorporateActionInboxExportSource([], [Case(Proposal(), "tenant-b", "company-b")]));
        using var services = new ServiceCollection().AddSingleton(store.Object).BuildServiceProvider();
        var request = new WorkstationTableExportRequest(["event"]);
        var denied = await WorkstationEndpoints.ExportCorporateActionInboxAsync(request,
            Context(services, UserPermission.ViewDiagnostics), new(JsonSerializerDefaults.Web));
        denied.Should().BeAssignableTo<IStatusCodeHttpResult>().Which.StatusCode.Should().Be(403);
        store.Verify(value => value.ReadInboxExportSourceAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        var foreign = await WorkstationEndpoints.ExportCorporateActionInboxAsync(request,
            Context(services, UserPermission.ViewCorporateActions), new(JsonSerializerDefaults.Web));
        foreign.Should().BeAssignableTo<IStatusCodeHttpResult>().Which.StatusCode.Should().Be(503);
    }

    [Fact]
    public void RemediationExport_UsesEveryRetainedExecutionBeforeFiltering()
    {
        var history = new BackfillExecutionHistory();
        for (var index = 0; index < 150; index++)
            history.AddExecution(new BackfillExecutionLog
            {
                ExecutionId = $"retained-{index}",
                Symbols = [index % 2 == 0 ? "AAPL" : "MSFT"],
                Status = ExecutionStatus.Completed,
                ScheduledAt = DateTimeOffset.UtcNow.AddMinutes(-index),
                FromDate = new(2026, 11, 1),
                ToDate = new(2026, 11, 2),
                AutoRemediationSla = new(BackfillRemediationSlaTier.Standard, DateTimeOffset.UtcNow.AddHours(1), false,
                "Data", "synthetic-gap", "stooq", AutoRemediationTriggerSource.DataQualityGap)
            });
        using var services = new ServiceCollection().AddSingleton(history).BuildServiceProvider();
        var result = WorkstationEndpoints.ExportRetainedBackfillRemediation(new(["execution", "provider"], SearchText: "AAPL"),
            Context(services, UserPermission.ViewHistoricalData), new(JsonSerializerDefaults.Web));
        var exported = result.Should().BeOfType<JsonHttpResult<WorkstationTableExportDto>>().Which.Value!;
        exported.RowCount.Should().Be(75);
        exported.Csv.Should().Contain("global host backfill history").And.Contain("all retained executions before filtering");
    }

    private static CorporateActionSourceProposalDto Proposal()
    {
        var securityId = Guid.NewGuid();
        var action = new CorporateActionDto(Guid.NewGuid(), securityId, "Dividend", new(2026, 11, 11), null,
            12.345m, "USD", null, null, null, null, null, null, null);
        return new(Guid.NewGuid(), securityId, new("public-source", "private-provider-event", "v1", DateTimeOffset.UtcNow),
            action, 1, "fingerprint", CorporateActionSourceProposalStates.ReviewRequired, 1, null, null, null,
            "private-recording-actor", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
            DisplayMetadata: new("AAPL", "public-source", ["public-source"], []));
    }

    private static CorporateActionProcessingCaseDto Case(CorporateActionSourceProposalDto proposal, string tenant, string company)
        => new(Guid.NewGuid(), proposal.ProposalId, Guid.NewGuid(), proposal.SecurityId, new(tenant, company),
            "Detected", 1, null, null, null, "private-recording-actor", DateTimeOffset.UtcNow,
            "private-recording-actor", DateTimeOffset.UtcNow,
            SourceSnapshot: new(proposal.ProposedAction, proposal.ProviderIdentity, proposal.DisplayMetadata));

    private static DefaultHttpContext Context(IServiceProvider services, UserPermission permission)
    {
        var context = new DefaultHttpContext { RequestServices = services };
        context.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] = permission;
        context.Items[LoginSessionMiddleware.CurrentUserKey] = "synthetic-operator";
        context.Items[LoginSessionMiddleware.CurrentTenantIdKey] = "tenant-a";
        context.Items[LoginSessionMiddleware.CurrentUserCompanyIdKey] = "company-a";
        return context;
    }
}

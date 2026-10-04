using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.AccountingClose;
using Meridian.FinancialOperations.OperationsContinuity;
using Meridian.Contracts.Tenancy;
using Meridian.Ui.Shared.Services;
using Meridian.Storage.Ledger;
using Meridian.Ledger;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace Meridian.Tests.Ui;

public sealed partial class WorkstationEndpointsTests
{
    private static void RegisterRetainedReportBook(IServiceCollection services, Guid bookId, Guid accountId)
    {
        var books = new Mock<ILedgerBookService>(MockBehavior.Strict);
        books.Setup(service => service.GetBookAsync(bookId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LedgerBookDto(bookId, "test-fund-profile", accountId,
                FundStructureNodeKindDto.Account, "Primary", "USD", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
        books.Setup(service => service.ListPeriodsAsync(It.IsAny<LedgerPeriodQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new LedgerPeriodDto(Guid.NewGuid(), bookId, 2026, 5, "2026-05",
                new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 31), LedgerPeriodStatusDto.Open,
                DateTimeOffset.UtcNow, null, 1)]);
        services.AddSingleton(books.Object);
    }

    private static async Task<string> RetainPreCloseReportAsync(IServiceProvider services, Guid bookId)
    {
        var package = await services.GetRequiredService<IAccountingReportPackageService>().BuildPackageAsync(
            new AccountingReportPackageRequestDto("test-fund-profile", "2026-05", "report-preparer",
                LedgerBookId: bookId, TenantId: "tenant-test", CompanyId: "tenant-test", EvidenceLinks:
                [
                    $"evidence:ledger:trial-balance:2026-05:book:{bookId:D}",
                    $"evidence:reconciliation:gl-tie-out:2026-05:book:{bookId:D}",
                    $"evidence:report-render:financial-statements:2026-05:book:{bookId:D}",
                    $"evidence:nav:support-package:2026-05:book:{bookId:D}"
                ]));
        package.Certification.State.Should().Be(AccountingCertificationStateDto.ReadyForReview);
        return package.FinancialStatements.PackageId;
    }

    [Fact]
    public async Task OperationsPosture_CallerCannotInventReadyReportPackage()
    {
        await using var app = await CreateAppAsync(RegisterOperationsContinuityServices);
        var service = app.Services.GetRequiredService<IOperationsContinuityWorkflowService>();
        var start = await service.StartWorkflowAsync(new OperationsStartWorkflowRequestDto(
            Guid.NewGuid(), "2026-05", null, "custodian", "ops-user"));
        var workflow = start.Workflow!;
        using var response = await app.GetTestClient().PostAsJsonAsync(
            $"/api/workstation/operations/continuity/{workflow.WorkflowId}/posture/refresh",
            new OperationsGatePostureRequestDto(workflow.Version, "ops-user",
                ReportPackReady: true, ReportPackId: "invented-report"), ServerJsonOptions);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var result = await response.Content.ReadFromJsonAsync<OperationsTransitionResultDto>(ServerJsonOptions);
        result!.Blockers.Should().Contain(blocker => blocker.Code == "REPORT_PACK_NOT_READY");
        var unchanged = await service.GetAsync(workflow.WorkflowId);
        unchanged!.Version.Should().Be(workflow.Version);
        unchanged.ReportPackReadiness.IsReady.Should().BeFalse();
    }

    [Fact]
    public async Task RetainedReportSupport_RejectsForeignScopeAndStaleEvidence_RecoversAfterRebuild()
    {
        var bookId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        await using var app = await CreateAppAsync(services =>
        {
            RegisterOperationsContinuityServices(services);
            RegisterRetainedReportBook(services, bookId, accountId);
        });
        var workflows = app.Services.GetRequiredService<IOperationsContinuityWorkflowService>();
        var start = await workflows.StartWorkflowAsync(new OperationsStartWorkflowRequestDto(
            accountId, "2026-05", null, "custodian", "ops-user", LedgerBookId: bookId));
        var imported = await workflows.ImportBrokerDataAsync(start.Workflow!.WorkflowId,
            new OperationsTransitionRequestDto(start.Workflow.Version, "ops-user"));
        var packageId = await RetainPreCloseReportAsync(app.Services, bookId);
        var authority = new OperationsReportPackAuthority(
            app.Services.GetRequiredService<IAccountingReportPackageService>(),
            app.Services.GetRequiredService<ILedgerBookService>(),
            app.Services.GetRequiredService<IFundProfileTenancyRegistry>(),
            app.Services.GetRequiredService<ILedgerJournalStore>());
        var ready = await authority.ResolveAsync(imported.Workflow!, packageId, "tenant-test", "tenant-test");
        ready.IsReady.Should().BeTrue();
        ready.EvidenceLinks.Should().ContainSingle(link => link.EvidenceId == packageId
            && link.Source == "accounting-report-pack");
        (await authority.ResolveAsync(imported.Workflow!, packageId, "tenant-test", "foreign-company"))
            .IsReady.Should().BeFalse();
        (await authority.ResolveAsync(imported.Workflow!, "fabricated", "tenant-test", "tenant-test"))
            .IsReady.Should().BeFalse();
        (await authority.ResolveAsync(imported.Workflow! with { PeriodId = "2026-06" }, packageId,
            "tenant-test", "tenant-test")).IsReady.Should().BeFalse();
        (await authority.ResolveAsync(imported.Workflow! with { FundAccountId = Guid.NewGuid() }, packageId,
            "tenant-test", "tenant-test")).IsReady.Should().BeFalse();

        var normalized = await workflows.NormalizeBrokerTransactionsAsync(imported.Workflow!.WorkflowId,
            new OperationsTransitionRequestDto(imported.Workflow.Version, "ops-user"));
        normalized.Success.Should().BeTrue();
        var stale = await authority.ResolveAsync(normalized.Workflow!, packageId, "tenant-test", "tenant-test");
        stale.IsReady.Should().BeFalse();
        stale.BlockingReason.Should().Contain("Rebuild");
        var rebuiltId = await RetainPreCloseReportAsync(app.Services, bookId);
        var rebuilt = await authority.ResolveAsync(normalized.Workflow!, rebuiltId, "tenant-test", "tenant-test");
        rebuilt.IsReady.Should().BeTrue();
        rebuiltId.Should().Be(packageId, "rebuilds retain package identity while changing the retained revision");
        OperationsReportPackAuthority.MatchesRetainedRevision(ready, rebuilt).Should().BeFalse();
        OperationsReportPackAuthority.MatchesRetainedRevision(rebuilt, rebuilt).Should().BeTrue();
    }

    [Fact]
    public async Task RetainedReportSupport_ExternalLedgerPostInvalidatesReviewWithoutOperationsEvent()
    {
        var bookId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        await using var app = await CreateAppAsync(services =>
        {
            RegisterOperationsContinuityServices(services);
            RegisterRetainedReportBook(services, bookId, accountId);
        });
        var workflows = app.Services.GetRequiredService<IOperationsContinuityWorkflowService>();
        var workflow = (await workflows.StartWorkflowAsync(new OperationsStartWorkflowRequestDto(
            accountId, "2026-05", null, "custodian", "ops-user", LedgerBookId: bookId))).Workflow!;
        var books = app.Services.GetRequiredService<ILedgerBookService>();
        var periodId = (await books.ListPeriodsAsync(new LedgerPeriodQuery(LedgerBookId: bookId))).Single().PeriodId;
        IReadOnlyList<LedgerJournalEntryRecord> records = [];
        var journals = new Mock<ILedgerJournalStore>(MockBehavior.Strict);
        journals.Setup(store => store.QueryAsync(It.Is<LedgerJournalEntryQuery>(query =>
                query.LedgerBookId == bookId && query.PeriodId == periodId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => records);
        var authority = new OperationsReportPackAuthority(
            app.Services.GetRequiredService<IAccountingReportPackageService>(), books,
            app.Services.GetRequiredService<IFundProfileTenancyRegistry>(), journals.Object);
        var packageId = await RetainPreCloseReportAsync(app.Services, bookId);
        var reviewed = await authority.ResolveAsync(workflow, packageId, "tenant-test", "tenant-test");
        reviewed.IsReady.Should().BeTrue();

        var entryId = Guid.NewGuid();
        var effectiveAt = new DateTimeOffset(2026, 5, 31, 12, 0, 0, TimeSpan.Zero);
        const string description = "Independent ledger adjustment";
        var entry = new Meridian.Ledger.JournalEntry(entryId, effectiveAt, description,
        [
            new LedgerEntry(Guid.NewGuid(), entryId, effectiveAt, new LedgerAccount("Cash", LedgerAccountType.Asset), 10m, 0m, description),
            new LedgerEntry(Guid.NewGuid(), entryId, effectiveAt, new LedgerAccount("Income", LedgerAccountType.Revenue), 0m, 10m, description)
        ]);
        records = [new LedgerJournalEntryRecord(entry, accountId, periodId, null, null, 17, DateTimeOffset.UtcNow)];
        (await workflows.GetAsync(workflow.WorkflowId))!.Version.Should().Be(workflow.Version,
            "the independent ledger producer did not update the Operations timeline");
        var stale = await authority.ResolveAsync(workflow, packageId, "tenant-test", "tenant-test");
        stale.IsReady.Should().BeFalse();
        stale.BlockingReason.Should().Contain("canonical ledger posting");

        var rebuiltId = await RetainPreCloseReportAsync(app.Services, bookId);
        var rebuilt = await authority.ResolveAsync(workflow, rebuiltId, "tenant-test", "tenant-test");
        rebuilt.IsReady.Should().BeTrue();
        OperationsReportPackAuthority.MatchesRetainedRevision(reviewed, rebuilt).Should().BeFalse();
        // Even replay or migration that preserves the old timestamp must change the journal fingerprint.
        records = [records[0] with { GlobalSequence = 18 }];
        var replayed = await authority.ResolveAsync(workflow, rebuiltId, "tenant-test", "tenant-test");
        replayed.IsReady.Should().BeTrue();
        OperationsReportPackAuthority.MatchesRetainedRevision(rebuilt, replayed).Should().BeFalse();
    }
}

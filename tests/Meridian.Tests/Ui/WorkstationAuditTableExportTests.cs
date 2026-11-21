using System.Text.Json;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Tenancy;
using Meridian.Contracts.Workstation;
using Meridian.Execution.Services;
using Meridian.Execution.Models;
using Meridian.FinancialOperations.OperationsContinuity;
using Meridian.Identity.Auth;
using Meridian.Identity;
using Meridian.Ledger;
using Meridian.Storage.Archival;
using Meridian.Ui.Shared.Endpoints;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Meridian.Tests.Ui;

public sealed class WorkstationAuditTableExportTests
{
    [Fact]
    public async Task Export_ReadsMoreThanOneThousandRetainedMatchesWithSameFiltersOrderAndSelectedColumns()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-audit-export-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using var trail = new ExecutionAuditTrailService(new ExecutionAuditTrailOptions(root, 2000, WalSyncMode.NoSync), NullLogger<ExecutionAuditTrailService>.Instance);
            var now = DateTimeOffset.UtcNow;
            for (var index = 0; index < 1105; index++)
                await trail.RecordAsync(new ExecutionAuditEntry($"audit-{index:D4}", "Order", "Submitted", "Accepted", now.AddSeconds(-index),
                    Actor: "=UNSAFE()", OrderId: $"order-{index}", Symbol: index % 2 == 0 ? "AAPL" : "MSFT", Message: "matching retained source"));
            var explorer = new AuditTrailExplorerService(trail);
            using var services = new ServiceCollection().AddSingleton(explorer).BuildServiceProvider();
            var context = Context(services);
            var preview = await explorer.SearchAuthorizedAsync(new(SearchText: "matching", Limit: 50), (_, _) => Task.FromResult(false));
            Assert.Equal(50, preview.Returned);
            Assert.Equal(1105, preview.TotalMatched);
            var result = await Export(new(["column-1", "column-5"], SearchText: "matching"), context);
            var csv = Assert.IsType<JsonHttpResult<WorkstationTableExportDto>>(result).Value!;
            Assert.Equal(1105, csv.RowCount);
            Assert.StartsWith("Occurred UTC,Actor,Context · Audit ID", csv.Csv);
            Assert.Contains("'=UNSAFE()", csv.Csv);
            Assert.Contains("host-global execution feed; not tenant partitioned", csv.Csv);
            Assert.Contains("feed retention limits historical coverage", csv.Csv);
            Assert.Contains("audit-1104", csv.Csv);
            Assert.DoesNotContain("Context · Source query,limit", csv.Csv);
            var filtered = Assert.IsType<JsonHttpResult<WorkstationTableExportDto>>(await Export(new(["column-1"], SearchText: "matching",
                SortColumnId: "column-1", SortDirection: "asc", Filters: new Dictionary<string, string> { ["symbol"] = "AAPL" }), context)).Value!;
            Assert.Equal(553, filtered.RowCount);
            Assert.Contains("audit-1104", filtered.Csv.Split("\r\n")[1]);
            Assert.DoesNotContain("audit-1103", filtered.Csv);
            var denied = Context(services, UserPermission.ViewDiagnostics);
            Assert.Equal(403, Assert.IsAssignableFrom<IStatusCodeHttpResult>(await Export(new(["column-1"]), denied)).StatusCode);
            Assert.Equal(400, Assert.IsAssignableFrom<IStatusCodeHttpResult>(await Export(new(["column-1"], Filters: new Dictionary<string, string> { ["limit"] = "50" }), context)).StatusCode);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData("tenant-b", "company-a")]
    [InlineData("tenant-a", "company-b")]
    public async Task Export_DoesNotReadForeignOperationsTimelineEvenForAdmin(string tenant, string company)
    {
        var fixture = new OperationsFixture(tenant, company);
        using var services = fixture.Services();
        var result = Assert.IsType<JsonHttpResult<WorkstationTableExportDto>>(await Export(new(["column-2"]), Context(services))).Value!;
        Assert.Equal(0, result.RowCount);
        fixture.Operations.Verify(value => value.GetTimelineAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Export_RejectsMismatchedBookOrAccountBeforeTimelineRead(bool bookMismatch)
    {
        var fixture = new OperationsFixture();
        fixture.Books.Setup(value => value.GetBookAsync(fixture.BookId, It.IsAny<CancellationToken>())).ReturnsAsync(fixture.Book with
        { LedgerBookId = bookMismatch ? Guid.NewGuid() : fixture.BookId, FundStructureNodeId = bookMismatch ? fixture.AccountId : Guid.NewGuid() });
        using var services = fixture.Services();
        var csv = Assert.IsType<JsonHttpResult<WorkstationTableExportDto>>(await Export(new(["column-2"]), Context(services))).Value!;
        Assert.Equal(0, csv.RowCount);
        fixture.Operations.Verify(value => value.GetTimelineAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Export_RefusesWholeResultWhenReturnedTimelineIdentityIsInconsistent(bool workflowMismatch)
    {
        var fixture = new OperationsFixture();
        fixture.Operations.Setup(value => value.GetTimelineAsync(fixture.WorkflowId, It.IsAny<CancellationToken>())).ReturnsAsync(
            [fixture.Timeline with { WorkflowId = workflowMismatch ? Guid.NewGuid() : fixture.WorkflowId, FundAccountId = workflowMismatch ? fixture.AccountId : Guid.NewGuid() }]);
        using var services = fixture.Services();
        var result = await Export(new(["column-2"]), Context(services));
        Assert.Equal(503, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
        Assert.IsNotType<JsonHttpResult<WorkstationTableExportDto>>(result);
    }

    [Fact]
    public async Task BrowseAndExport_ApplyTheSameWorkflowAuthorityAndExposeItsNamespace()
    {
        var fixture = new OperationsFixture();
        using var services = fixture.Services();
        var context = Context(services);
        var browse = await fixture.Explorer.SearchAuthorizedAsync(new(), (workflow, token) => AuditTrailHttpAuthority.CanReadWorkflowAsync(context, workflow, token));
        var csv = Assert.IsType<JsonHttpResult<WorkstationTableExportDto>>(await Export(new(["column-2", "column-6"]), context)).Value!;
        Assert.Single(browse.Entries);
        Assert.Equal(1, csv.RowCount);
        Assert.Contains("authorized tenant/company operations timeline", csv.Csv);
        var viewTradesOnly = Context(services, UserPermission.ViewTrades);
        var denied = await fixture.Explorer.SearchAuthorizedAsync(new(), (workflow, token) => AuditTrailHttpAuthority.CanReadWorkflowAsync(viewTradesOnly, workflow, token));
        Assert.Empty(denied.Entries);
        fixture.Operations.Verify(value => value.GetTimelineAsync(fixture.WorkflowId, It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task Export_MissingSourceAndInvalidDateBoundsRefuseWithoutPretendCompleteness()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var context = Context(services);
        Assert.Equal(503, Assert.IsAssignableFrom<IStatusCodeHttpResult>(await Export(new(["column-1"]), context)).StatusCode);
        Assert.Equal(400, Assert.IsAssignableFrom<IStatusCodeHttpResult>(await Export(new(["column-1"], Filters: new Dictionary<string, string> { ["fromUtc"] = "2026-11-11" }), context)).StatusCode);
    }

    [Fact]
    public async Task Export_OverQuotaRefusesInsteadOfReturningAPartialCsv()
    {
        var fixture = new OperationsFixture();
        fixture.Operations.Setup(value => value.GetTimelineAsync(fixture.WorkflowId, It.IsAny<CancellationToken>())).ReturnsAsync(
            Enumerable.Repeat(fixture.Timeline, AuditTrailExplorerService.MaximumExportEntries + 1).ToArray());
        using var services = fixture.Services();
        var result = await Export(new(["column-2"]), Context(services));
        Assert.Equal(413, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
        Assert.IsNotType<JsonHttpResult<WorkstationTableExportDto>>(result);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task NonAdminOperationsRead_RequiresTheExactAccountScopedGrant(bool allowed)
    {
        var fixture = new OperationsFixture();
        var permissions = UserPermission.ViewTrades | UserPermission.ViewDirectLending;
        var scoped = new Mock<IScopedAuthorizationService>(MockBehavior.Strict);
        scoped.Setup(value => value.AuthorizeAsync("operator-a", UserPermission.ViewTrades, AccessScopeKindDto.Account,
            fixture.AccountId, permissions, It.IsAny<CancellationToken>())).ReturnsAsync(new ScopedAuthorizationDecisionDto(
                allowed, "operator-a", UserPermission.ViewTrades, AccessScopeKindDto.Account, fixture.AccountId, "retained test grant"));
        using var services = fixture.Services(scoped.Object);
        var result = Assert.IsType<JsonHttpResult<WorkstationTableExportDto>>(await Export(new(["column-2"]), Context(services, permissions))).Value!;
        Assert.Equal(allowed ? 1 : 0, result.RowCount);
        scoped.VerifyAll();
        fixture.Operations.Verify(value => value.GetTimelineAsync(fixture.WorkflowId, It.IsAny<CancellationToken>()), allowed ? Times.Once() : Times.Never());
    }

    [Fact]
    public async Task Export_EnforcesBrokerageHostAccountGuardBeforeAnyAuditSourceRead()
    {
        var fixture = new OperationsFixture();
        var synchronization = new LiveBrokeragePortfolioSyncService(() => null, () => null,
            new BrokerageExecutionReconciliationService(NullLogger<BrokerageExecutionReconciliationService>.Instance));
        using var services = new ServiceCollection().AddSingleton(fixture.Explorer)
            .AddSingleton<IPortfolioState>(new BrokeragePortfolioState(synchronization)).BuildServiceProvider();
        var result = await Export(new(["column-1"]), Context(services));
        Assert.Equal(403, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
        fixture.Operations.Verify(value => value.ListAsync(null, null, null, It.IsAny<CancellationToken>(), null), Times.Never);
    }

    private static Task<IResult?> Export(WorkstationTableExportRequest request, HttpContext context)
        => WorkstationEndpoints.TryExportExecutionAuditAsync("trading-execution-audit", request, context, new(JsonSerializerDefaults.Web));
    private static DefaultHttpContext Context(IServiceProvider services, UserPermission permission = UserPermission.ViewTrades | UserPermission.AdminMaintenance)
    {
        var context = new DefaultHttpContext { RequestServices = services };
        context.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] = permission;
        context.Items[LoginSessionMiddleware.CurrentUserKey] = "operator-a";
        context.Items[LoginSessionMiddleware.CurrentTenantIdKey] = "tenant-a";
        context.Items[LoginSessionMiddleware.CurrentUserCompanyIdKey] = "company-a";
        return context;
    }
    private sealed class OperationsFixture
    {
        public Guid BookId { get; } = Guid.NewGuid();
        public Guid AccountId { get; } = Guid.NewGuid();
        public Guid WorkflowId { get; } = Guid.NewGuid();
        public Mock<IOperationsContinuityWorkflowService> Operations { get; } = new();
        public Mock<ILedgerBookService> Books { get; } = new();
        public Mock<IFundProfileTenancyRegistry> Tenancy { get; } = new();
        public LedgerBookDto Book { get; }
        public OperationsTimelineEntryDto Timeline { get; }
        public AuditTrailExplorerService Explorer { get; }
        public OperationsFixture(string tenant = "tenant-a", string company = "company-a")
        {
            var now = DateTimeOffset.UtcNow;
            Book = new(BookId, "fund-a", AccountId, FundStructureNodeKindDto.Account, "Owned book", "USD", now, now);
            Books.Setup(value => value.GetBookAsync(BookId, It.IsAny<CancellationToken>())).ReturnsAsync(Book);
            Tenancy.Setup(value => value.ResolveAsync("fund-a", It.IsAny<CancellationToken>())).ReturnsAsync(new FundProfileOwnership("fund-a", tenant, company));
            var summary = new OperationsContinuityWorkflowSummaryDto(WorkflowId, AccountId, "period", null, "source", OperationsWorkflowStatusDto.ApprovalPending, 1, now, now, [], [], BookId);
            Operations.Setup(value => value.ListAsync(null, null, null, It.IsAny<CancellationToken>(), null)).ReturnsAsync([summary]);
            Timeline = new(Guid.NewGuid(), now, WorkflowId, AccountId, "period", "Reviewed", OperationsWorkflowStatusDto.ApprovalPending,
                OperationsWorkflowStatusDto.ApprovalPending, OperationsGateKeyDto.Approval, null, null, "operator-a", "retained rationale", null, null, [], null, "retained-hash");
            Operations.Setup(value => value.GetTimelineAsync(WorkflowId, It.IsAny<CancellationToken>())).ReturnsAsync([Timeline]);
            Explorer = new(operationsContinuity: Operations.Object);
        }
        public ServiceProvider Services(IScopedAuthorizationService? scoped = null)
        {
            var services = new ServiceCollection().AddSingleton(Explorer).AddSingleton(Books.Object).AddSingleton(Tenancy.Object);
            if (scoped is not null)
                services.AddSingleton(scoped);
            return services.BuildServiceProvider();
        }
    }
}

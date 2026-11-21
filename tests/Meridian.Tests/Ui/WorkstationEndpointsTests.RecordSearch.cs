using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Api;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Tenancy;
using Meridian.Contracts.Workstation;
using Meridian.Domain.Reconciliation;
using Meridian.FinancialOperations.OperationsContinuity;
using Meridian.FinancialOperations.Reconciliation;
using Meridian.Identity;
using Meridian.Identity.Auth;
using Meridian.PortfolioRecords.Accounts;
using Meridian.Storage.Ledger;
using Meridian.Strategies.Services;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Meridian.Tests.Ui;

public sealed partial class WorkstationEndpointsTests
{
    [Theory]
    [InlineData(UserPermission.None, "tenant-test")]
    [InlineData(UserPermission.AdminMaintenance, null)]
    public async Task RecordSearch_RequiresPermissionAndBoundCompany(UserPermission permissions, string? company)
    {
        await using var app = await CreateAppAsync(
            currentUserPermissions: permissions, currentUserCompanyId: company, currentUserTenantId: "tenant-test");
        using var response = await app.GetTestClient().GetAsync($"{UiApiRoutes.WorkstationRecordSearch}?q=abc");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task RecordSearch_RanksTypedExactIdsBeforeIdentifierAndNamePrefixes()
    {
        var exactId = Guid.Parse("abcdef12-0000-0000-0000-000000000001");
        var security = Substitute.For<ISecurityMasterQueryService>();
        security.SearchAsync(Arg.Any<SecuritySearchRequest>(), Arg.Any<CancellationToken>()).Returns(
            [
                new SecuritySummaryDto(Guid.Parse("20000000-0000-0000-0000-000000000001"), "Equity", SecurityStatusDto.Active, "ABCD name match", "ZZZ", "USD", 1),
                new SecuritySummaryDto(Guid.Parse("20000000-0000-0000-0000-000000000002"), "Equity", SecurityStatusDto.Active, "Unrelated name", "ABC", "USD", 1),
                new SecuritySummaryDto(exactId, "Equity", SecurityStatusDto.Inactive, "Former instrument", "FORM", "USD", 1),
                new SecuritySummaryDto(Guid.Parse("20000000-0000-0000-0000-000000000003"), "Equity", SecurityStatusDto.Active, "Contains ABC only", "ZZX", "USD", 1)
            ]);
        await using var app = await CreateAppAsync(
            services => services.AddSingleton(security), currentUserPermissions: UserPermission.ViewSecurityMaster);
        var client = app.GetTestClient();

        var prefix = await SearchRecordsAsync(client, " abc ");
        prefix.Items.Select(hit => hit.Label).Should().Equal("Unrelated name", "Former instrument", "ABCD name match");

        var exact = (await SearchRecordsAsync(client, exactId.ToString("N"))).Items.Should().ContainSingle().Subject;
        exact.RecordType.Should().Be("Instrument");
        exact.Status.Should().Be("Inactive");
        exact.AsOfPosture.Should().Be("Current reference");
        exact.Href.Should().Be($"/accounting/security-master/detail?recordScope=1&securityId={exactId:D}");
        await security.Received().SearchAsync(Arg.Is<SecuritySearchRequest>(request => request.PrefixOnly && !request.ActiveOnly),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RecordSearch_FiltersCasesStatementsAndWorkflowsByAccountTenantAndBook()
    {
        var visible = SearchAccount("Authorized account");
        var denied = SearchAccount("Restricted account");
        var foreign = SearchAccount("Foreign account");
        var treasury = Substitute.For<IFundProfileTenancyRegistry>();
        treasury.ResolveAsync(visible.FundId!.Value.ToString("D"), Arg.Any<CancellationToken>())
            .Returns(new FundProfileOwnership(visible.FundId.Value.ToString("D"), "tenant-test", "tenant-test"));
        treasury.ResolveAsync(denied.FundId!.Value.ToString("D"), Arg.Any<CancellationToken>())
            .Returns(new FundProfileOwnership(denied.FundId.Value.ToString("D"), "tenant-test", "tenant-test"));
        treasury.ResolveAsync(foreign.FundId!.Value.ToString("D"), Arg.Any<CancellationToken>())
            .Returns(new FundProfileOwnership(foreign.FundId.Value.ToString("D"), "tenant-test", "other-company"));

        var workflowId = Guid.NewGuid();
        var foreignBook = Guid.NewGuid();
        var books = Substitute.For<ILedgerJournalStore>();
        books.GetLedgerBookAsync(foreignBook, Arg.Any<CancellationToken>())
            .Returns(new LedgerBookRecord(foreignBook, foreign.FundId.Value.ToString("D"), visible.AccountId,
                FundStructureNodeKindDto.Account, "Foreign book", "USD", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch));
        var workflows = Substitute.For<IOperationsContinuityWorkflowService>();
        workflows.ListAsync(ct: Arg.Any<CancellationToken>()).Returns(
            [
                new OperationsContinuityWorkflowSummaryDto(workflowId, visible.AccountId, "2026-06", null, "fixture",
                    default, 1, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, [], []),
                new OperationsContinuityWorkflowSummaryDto(Guid.NewGuid(), visible.AccountId, "2026-06", null, "fixture",
                    default, 1, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, [], [], LedgerBookId: foreignBook),
                new OperationsContinuityWorkflowSummaryDto(Guid.NewGuid(), denied.AccountId, "2026-06", null, "fixture",
                    default, 1, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, [], [])
            ]);
        var statements = Substitute.For<IStatementRunWorkflowService>();
        statements.ListImportsAsync(Arg.Any<CancellationToken>()).Returns(
            [
                SearchStatement("test-visible-statement", visible),
                SearchStatement("test-denied-statement", denied),
                SearchStatement("test-foreign-statement", foreign),
                SearchStatement("test-inconsistent-statement", visible) with
                {
                    AccountingScope = new StatementAccountingScope(denied.FundId.Value.ToString("D"), foreignBook,
                        Guid.NewGuid(), new DateOnly(2026, 6, 30))
                }
            ]);
        statements.ListOpenBreaksAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<ReconciliationBreakRecord>());

        await using var app = await CreateAppAsync(services =>
        {
            services.AddSingleton(SearchAccounts(visible, denied, foreign));
            // A grant on an account cannot confer authority over its foreign ownership.
            services.AddSingleton(SearchGrants(UserPermission.ViewDirectLending, visible.AccountId, foreign.AccountId));
            services.AddSingleton(treasury);
            services.AddSingleton(books);
            services.AddSingleton(workflows);
            services.AddSingleton(statements);
        }, currentUserPermissions: UserPermission.ViewDirectLending);
        var queue = app.Services.GetRequiredService<IReconciliationBreakQueueRepository>();
        foreach (var (id, account) in new[] { ("test-visible-case", visible), ("test-denied-case", denied), ("test-foreign-case", foreign) })
        {
            await queue.CreateIfMissingAsync(TestReconciliationQueueScope,
                BuildBreakQueueItem(id, null) with { FundAccountId = account.AccountId.ToString("D") });
        }
        await queue.CreateIfMissingAsync(new ReconciliationBreakQueueScope("other-tenant", "tenant-test"),
            BuildBreakQueueItem("test-cross-tenant-case", null) with
            {
                TenantId = "other-tenant",
                FundAccountId = visible.AccountId.ToString("D")
            });
        var client = app.GetTestClient();

        var results = await SearchRecordsAsync(client, "test-");
        results.IsPartial.Should().BeFalse();
        results.Items.Select(hit => hit.RecordId).Should().BeEquivalentTo(["test-visible-case", "test-visible-statement"]);
        results.Items.Should().OnlyContain(hit => hit.Scope == visible.DisplayName);
        results.Items.Should().Contain(hit => hit.RecordType == "Reconciliation case" &&
            hit.Href.EndsWith("reconciliationRun=run-test-visible-case&reconciliationBreak=test-visible-case"));
        results.Items.Should().Contain(hit => hit.RecordType == "Statement run" && hit.AsOfPosture == "Statement through 2026-06-30" &&
            hit.Href.EndsWith("reconciliationRun=test-visible-statement#statement-runs"));

        (await SearchRecordsAsync(client, "test-denied-case")).Items.Should().BeEmpty();
        var workflow = (await SearchRecordsAsync(client, "2026-06")).Items.Should().ContainSingle().Subject;
        workflow.RecordId.Should().Be(workflowId.ToString("D"));
        workflow.Href.Should().EndWith($"workflowId={workflowId:D}");
    }

    [Fact]
    public async Task RecordSearch_RequiresDraftPermissionOnTheBook()
    {
        var nodeId = Guid.NewGuid();
        var book = new LedgerBookRecord(Guid.NewGuid(), Guid.NewGuid().ToString("D"), nodeId,
            FundStructureNodeKindDto.Account, "Authorized book", "USD", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
        var journalStore = Substitute.For<ILedgerJournalStore>();
        journalStore.ListLedgerBooksAsync(ct: Arg.Any<CancellationToken>()).Returns([book]);
        journalStore.QueryAsync(Arg.Any<LedgerJournalEntryQuery>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<LedgerJournalEntryRecord>());
        var drafts = Substitute.For<IManualJournalEntryDraftStore>();
        var journalId = Guid.NewGuid();

        await using var app = await CreateAppAsync(services =>
        {
            services.AddSingleton(journalStore);
            services.AddSingleton(drafts);
            services.AddSingleton(SearchGrants(UserPermission.ViewLedgerReports, nodeId));
        }, currentUserPermissions: UserPermission.ViewLedgerReports | UserPermission.ManageLedgerReports);

        (await SearchRecordsAsync(app.GetTestClient(), journalId.ToString("N"))).Items.Should().BeEmpty();
        await journalStore.Received(1).QueryAsync(
            Arg.Is<LedgerJournalEntryQuery>(query => query.LedgerBookId == book.LedgerBookId && query.JournalEntryId == journalId),
            Arg.Any<CancellationToken>());
        drafts.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task RecordSearch_RetainedPositionsKeepOpaqueIdsAndAuthorizedAccountScope()
    {
        using var root = new SearchProjectionDirectory();
        var visible = SearchAccount("Authorized holdings");
        var denied = SearchAccount("Restricted holdings");
        var opaqueId = Guid.NewGuid().ToString("N");
        await root.SeedAsync(visible, "VISIBLE", opaqueId);
        await root.SeedAsync(denied, "RESTRICTED", opaqueId);
        await using var app = await CreateAppAsync(services =>
        {
            services.AddSingleton(SearchAccounts(visible, denied));
            services.AddSingleton(SearchGrants(UserPermission.ViewTrades, visible.AccountId));
            services.AddSingleton(sp => new BrokeragePortfolioSyncService(
                new BrokeragePortfolioSyncOptions(root.Path, TimeSpan.FromMinutes(10), "fixture"),
                [], [], [], sp, NullLogger<BrokeragePortfolioSyncService>.Instance));
        }, currentUserPermissions: UserPermission.ViewTrades);

        var result = (await SearchRecordsAsync(app.GetTestClient(), opaqueId)).Items.Should().ContainSingle().Subject;
        result.RecordType.Should().Be("Position");
        result.RecordId.Should().Be(opaqueId);
        result.Label.Should().Be("VISIBLE");
        result.Scope.Should().Be(visible.DisplayName);
        result.Href.Should().Be($"/portfolio/brokerage-sync?recordScope=1&fundAccountId={visible.AccountId:D}&brokeragePosition={visible.AccountId:D}-VISIBLE-{opaqueId}&positionProvider=fixture");
    }

    private static async Task<WorkstationRecordSearchResult> SearchRecordsAsync(HttpClient client, string query)
    {
        using var response = await client.GetAsync($"{UiApiRoutes.WorkstationRecordSearch}?q={Uri.EscapeDataString(query)}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        return (await response.Content.ReadFromJsonAsync<WorkstationRecordSearchResult>(ServerJsonOptions))!;
    }

    private static AccountSummaryDto SearchAccount(string name) => new(
        Guid.NewGuid(), default, null, Guid.NewGuid(), null, null, name, name,
        "USD", null, true, DateTimeOffset.UnixEpoch, null, null, null, null, null);

    private static IAccountQueryService SearchAccounts(params AccountSummaryDto[] accounts)
    {
        var service = Substitute.For<IAccountQueryService>();
        service.ListAccountsAsync(null, null, null, Arg.Any<CancellationToken>()).Returns(accounts);
        return service;
    }

    private static IScopedAuthorizationService SearchGrants(UserPermission grantedPermission, params Guid[] ids)
    {
        var service = Substitute.For<IScopedAuthorizationService>();
        service.AuthorizeManyAsync(Arg.Any<string>(), Arg.Any<UserPermission>(), Arg.Any<AccessScopeKindDto>(),
            Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<UserPermission>(), Arg.Any<CancellationToken>())
            .Returns(call => call.ArgAt<IReadOnlyCollection<Guid>>(3).ToDictionary(
                id => id, id => new ScopedAuthorizationDecisionDto(
                    call.ArgAt<UserPermission>(1) == grantedPermission && ids.Contains(id),
                    call.ArgAt<string>(0), call.ArgAt<UserPermission>(1), call.ArgAt<AccessScopeKindDto>(2), id, "Test grant")));
        return service;
    }

    private static CanonicalStatementImport SearchStatement(string id, AccountSummaryDto account) =>
        new(id, "Synthetic custodian", new DateOnly(2026, 6, 30), DateTimeOffset.UnixEpoch, "synthetic", "synthetic", 1, 1)
        { FundAccountId = account.AccountId.ToString("D") };

    private sealed class SearchProjectionDirectory : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("meridian-record-search-").FullName;

        public Task SeedAsync(AccountSummaryDto account, string symbol, string id)
        {
            var accountId = account.AccountId;
            var timestamp = new DateTimeOffset(2026, 6, 30, 18, 0, 0, TimeSpan.Zero);
            var directory = Directory.CreateDirectory(System.IO.Path.Combine(Path, "projections", accountId.ToString("N")));
            var path = System.IO.Path.Combine(directory.FullName, "current.json");
            var activity = new FundAccountBrokerageSyncActivityDto(accountId,
                new WorkstationBrokerageAccountLinkDto(accountId, "fixture", "external", account.DisplayName, timestamp),
                new WorkstationBrokerageSyncStatusDto(accountId, "fixture", "external", WorkstationBrokerageSyncHealth.Healthy,
                    true, false, timestamp, timestamp, null, 1, 0, 0, 0, 0, []),
                null, [new FundAccountBrokeragePositionDto(symbol, 1, 2, 3, 4, 5, "Equity", null, PositionId: id)],
                [], [], [], timestamp, "fixture", path);
            return File.WriteAllTextAsync(path, JsonSerializer.Serialize(activity, ServerJsonOptions));
        }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}

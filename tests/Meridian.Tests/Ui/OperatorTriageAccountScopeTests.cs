using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Tenancy;
using Meridian.Contracts.Workstation;
using Meridian.Identity;
using Meridian.Identity.Auth;
using Meridian.PortfolioRecords.Accounts;
using Meridian.PortfolioRecords.FundAccounts;
using Meridian.Storage.Workstation;
using Meridian.Strategies.Services;
using Meridian.Ui.Shared.Endpoints;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Meridian.Tests.Ui;

public sealed class OperatorTriageAccountScopeTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    [Fact]
    public void AddingOptionalAccountScopePreservesPreviouslyRetainedUnscopedReceipts()
    {
        var command = new OperatorTriageCommand("retained-unscoped", OperatorTriageAction.Acknowledge, 0,
            "Reviewed retained evidence.");
        var legacyJson = JsonSerializer.Serialize(new
        {
            command.CommandId, command.Action, command.ExpectedVersion, command.Reason,
            command.AssigneeId, command.SnoozedUntil, command.ExpectedSourceVersion
        });
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(legacyJson)));
        var retained = OperatorTriageState.Empty with
        {
            Version = 1,
            Receipts = [new(Harness.Actor, command.CommandId, fingerprint)]
        };

        Assert.Equal(retained, OperatorTriagePolicy.Apply(retained, Harness.Actor, command,
            OperatorWorkItemToneDto.Warning, DateTimeOffset.UtcNow, false, out var replayed));
        Assert.True(replayed);
    }

    [Fact]
    public async Task SelectedBrokerageItem_AcknowledgementAndExactReplayUseTheSameAuthorizedAccount()
    {
        await using var harness = await Harness.CreateAsync();
        var item = await harness.BrokerageItemAsync();
        var command = new OperatorTriageCommand("ack-scoped", OperatorTriageAction.Acknowledge, item.Version,
            "Reviewed the retained brokerage sync warning.", FundAccountId: harness.AccountId);

        using var withoutScope = await harness.Client.PostAsJsonAsync(harness.ActionPath(item), command with { FundAccountId = null }, Json);
        Assert.Equal(HttpStatusCode.NotFound, withoutScope.StatusCode);

        using var applied = await harness.Client.PostAsJsonAsync(harness.ActionPath(item), command, Json);
        Assert.Equal(HttpStatusCode.OK, applied.StatusCode);
        var result = await applied.Content.ReadFromJsonAsync<OperatorTriageResult>(Json);
        Assert.Equal("Applied", result!.Status);
        Assert.Equal(harness.AccountId, result.Item!.FundAccountId);
        Assert.NotNull(result.Item.AcknowledgedAt);

        using var replay = await harness.Client.PostAsJsonAsync(harness.ActionPath(item), command, Json);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.True((await replay.Content.ReadFromJsonAsync<OperatorTriageResult>(Json))!.Replayed);
        var retained = await harness.Store.GetAsync(Harness.Scope, item.WorkItemId);
        Assert.Single(retained.Activity);
        Assert.Single(retained.Receipts);
    }

    [Fact]
    public async Task BulkAssignmentPreservesSelectedAccountForEachItem()
    {
        await using var harness = await Harness.CreateAsync();
        var item = await harness.BrokerageItemAsync();
        var command = new OperatorTriageBulkCommand("assign-scoped", OperatorTriageAction.Assign,
            "Assign the retained brokerage warning to this operator.", Harness.Actor,
            [new(item.WorkItemId, item.Version)], FundAccountId: harness.AccountId);

        using var response = await harness.Client.PostAsJsonAsync("/api/workstation/operator/triage/bulk", command, Json);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = Assert.Single((await response.Content.ReadFromJsonAsync<OperatorTriageBulkResult>(Json))!.Results);
        Assert.Equal("Applied", result.Status);
        Assert.Equal(Harness.Actor, result.Item!.OwnerId);
        Assert.Equal(harness.AccountId, result.Item.FundAccountId);
        Assert.Equal(Harness.Actor, (await harness.Store.GetAsync(Harness.Scope, item.WorkItemId)).AssigneeId);
    }

    [Fact]
    public async Task ForeignAccountReadAndCommandsAreRefusedBeforeReadingTheAccountOrWritingMetadata()
    {
        await using var harness = await Harness.CreateAsync();
        var item = await harness.BrokerageItemAsync();
        harness.Accounts.ClearReceivedCalls();
        var foreign = Guid.NewGuid();
        using var read = await harness.Client.GetAsync($"/api/workstation/operator/triage?fundAccountId={foreign:D}");
        Assert.Equal(HttpStatusCode.Forbidden, read.StatusCode);

        var command = new OperatorTriageCommand("ack-foreign", OperatorTriageAction.Acknowledge, item.Version,
            "Attempted foreign scope.", FundAccountId: foreign);
        using var single = await harness.Client.PostAsJsonAsync(harness.ActionPath(item), command, Json);
        Assert.Equal(HttpStatusCode.Forbidden, single.StatusCode);
        var bulk = new OperatorTriageBulkCommand("assign-foreign", OperatorTriageAction.Assign,
            "Attempted foreign scope.", Harness.Actor, [new(item.WorkItemId, item.Version)], FundAccountId: foreign);
        using var bulkResponse = await harness.Client.PostAsJsonAsync("/api/workstation/operator/triage/bulk", bulk, Json);
        Assert.Equal(HttpStatusCode.OK, bulkResponse.StatusCode);
        Assert.Equal("Forbidden", Assert.Single((await bulkResponse.Content.ReadFromJsonAsync<OperatorTriageBulkResult>(Json))!.Results).Status);

        await harness.Accounts.DidNotReceive().GetAccountAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        var retained = await harness.Store.GetAsync(Harness.Scope, item.WorkItemId);
        Assert.Empty(retained.Activity);
        Assert.Empty(retained.Receipts);
    }

    [Theory]
    [InlineData(UserPermission.ViewDirectLending)]
    [InlineData(UserPermission.ViewSecurityMaster)]
    public async Task AccountingOnlyOperatorCanAcknowledgeSelectedOwnedAccountWithoutTradingPermission(UserPermission permissions)
    {
        await using var harness = await Harness.CreateAsync(permissions);
        var item = await harness.AccountingItemAsync();
        var command = new OperatorTriageCommand("accounting-ack", OperatorTriageAction.Acknowledge, item.Version,
            "Reviewed the retained accounting case.", FundAccountId: harness.AccountId);
        using var response = await harness.Client.PostAsJsonAsync(harness.ActionPath(item), command, Json);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull((await response.Content.ReadFromJsonAsync<OperatorTriageResult>(Json))!.Item!.AcknowledgedAt);
    }

    [Theory]
    [InlineData("foreign-tenant", "company-triage")]
    [InlineData("tenant-triage", "foreign-company")]
    public async Task AccountingOnlyScopeRejectsForeignRetainedOwnership(string ownerTenant, string ownerCompany)
    {
        await using var harness = await Harness.CreateAsync(UserPermission.ViewDirectLending, ownerTenant, ownerCompany);
        var item = await harness.AccountingItemAsync(selectedAccount: false);
        using var read = await harness.Client.GetAsync($"/api/workstation/operator/triage?fundAccountId={harness.AccountId:D}");
        Assert.Equal(HttpStatusCode.Forbidden, read.StatusCode);
        using var response = await harness.Client.PostAsJsonAsync(harness.ActionPath(item),
            new OperatorTriageCommand("foreign-accounting-ack", OperatorTriageAction.Acknowledge, item.Version,
                "Attempted foreign ownership.", FundAccountId: harness.AccountId), Json);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty((await harness.Store.GetAsync(Harness.Scope, item.WorkItemId)).Receipts);
    }

    private sealed class Harness(WebApplication app, string root, Guid accountId, IAccountQueryService accounts,
        IOperatorTriageStore store) : IAsyncDisposable
    {
        public const string Actor = "triage-operator";
        public static readonly OperatorTriageScope Scope = new("tenant-triage", "company-triage");
        public HttpClient Client { get; } = app.GetTestClient();
        public Guid AccountId => accountId;
        public IAccountQueryService Accounts => accounts;
        public IOperatorTriageStore Store => store;
        public string ActionPath(OperatorTriageItemDto item)
            => $"/api/workstation/operator/triage/{Uri.EscapeDataString(item.WorkItemId)}/actions";

        public async Task<OperatorTriageItemDto> BrokerageItemAsync()
        {
            var inbox = await Client.GetFromJsonAsync<OperatorInboxTriageDto>(
                $"/api/workstation/operator/triage?fundAccountId={AccountId:D}", Json);
            var item = Assert.Single(inbox!.Items, row => row.Kind == OperatorWorkItemKindDto.BrokerageSync);
            Assert.True(item.Capabilities.CanAcknowledge);
            Assert.True(item.Capabilities.CanAssign);
            return item;
        }

        public async Task<OperatorTriageItemDto> AccountingItemAsync(bool selectedAccount = true)
        {
            var suffix = selectedAccount ? $"?fundAccountId={AccountId:D}" : string.Empty;
            var inbox = await Client.GetFromJsonAsync<OperatorInboxTriageDto>("/api/workstation/operator/triage" + suffix, Json);
            Assert.DoesNotContain(inbox!.Items, item => item.Kind == OperatorWorkItemKindDto.BrokerageSync);
            return Assert.Single(inbox.Items, item => item.Kind == OperatorWorkItemKindDto.ReconciliationBreak);
        }

        public static async Task<Harness> CreateAsync(UserPermission permissions = UserPermission.ViewTrades | UserPermission.ManageOrders,
            string? ownerTenant = null, string? ownerCompany = null)
        {
            var root = Path.Combine(Path.GetTempPath(), "meridian-triage-scope-" + Guid.NewGuid().ToString("N"));
            var accountId = Guid.NewGuid();
            var fundId = Guid.NewGuid();
            var retainedAccounts = new InMemoryFundAccountService();
            await retainedAccounts.CreateAccountAsync(new CreateAccountRequest(accountId, AccountTypeDto.Brokerage,
                "SYNTHETIC-TRIAGE", "Synthetic triage account", "USD", DateTimeOffset.UtcNow, Actor, FundId: fundId));
            var accounts = Substitute.For<IAccountQueryService>();
            accounts.GetAccountAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                .Returns(call => retainedAccounts.GetAccountAsync(call.Arg<Guid>(), call.Arg<CancellationToken>()));
            var store = new FileOperatorTriageStore(root);
            var tenancy = Substitute.For<IFundProfileTenancyRegistry>();
            tenancy.ResolveAsync(fundId.ToString("D"), Arg.Any<CancellationToken>()).Returns(Task.FromResult<FundProfileOwnership?>(
                new(fundId.ToString("D"), ownerTenant ?? Scope.TenantId, ownerCompany ?? Scope.CompanyId)));
            var queue = new FileReconciliationBreakQueueRepository(Path.Combine(root, "source"), NullLogger<FileReconciliationBreakQueueRepository>.Instance);
            if (!permissions.HasFlag(UserPermission.ViewTrades))
            {
                var now = DateTimeOffset.UtcNow;
                await queue.CreateIfMissingAsync(new ReconciliationBreakQueueScope(Scope.TenantId, Scope.CompanyId), new ReconciliationBreakQueueItem(
                    "accounting-scope-break", "synthetic-run", "Synthetic accounting review", ReconciliationBreakCategory.ExternalStatementMismatch,
                    ReconciliationBreakQueueStatus.Open, 1m, "Retained source differs.", null, now, now,
                    Severity: ReconciliationBreakSeverity.Medium, FundAccountId: accountId.ToString("D"))
                { TenantId = Scope.TenantId, CompanyId = Scope.CompanyId });
            }
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();
            builder.Services.AddSingleton<IAccountQueryService>(accounts);
            builder.Services.AddSingleton<IFundProfileTenancyRegistry>(tenancy);
            builder.Services.AddSingleton<IReconciliationBreakQueueRepository>(queue);
            builder.Services.AddSingleton<IScopedAuthorizationService>(new AccountAuthorization(accountId));
            builder.Services.AddSingleton<IOperatorTriageStore>(store);
            builder.Services.AddSingleton(provider => new BrokeragePortfolioSyncService(
                new BrokeragePortfolioSyncOptions(root, TimeSpan.FromMinutes(30), "alpaca"), [], [], [], provider,
                NullLogger<BrokeragePortfolioSyncService>.Instance));
            builder.Services.AddRateLimiter(options => options.AddPolicy(UiEndpoints.MutationRateLimitPolicy,
                _ => RateLimitPartition.GetNoLimiter("triage-scope-test")));
            var app = builder.Build();
            app.Use(async (context, next) =>
            {
                context.Items[LoginSessionMiddleware.CurrentUserKey] = Actor;
                context.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] = permissions;
                context.Items[LoginSessionMiddleware.CurrentUserCompanyIdKey] = Scope.CompanyId;
                context.Items[LoginSessionMiddleware.CurrentTenantIdKey] = Scope.TenantId;
                await next();
            });
            app.UseRouting();
            app.UseRateLimiter();
            typeof(WorkstationEndpoints).GetMethod("MapOperatorTriageEndpoints", BindingFlags.NonPublic | BindingFlags.Static)!
                .Invoke(null, [app.MapGroup("/api/workstation"), Json]);
            await app.StartAsync();
            return new(app, root, accountId, accounts, store);
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await app.DisposeAsync();
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private sealed class AccountAuthorization(Guid accountId) : IScopedAuthorizationService
    {
        public Task<ScopedAuthorizationDecisionDto> AuthorizeAsync(string actor, UserPermission requiredPermission,
            AccessScopeKindDto scopeKind, Guid? scopeId, UserPermission globalPermissions, CancellationToken ct = default)
            => Task.FromResult(new ScopedAuthorizationDecisionDto(scopeKind == AccessScopeKindDto.Account && scopeId == accountId,
                actor, requiredPermission, scopeKind, scopeId, "Synthetic account grant."));
    }
}

using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using Meridian.Contracts.Api;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Tenancy;
using Meridian.Identity;
using Meridian.Identity.Auth;
using Meridian.Storage.Ledger;
using Meridian.Ui.Shared.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;

namespace Meridian.Tests.Ui;

public sealed class WorkstationBookReceiptEndpointTests
{
    private static readonly Guid BookId = Guid.Parse("941d8a91-c293-4408-910d-d030447edbbb");
    private static readonly Guid EntityId = Guid.Parse("538da494-83da-487f-81fb-1278cb5a68a6");
    private static readonly DateOnly Date = new(2026, 11, 20);
    private static readonly BookReceiptScopeDto Scope = new("tenant-a", "company-a", BookId);
    private static readonly BookReceiptRunDto Run = new("retained-run", "book-receipts-v1", Scope, Date,
        "input-hash", [], [], new([], [], [], []));

    [Fact]
    public async Task Run_derives_scope_from_authenticated_book_and_reads_never_capture()
    {
        var queue = new Mock<IBookReceiptQueueService>(MockBehavior.Strict);
        BookReceiptQueryDto? actual = null;
        queue.Setup(x => x.RunAsync(It.IsAny<BookReceiptQueryDto>(), It.IsAny<CancellationToken>()))
            .Callback<BookReceiptQueryDto, CancellationToken>((query, _) => actual = query).ReturnsAsync(Run);
        queue.Setup(x => x.GetRunsAsync(Scope, It.IsAny<CancellationToken>())).ReturnsAsync([Run]);
        queue.Setup(x => x.GetRunAsync(Scope, Run.RunId, It.IsAny<CancellationToken>())).ReturnsAsync(Run);
        await using var app = await AppAsync(queue.Object);
        var client = app.GetTestClient();
        var created = await client.PostAsJsonAsync(UiApiRoutes.WorkstationBookReceiptRuns,
            new { ledgerBookId = BookId, asOfDate = Date, tenantId = "forged", companyId = "forged" });
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        Assert.Equal(new BookReceiptQueryDto(Scope, Date), actual);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"{UiApiRoutes.WorkstationBookReceiptRuns}?ledgerBookId={BookId}")).StatusCode);
        var replay = await client.GetAsync($"{UiApiRoutes.WorkstationBookReceiptRuns}/{Run.RunId}?ledgerBookId={BookId}");
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        var filtered = await client.GetFromJsonAsync<BookReceiptRunDto[]>($"{UiApiRoutes.WorkstationBookReceiptRuns}?ledgerBookId={BookId}&asOfDate=2026-11-19");
        Assert.Empty(filtered!);
        queue.Verify(x => x.RunAsync(It.IsAny<BookReceiptQueryDto>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("foreign-tenant", "company-a")]
    [InlineData("tenant-a", "foreign-company")]
    public async Task Foreign_book_is_denied_before_loading_or_capturing_evidence(string tenant, string company)
    {
        var queue = new Mock<IBookReceiptQueueService>(MockBehavior.Strict);
        await using var app = await AppAsync(queue.Object, ownerTenant: tenant, ownerCompany: company);
        var client = app.GetTestClient();
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"{UiApiRoutes.WorkstationBookReceiptRuns}?ledgerBookId={BookId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(UiApiRoutes.WorkstationBookReceiptRuns,
            new { ledgerBookId = BookId, asOfDate = Date })).StatusCode);
        queue.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Global_ledger_permission_cannot_read_or_run_an_unassigned_entity_book()
    {
        var queue = new Mock<IBookReceiptQueueService>(MockBehavior.Strict);
        await using var app = await AppAsync(queue.Object, scopedGrant: false);
        var client = app.GetTestClient();
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"{UiApiRoutes.WorkstationBookReceiptRuns}?ledgerBookId={BookId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(UiApiRoutes.WorkstationBookReceiptRuns,
            new { ledgerBookId = BookId, asOfDate = Date })).StatusCode);
        queue.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Report_viewer_can_replay_but_cannot_run_or_mutate_exceptions()
    {
        var queue = new Mock<IBookReceiptQueueService>(MockBehavior.Strict);
        queue.Setup(x => x.GetRunsAsync(Scope, It.IsAny<CancellationToken>())).ReturnsAsync([Run]);
        await using var app = await AppAsync(queue.Object, permission: UserPermission.ViewLedgerReports);
        var client = app.GetTestClient();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"{UiApiRoutes.WorkstationBookReceiptRuns}?ledgerBookId={BookId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(UiApiRoutes.WorkstationBookReceiptRuns,
            new { ledgerBookId = BookId, asOfDate = Date })).StatusCode);
        queue.Verify(x => x.RunAsync(It.IsAny<BookReceiptQueryDto>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Direct_lending_authority_does_not_grant_ledger_receipt_authority()
    {
        var queue = new Mock<IBookReceiptQueueService>(MockBehavior.Strict);
        await using var app = await AppAsync(queue.Object, permission: UserPermission.ManageDirectLending);
        var client = app.GetTestClient();
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"{UiApiRoutes.WorkstationBookReceiptRuns}?ledgerBookId={BookId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(UiApiRoutes.WorkstationBookReceiptRuns,
            new { ledgerBookId = BookId, asOfDate = Date })).StatusCode);
        queue.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Missing_book_authority_is_unavailable_and_not_an_empty_queue()
    {
        var queue = new Mock<IBookReceiptQueueService>(MockBehavior.Strict);
        await using var app = await AppAsync(queue.Object, bookAuthority: false);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await app.GetTestClient().GetAsync(
            $"{UiApiRoutes.WorkstationBookReceiptRuns}?ledgerBookId={BookId}")).StatusCode);
        queue.VerifyNoOtherCalls();
    }

    private static async Task<WebApplication> AppAsync(IBookReceiptQueueService queue,
        UserPermission permission = UserPermission.ManageLedgerReports, bool scopedGrant = true,
        string ownerTenant = "tenant-a", string ownerCompany = "company-a", bool bookAuthority = true)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(queue);
        builder.Services.AddRateLimiter(options => options.AddFixedWindowLimiter(UiEndpoints.MutationRateLimitPolicy,
            limiter => { limiter.PermitLimit = 100; limiter.Window = TimeSpan.FromMinutes(1); }));
        var authorization = new Mock<IScopedAuthorizationService>();
        authorization.Setup(x => x.AuthorizeAsync(It.IsAny<string>(), It.IsAny<UserPermission>(), It.IsAny<AccessScopeKindDto>(),
            It.IsAny<Guid?>(), It.IsAny<UserPermission>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string actor, UserPermission required, AccessScopeKindDto kind, Guid? id,
                UserPermission _, CancellationToken _) => new ScopedAuthorizationDecisionDto(
                    scopedGrant && kind == AccessScopeKindDto.LegalEntity && id == EntityId,
                    actor, required, kind, id, "Synthetic scoped grant"));
        builder.Services.AddSingleton(authorization.Object);
        if (bookAuthority)
        {
            var journals = new Mock<ILedgerJournalStore>();
            journals.Setup(x => x.GetLedgerBookAsync(BookId, It.IsAny<CancellationToken>())).ReturnsAsync(new LedgerBookRecord(
                BookId, "fund-a", EntityId, FundStructureNodeKindDto.Entity, "Synthetic book", "USD", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
            var owners = new Mock<IFundProfileTenancyRegistry>();
            owners.Setup(x => x.ResolveAsync("fund-a", It.IsAny<CancellationToken>())).ReturnsAsync(new FundProfileOwnership("fund-a", ownerTenant, ownerCompany));
            builder.Services.AddSingleton(journals.Object);
            builder.Services.AddSingleton(owners.Object);
        }
        var app = builder.Build();
        app.UseRouting();
        app.Use(async (http, next) =>
        {
            http.Items[LoginSessionMiddleware.CurrentUserKey] = "operator-a";
            http.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] = permission;
            http.Items[LoginSessionMiddleware.CurrentTenantIdKey] = Scope.TenantId;
            http.Items[LoginSessionMiddleware.CurrentUserCompanyIdKey] = Scope.CompanyId;
            await next(http);
        });
        app.UseRateLimiter();
        var group = app.MapGroup("/api/workstation");
        typeof(WorkstationEndpoints).GetMethod("MapBookReceiptQueueEndpoints", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, [group, new JsonSerializerOptions(JsonSerializerDefaults.Web)]);
        await app.StartAsync();
        return app;
    }
}

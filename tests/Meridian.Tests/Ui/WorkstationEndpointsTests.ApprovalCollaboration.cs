using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Ui.Shared.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection.Extensions;
using FluentAssertions;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Tenancy;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.OperationsContinuity;
using Meridian.Identity;
using Meridian.Identity.Auth;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NSubstitute;

namespace Meridian.Tests.Ui;

public sealed class OperationsApprovalCollaborationEndpointTests
{
    [Theory]
    [InlineData("foreign-tenant")]
    [InlineData("foreign-company")]
    [InlineData("wrong-book-id")]
    [InlineData("wrong-node-kind")]
    [InlineData("wrong-account")]
    public async Task ApprovalCollaboration_RejectsUnownedOrAmbiguousWorkflowBook(string variation)
    {
        var bookId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var books = new Mock<ILedgerBookService>();
        books.Setup(service => service.GetBookAsync(bookId, It.IsAny<CancellationToken>())).ReturnsAsync(new LedgerBookDto(
            variation == "wrong-book-id" ? Guid.NewGuid() : bookId, "test-fund-profile",
            variation == "wrong-account" ? Guid.NewGuid() : accountId,
            variation == "wrong-node-kind" ? FundStructureNodeKindDto.Entity : FundStructureNodeKindDto.Account,
            "Owned book", "USD", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
        await using var app = await CreateAppAsync(services =>
        {
            RegisterOperationsContinuityServices(services);
            services.AddSingleton(books.Object);
            if (variation is "foreign-tenant" or "foreign-company")
                services.AddSingleton<IFundProfileTenancyRegistry>(
                new TestCurrentScopeFundProfileTenancyRegistry(variation == "foreign-tenant" ? "other-tenant" : "tenant-test",
                    variation == "foreign-company" ? "other-company" : "tenant-test"));
        }, currentUserPermissions: UserPermission.ManageDirectLending);
        var service = app.Services.GetRequiredService<IOperationsContinuityWorkflowService>();
        var started = await service.StartWorkflowAsync(new(accountId, "2026-05", null, "synthetic", "ops-user", LedgerBookId: bookId));
        started.Success.Should().BeTrue();
        var workflow = started.Workflow!;
        var client = app.GetTestClient();
        (await client.GetAsync($"/api/workstation/operations/continuity/{workflow.WorkflowId}/approval/collaboration")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.PostAsJsonAsync($"/api/workstation/operations/continuity/{workflow.WorkflowId}/approval/comments",
            new OperationsApprovalCommentRequestDto(workflow.Version, "approval", "command", "forged-actor", "Review source."))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await service.GetApprovalCollaborationAsync(workflow.WorkflowId))!.Comments.Should().BeEmpty();
        (await service.GetAsync(workflow.WorkflowId))!.Version.Should().Be(workflow.Version);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ApprovalCollaboration_RejectsDisabledMentionOrReviewer(bool reassignment)
    {
        var bookId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var accounts = Substitute.For<IUserAccountStore>();
        accounts.HasAccounts.Returns(true);
        // An account summary can be disabled after an older profile projection was built.
        accounts.LoadAccounts().Returns([new UserAccountConfig("disabled-reviewer", PasswordHashing.HashPassword("synthetic-test-password"), UserRole.Admin, CompanyId: "tenant-test")]);
        accounts.GetAccountsAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<UserAccountDto>>([
            new("disabled-reviewer", "Admin", null, [], 0, true, false, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "admin", "admin", CompanyId: "tenant-test")
        ]));
        await using var app = await CreateAppAsync(services =>
        {
            RegisterOperationsContinuityServices(services);
            RegisterRetainedReportBook(services, bookId, accountId);
            services.AddSingleton(accounts);
            services.AddSingleton(new UserProfileRegistry(null, accounts));
        }, currentUserPermissions: UserPermission.ManageDirectLending);
        var service = app.Services.GetRequiredService<IOperationsContinuityWorkflowService>();
        var started = await service.StartWorkflowAsync(new(accountId, "2026-05", null, "synthetic", "ops-user", LedgerBookId: bookId));
        var workflow = started.Workflow!;
        var client = app.GetTestClient();
        using var response = reassignment
            ? await client.PostAsJsonAsync($"/api/workstation/operations/continuity/{workflow.WorkflowId}/approval/reviewer", new OperationsApprovalReassignRequestDto(workflow.Version, "approval", "command", "forged", "disabled-reviewer", "Desk coverage"))
            : await client.PostAsJsonAsync($"/api/workstation/operations/continuity/{workflow.WorkflowId}/approval/comments", new OperationsApprovalCommentRequestDto(workflow.Version, "approval", "command", "forged", "Review source", ["disabled-reviewer"]));
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain(reassignment ? "approval-capable reviewer" : "active");
        (await service.GetAsync(workflow.WorkflowId))!.Version.Should().Be(workflow.Version);
    }
    private static async Task<WebApplication> CreateAppAsync(Action<IServiceCollection> configureServices,
        UserPermission currentUserPermissions = UserPermission.ManageDirectLending)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        configureServices(builder.Services);
        builder.Services.TryAddSingleton<IFundProfileTenancyRegistry>(new TestCurrentScopeFundProfileTenancyRegistry("tenant-test", "tenant-test"));
        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            context.Items[LoginSessionMiddleware.CurrentUserKey] = "ops-user";
            context.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] = currentUserPermissions;
            context.Items[LoginSessionMiddleware.CurrentUserCompanyIdKey] = "tenant-test";
            context.Items[LoginSessionMiddleware.CurrentTenantIdKey] = "tenant-test";
            await next();
        });
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
        // Exercise this production endpoint group and its filters without mounting unrelated hosts.
        typeof(WorkstationEndpoints).GetMethod("MapApprovalCollaborationEndpoints", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, [app.MapGroup("/api/workstation"), options]);
        await app.StartAsync();
        return app;
    }

    private static void RegisterOperationsContinuityServices(IServiceCollection services)
    {
        services.AddSingleton<IOperationsStatusDerivationService, OperationsStatusDerivationService>();
        services.AddSingleton<IOperationsContinuityRepository, InMemoryOperationsContinuityRepository>();
        services.AddSingleton<IOperationsWorkflowAuditStore, InMemoryOperationsWorkflowAuditStore>();
        services.AddSingleton<IOperationsContinuityWorkflowService, OperationsContinuityWorkflowService>();
    }

    private static void RegisterRetainedReportBook(IServiceCollection services, Guid bookId, Guid accountId)
    {
        var books = new Mock<ILedgerBookService>();
        books.Setup(service => service.GetBookAsync(bookId, It.IsAny<CancellationToken>())).ReturnsAsync(
            new LedgerBookDto(bookId, "test-fund-profile", accountId, FundStructureNodeKindDto.Account, "Primary", "USD", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
        services.AddSingleton(books.Object);
    }

    private sealed class TestCurrentScopeFundProfileTenancyRegistry(string tenantId, string companyId) : IFundProfileTenancyRegistry
    {
        public Task<FundProfileOwnership> BindAsync(string fundProfileId, string requestedTenantId, string? requestedCompanyId = null, CancellationToken ct = default)
            => Task.FromResult(new FundProfileOwnership(fundProfileId, tenantId, companyId));
        public Task<FundProfileOwnership?> ResolveAsync(string fundProfileId, CancellationToken ct = default)
            => Task.FromResult<FundProfileOwnership?>(new(fundProfileId, tenantId, companyId));
        public Task<bool> IsAccessibleAsync(string fundProfileId, string requestedTenantId, string? requestedCompanyId = null, CancellationToken ct = default)
            => Task.FromResult(tenantId == requestedTenantId && companyId == requestedCompanyId);
    }

}

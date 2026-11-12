using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Services;
using Meridian.Contracts.Tenancy;
using Meridian.Identity.Auth;
using Meridian.Storage.Ledger;
using Meridian.Ui.Shared.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;

namespace Meridian.Tests.Ui;

public sealed class IntercompanyAgreementEndpointTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Create_DerivesTenantAndActorFromSession_NotBodyFields()
    {
        var command = Command();
        var service = new Mock<IIntercompanyAgreementService>();
        IntercompanyAgreementCallContext? actual = null;
        service.Setup(s => s.CreateAsync(It.IsAny<IntercompanyAgreementCallContext>(), command, It.IsAny<CancellationToken>()))
            .Callback<IntercompanyAgreementCallContext, IntercompanyAgreementCommandDto, CancellationToken>((context, _, _) => actual = context)
            .ReturnsAsync(Result(command));
        await using var app = await CreateAppAsync(service.Object);
        var body = JsonSerializer.SerializeToElement(command, JsonOptions).EnumerateObject()
            .ToDictionary(item => item.Name, item => (object?)item.Value);
        body["tenantId"] = "foreign-tenant";
        body["actor"] = "forged-approver";

        var response = await app.GetTestClient().PostAsJsonAsync("/api/fund-structure/intercompany-agreements", body);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        actual.Should().Be(new IntercompanyAgreementCallContext("tenant-a", "operator-a"));
    }

    [Fact]
    public async Task MissingTenantAndCompany_DeniesBeforeInvokingRegistry()
    {
        var service = new Mock<IIntercompanyAgreementService>(MockBehavior.Strict);
        await using var app = await CreateAppAsync(service.Object, includeScope: false);

        var response = await app.GetTestClient().GetAsync("/api/fund-structure/intercompany-agreements");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task UnconfiguredRegistry_ReturnsUnavailable_WithoutLocalFallback()
    {
        await using var app = await CreateAppAsync(null);

        var response = await app.GetTestClient().GetAsync("/api/fund-structure/intercompany-agreements");

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task ReadOnlyPermission_MasksMutationCapabilities_AndCannotCreate()
    {
        var command = Command();
        var service = new Mock<IIntercompanyAgreementService>();
        service.Setup(s => s.ListAsync(It.IsAny<IntercompanyAgreementCallContext>(), It.IsAny<IntercompanyAgreementQueryDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IntercompanyAgreementListDto([Detail(command.AgreementId)], null) { CanCreate = true });
        await using var app = await CreateAppAsync(service.Object, permissions: UserPermission.ManageDirectLending);

        var response = await app.GetTestClient().GetAsync("/api/fund-structure/intercompany-agreements");
        var list = await response.Content.ReadFromJsonAsync<IntercompanyAgreementListDto>(JsonOptions);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        list!.CanCreate.Should().BeFalse();
        list.ReferencePostingSupported.Should().BeFalse();
        list.Items.Single().Capabilities.CanApprove.Should().BeFalse();
        list.Items.Single().Capabilities.CanEdit.Should().BeFalse();
        (await app.GetTestClient().PostAsJsonAsync("/api/fund-structure/intercompany-agreements", command))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task RouteBodyIdentityMismatch_IsRejectedBeforeChangingAnotherAgreement()
    {
        var service = new Mock<IIntercompanyAgreementService>(MockBehavior.Strict);
        var command = Command();
        await using var app = await CreateAppAsync(service.Object);

        var response = await app.GetTestClient().PostAsJsonAsync(
            $"/api/fund-structure/intercompany-agreements/{Guid.NewGuid()}/proposals/{command.ProposalId}/submit", command);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task JournalCoverage_RequiresLedgerPermissionInAdditionToAgreementAccess()
    {
        var service = new Mock<IIntercompanyAgreementService>(MockBehavior.Strict);
        await using var app = await CreateAppAsync(service.Object);

        var response = await app.GetTestClient().GetAsync(
            $"/api/fund-structure/intercompany-agreements/{Guid.NewGuid()}/journal-lines?ledgerBookId={Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task JournalCoverage_ForeignBookOwnershipLooksUnknown_AndDoesNotQuerySourceLines()
    {
        var detail = Detail(Guid.NewGuid());
        var bookId = Guid.NewGuid();
        var service = new Mock<IIntercompanyAgreementService>();
        service.Setup(s => s.GetAsync(It.IsAny<IntercompanyAgreementCallContext>(), detail.AgreementId,
            It.IsAny<DateOnly?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>())).ReturnsAsync(detail);
        var ledger = new Mock<ILedgerJournalStore>(MockBehavior.Strict);
        ledger.Setup(store => store.GetLedgerBookAsync(bookId, It.IsAny<CancellationToken>())).ReturnsAsync(
            new LedgerBookRecord(bookId, "foreign-fund", detail.PartyOneId, FundStructureNodeKindDto.Entity,
                "Foreign book", "USD", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
        var tenancy = new Mock<IFundProfileTenancyRegistry>();
        tenancy.Setup(registry => registry.ResolveAsync("foreign-fund", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FundProfileOwnership("foreign-fund", "foreign-tenant", "foreign-company"));
        await using var app = await CreateAppAsync(service.Object, permissions: UserPermission.AdminMaintenance,
            configure: services => { services.AddSingleton(ledger.Object); services.AddSingleton(tenancy.Object); });

        var response = await app.GetTestClient().GetAsync(
            $"/api/fund-structure/intercompany-agreements/{detail.AgreementId}/journal-lines?ledgerBookId={bookId}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        ledger.Verify(store => store.QueryAsync(It.IsAny<LedgerJournalEntryQuery>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("approve")]
    [InlineData("APPROVE")]
    [InlineData("approve/")]
    public async Task ApiKeyPrincipal_CannotApproveEvenWithManagementPermission(string action)
    {
        var service = new Mock<IIntercompanyAgreementService>(MockBehavior.Strict);
        var command = Command();
        await using var app = await CreateAppAsync(service.Object, apiKey: true);

        var response = await app.GetTestClient().PostAsJsonAsync(
            $"/api/fund-structure/intercompany-agreements/{command.AgreementId}/proposals/{command.ProposalId}/{action}", command);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        service.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("missing", HttpStatusCode.NotFound)]
    [InlineData("conflict", HttpStatusCode.Conflict)]
    [InlineData("forbidden", HttpStatusCode.Forbidden)]
    [InlineData("validation", HttpStatusCode.BadRequest)]
    public async Task DomainFailures_UseStableHttpStatus(string failure, HttpStatusCode expected)
    {
        Exception exception = failure switch
        {
            "missing" => new IntercompanyAgreementNotFoundException(),
            "conflict" => new IntercompanyAgreementConflictException("Changed version"),
            "forbidden" => new IntercompanyAgreementForbiddenException("Independent reviewer required"),
            _ => new IntercompanyAgreementValidationException("Invalid terms")
        };
        var service = new Mock<IIntercompanyAgreementService>();
        service.Setup(s => s.CreateAsync(It.IsAny<IntercompanyAgreementCallContext>(), It.IsAny<IntercompanyAgreementCommandDto>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(exception);
        await using var app = await CreateAppAsync(service.Object);

        var response = await app.GetTestClient().PostAsJsonAsync("/api/fund-structure/intercompany-agreements", Command());

        response.StatusCode.Should().Be(expected);
    }

    private static IntercompanyAgreementCommandDto Command() => new(Guid.NewGuid(), Guid.NewGuid(), 0, Guid.NewGuid());

    private static IntercompanyAgreementDetailDto Detail(Guid id) => new(id, 1, "IC-1", "Synthetic agreement",
        Guid.NewGuid(), "Lender", Guid.NewGuid(), "Borrower", IntercompanyAgreementTypeDto.Loan, "USD",
        new DateOnly(2026, 1, 1), IntercompanyAgreementStatusDto.Unapproved, null, null, [], [], [],
        new(true, true, true, true, true, true, true));

    private static IntercompanyAgreementCommandResultDto Result(IntercompanyAgreementCommandDto command)
    {
        var detail = Detail(command.AgreementId);
        return new(detail, new(command.CommandId, "Create", "synthetic-hash", "operator-a", DateTimeOffset.UtcNow, 1, detail));
    }

    private static async Task<WebApplication> CreateAppAsync(IIntercompanyAgreementService? service,
        bool includeScope = true, bool apiKey = false, UserPermission permissions = UserPermission.ManageFundStructure,
        Action<IServiceCollection>? configure = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
        builder.WebHost.UseTestServer();
        if (service is not null) builder.Services.AddSingleton(service);
        configure?.Invoke(builder.Services);
        var app = builder.Build();
        app.Use(async (http, next) =>
        {
            http.Items[LoginSessionMiddleware.CurrentUserKey] = "operator-a";
            http.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] = permissions;
            if (includeScope)
            {
                http.Items[LoginSessionMiddleware.CurrentTenantIdKey] = "tenant-a";
                http.Items[LoginSessionMiddleware.CurrentUserCompanyIdKey] = "company-a";
            }
            if (apiKey) http.Items["CurrentUserIsApiKey"] = true;
            await next(http);
        });
        app.MapIntercompanyAgreementEndpoints(JsonOptions);
        await app.StartAsync();
        return app;
    }
}

using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Tenancy;
using Meridian.Contracts.Workstation;
using Meridian.Identity;
using Meridian.Identity.Auth;
using Meridian.Storage.FundStructure;
using Meridian.Storage.Ledger;
using Meridian.Ui.Shared.Endpoints;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;

namespace Meridian.Tests.Ui;

public sealed class IntercompanyAgreementWorkQueueEndpointTests
{
    private static readonly Guid BookId = Guid.Parse("cc7b22a5-42a0-4d80-a00a-f2ace1e84152");
    private static readonly Guid EntityId = Guid.Parse("91c218a9-7b4e-4166-9bb2-89ba6665c568");
    private static readonly DateOnly AsOf = new(2026, 11, 23);
    private static readonly DateTimeOffset Now = new(2026, 11, 23, 15, 0, 0, TimeSpan.Zero);
    private const string Route = "/api/ledger/intercompany/work-queue";
    private static readonly string Query = $"?ledgerBookId={BookId:D}&asOf=2026-11-23";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Theory]
    [InlineData(UserPermission.ViewLedgerReports, false)]
    [InlineData(UserPermission.ManageLedgerReports, true)]
    [InlineData(UserPermission.AdminMaintenance, true)]
    public async Task ReadDerivesAuthenticatedScopeAndProjectsManagementCapability(UserPermission permissions, bool canManage)
    {
        var service = new Mock<IIntercompanyAgreementWorkQueueService>();
        IntercompanyAgreementCallContext? actualContext = null;
        string? actualCompany = null;
        IntercompanyObligationMonitorRequestDto? actualScope = null;
        IHttpContextAccessor? accessor = null;
        service.Setup(x => x.GetAsync(It.IsAny<IntercompanyAgreementCallContext>(), It.IsAny<string>(),
                It.IsAny<IntercompanyObligationMonitorRequestDto>(), It.IsAny<CancellationToken>()))
            .Returns((IntercompanyAgreementCallContext context, string? company, IntercompanyObligationMonitorRequestDto scope, CancellationToken _) =>
            {
                (actualContext, actualCompany, actualScope) = (context, company, scope);
                var required = Assert.IsAssignableFrom<IReadOnlyCollection<UserPermission>>(
                    accessor!.HttpContext!.Items[HttpContextIntercompanyAgreementAccessAuthority.RequiredPermissionKey]);
                Assert.Equal(new[] { UserPermission.ViewLedgerReports, UserPermission.ManageLedgerReports }, required);
                return Task.FromResult(Queue() with { CanManage = !canManage });
            });
        await using var app = await AppAsync(service.Object, permissions);
        accessor = app.Services.GetRequiredService<IHttpContextAccessor>();

        var response = await app.GetTestClient().GetAsync(Route + Query + "&tenantId=forged&actor=forged");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new("tenant-a", "operator-a"), actualContext);
        Assert.Equal("company-a", actualCompany);
        Assert.Equal(new("fund-a", BookId, AsOf, 30), actualScope);
        var result = await response.Content.ReadFromJsonAsync<IntercompanyAgreementWorkQueueDto>(Json);
        Assert.Equal(canManage, result!.CanManage);
        service.Verify(x => x.ObserveAsync(It.IsAny<IntercompanyAgreementCallContext>(), It.IsAny<string>(),
            It.IsAny<IntercompanyObligationMonitorRequestDto>(), It.IsAny<IntercompanyAgreementWorkQueueObserveRequestDto>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("observe")]
    [InlineData("update")]
    public async Task ReadOnlyUserCannotMutateHistory(string operation)
    {
        var service = new Mock<IIntercompanyAgreementWorkQueueService>(MockBehavior.Strict);
        await using var app = await AppAsync(service.Object, UserPermission.ViewLedgerReports);
        var response = await app.GetTestClient().PostAsync(Route + "/" + operation + Query,
            new StringContent("{}", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ManageGrantOnOnePartyCannotCombineWithReadGrantOnTheOtherForMutation()
    {
        var otherParty = Guid.NewGuid();
        var permissions = UserPermission.ViewLedgerReports | UserPermission.ManageLedgerReports;
        var scoped = new Mock<IScopedAuthorizationService>();
        scoped.Setup(x => x.AuthorizeManyAsync("operator-a", It.IsAny<UserPermission>(), AccessScopeKindDto.LegalEntity,
                It.IsAny<IReadOnlyCollection<Guid>>(), permissions, It.IsAny<CancellationToken>()))
            .ReturnsAsync((string actor, UserPermission required, AccessScopeKindDto kind,
                IReadOnlyCollection<Guid> ids, UserPermission _, CancellationToken _) =>
                (IReadOnlyDictionary<Guid, ScopedAuthorizationDecisionDto>)ids.ToDictionary(id => id,
                    id => new ScopedAuthorizationDecisionDto(required == UserPermission.ViewLedgerReports || id == EntityId,
                        actor, required, kind, id, "Synthetic scoped grant")));
        var structure = new Mock<IFundStructureStore>();
        structure.Setup(x => x.GetNodeTenantsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new FundStructureTenantMap(
            true, new Dictionary<Guid, string> { [EntityId] = "tenant-a", [otherParty] = "tenant-a" }));
        IHttpContextAccessor? accessor = null;
        async Task CheckAsync(IntercompanyAgreementCallContext context)
        {
            var access = new HttpContextIntercompanyAgreementAccessAuthority(accessor!, structure.Object);
            if (!await access.CanAccessEntitiesAsync(context, [EntityId, otherParty]))
                throw new IntercompanyAgreementForbiddenException("The parties require current operation-specific grants.");
        }
        var service = new Mock<IIntercompanyAgreementWorkQueueService>();
        service.Setup(x => x.GetAsync(It.IsAny<IntercompanyAgreementCallContext>(), It.IsAny<string>(),
                It.IsAny<IntercompanyObligationMonitorRequestDto>(), It.IsAny<CancellationToken>()))
            .Returns(async (IntercompanyAgreementCallContext context, string? _, IntercompanyObligationMonitorRequestDto _, CancellationToken _) =>
            { await CheckAsync(context); return Queue(); });
        service.Setup(x => x.ObserveAsync(It.IsAny<IntercompanyAgreementCallContext>(), It.IsAny<string>(),
                It.IsAny<IntercompanyObligationMonitorRequestDto>(), It.IsAny<IntercompanyAgreementWorkQueueObserveRequestDto>(),
                It.IsAny<CancellationToken>()))
            .Returns(async (IntercompanyAgreementCallContext context, string? _, IntercompanyObligationMonitorRequestDto _,
                IntercompanyAgreementWorkQueueObserveRequestDto _, CancellationToken _) =>
            { await CheckAsync(context); return Item(); });
        service.Setup(x => x.UpdateAsync(It.IsAny<IntercompanyAgreementCallContext>(), It.IsAny<string>(),
                It.IsAny<IntercompanyObligationMonitorRequestDto>(), It.IsAny<IntercompanyAgreementWorkQueueUpdateRequestDto>(),
                It.IsAny<CancellationToken>()))
            .Returns(async (IntercompanyAgreementCallContext context, string? _, IntercompanyObligationMonitorRequestDto _,
                IntercompanyAgreementWorkQueueUpdateRequestDto _, CancellationToken _) =>
            { await CheckAsync(context); return Item(); });
        await using var app = await AppAsync(service.Object, permissions,
            configure: services => services.AddSingleton(scoped.Object));
        accessor = app.Services.GetRequiredService<IHttpContextAccessor>();
        var client = app.GetTestClient();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(Route + Query)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(Route + "/observe" + Query, Observe(), Json)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(Route + "/update" + Query, Update(), Json)).StatusCode);
    }

    [Theory]
    [InlineData("company-b", "tenant-a")]
    [InlineData("company-a", "tenant-b")]
    public async Task ForeignBookIsDeniedBeforeHistoryReadOrMutation(string ownerCompany, string ownerTenant)
    {
        var service = new Mock<IIntercompanyAgreementWorkQueueService>(MockBehavior.Strict);
        await using var app = await AppAsync(service.Object, ownerCompany: ownerCompany, ownerTenant: ownerTenant);
        var client = app.GetTestClient();
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(Route + Query)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync(Route + "/observe" + Query,
            JsonContent.Create(Observe()))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync(Route + "/update" + Query,
            JsonContent.Create(Update()))).StatusCode);
        service.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("?asOf=2026-11-23")]
    [InlineData("?ledgerBookId=cc7b22a5-42a0-4d80-a00a-f2ace1e84152")]
    [InlineData("?ledgerBookId=cc7b22a5-42a0-4d80-a00a-f2ace1e84152&asOf=invalid")]
    [InlineData("?ledgerBookId=cc7b22a5-42a0-4d80-a00a-f2ace1e84152&asOf=0001-01-01")]
    [InlineData("?ledgerBookId=cc7b22a5-42a0-4d80-a00a-f2ace1e84152&asOf=2026-11-23&expiryHorizonDays=-1")]
    [InlineData("?ledgerBookId=cc7b22a5-42a0-4d80-a00a-f2ace1e84152&asOf=2026-11-23&expiryHorizonDays=3661")]
    public async Task InvalidObservationScopeIsRejectedBeforeService(string query)
    {
        var service = new Mock<IIntercompanyAgreementWorkQueueService>(MockBehavior.Strict);
        await using var app = await AppAsync(service.Object);
        Assert.Equal(HttpStatusCode.BadRequest, (await app.GetTestClient().GetAsync(Route + query)).StatusCode);
        service.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("&fundProfileId=another-fund")]
    [InlineData("&entityId=11111111-2222-3333-4444-555555555555")]
    public async Task MismatchedFundOrEntityAssertionIsDenied(string assertion)
    {
        var service = new Mock<IIntercompanyAgreementWorkQueueService>(MockBehavior.Strict);
        await using var app = await AppAsync(service.Object);
        Assert.Equal(HttpStatusCode.Forbidden, (await app.GetTestClient().GetAsync(Route + Query + assertion)).StatusCode);
        service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ObserveAndUpdateUseAuthenticatedActorAndPreserveExpectedVersions()
    {
        var service = new Mock<IIntercompanyAgreementWorkQueueService>();
        var observe = Observe();
        var update = Update();
        service.Setup(x => x.ObserveAsync(new("tenant-a", "operator-a"), "company-a",
                new("fund-a", BookId, AsOf, 30), observe, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Item());
        service.Setup(x => x.UpdateAsync(new("tenant-a", "operator-a"), "company-a",
                new("fund-a", BookId, AsOf, 30), It.Is<IntercompanyAgreementWorkQueueUpdateRequestDto>(r =>
                    r.CommandId == update.CommandId && r.CaseId == update.CaseId && r.ExpectedVersion == 7
                    && r.ExpectedRecordHash == "expected-record" && r.ExpectedSourceFingerprint == "expected-source"
                    && r.Action == IntercompanyAgreementWorkItemActionDto.Resolve && r.Reason == "Reviewed amendment"
                    && r.EvidenceReferences!.Single() == "agreement:approved-revision-2"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Item() with { State = IntercompanyAgreementWorkItemStateDto.Resolved });
        await using var app = await AppAsync(service.Object);
        var client = app.GetTestClient();
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync(Route + "/observe" + Query, observe, Json)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync(Route + "/update" + Query, update, Json)).StatusCode);
        service.VerifyAll();
    }

    [Fact]
    public async Task StaleCaseIsAConflictAndUnavailableHistoryDoesNotLeakProviderDetails()
    {
        var service = new Mock<IIntercompanyAgreementWorkQueueService>();
        service.Setup(x => x.UpdateAsync(It.IsAny<IntercompanyAgreementCallContext>(), It.IsAny<string>(),
                It.IsAny<IntercompanyObligationMonitorRequestDto>(), It.IsAny<IntercompanyAgreementWorkQueueUpdateRequestDto>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IntercompanyAgreementWorkQueueConflictException("Refresh the case before updating it."));
        service.Setup(x => x.GetAsync(It.IsAny<IntercompanyAgreementCallContext>(), It.IsAny<string>(),
                It.IsAny<IntercompanyObligationMonitorRequestDto>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IntercompanyAgreementWorkQueueUnavailableException("private-path-and-provider-detail"));
        await using var app = await AppAsync(service.Object);
        var client = app.GetTestClient();
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync(Route + "/update" + Query, Update(), Json)).StatusCode);
        var unavailable = await client.GetAsync(Route + Query);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, unavailable.StatusCode);
        Assert.DoesNotContain("private-path-and-provider-detail", await unavailable.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task GuaranteeCapRemainsExactAndUnknownExposureRemainsNull()
    {
        var service = new Mock<IIntercompanyAgreementWorkQueueService>();
        var guarantee = new IntercompanyGuaranteeDisclosureDto(Guid.NewGuid(), "G-1", "Synthetic guarantee", 2,
            new string('a', 64), EntityId, "Guarantor", Guid.NewGuid(), "Obligor", "USD", "Synthetic beneficiary",
            12345678901234567890.123456789m, new(2026, 1, 1), new(2027, 1, 1), null,
            IntercompanyAgreementStatusDto.Active, null, "Unavailable", "No reviewed exposure source.");
        service.Setup(x => x.GetAsync(It.IsAny<IntercompanyAgreementCallContext>(), It.IsAny<string>(),
                It.IsAny<IntercompanyObligationMonitorRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Queue() with { Guarantees = [guarantee] });
        await using var app = await AppAsync(service.Object);
        using var document = JsonDocument.Parse(await app.GetTestClient().GetStringAsync(Route + Query));
        var row = document.RootElement.GetProperty("guarantees")[0];
        Assert.Equal("12345678901234567890.123456789", row.GetProperty("limitAmount").GetString());
        Assert.Equal(JsonValueKind.Null, row.GetProperty("currentExposure").ValueKind);
        Assert.Equal("Unavailable", row.GetProperty("exposureStatus").GetString());
    }

    [Fact]
    public async Task MissingScopeOrAuthorityAndMalformedCommandFailClosed()
    {
        await using (var missing = await AppAsync(null))
            Assert.Equal(HttpStatusCode.ServiceUnavailable, (await missing.GetTestClient().GetAsync(Route + Query)).StatusCode);
        var service = new Mock<IIntercompanyAgreementWorkQueueService>(MockBehavior.Strict);
        await using (var unscoped = await AppAsync(service.Object, includeScope: false))
            Assert.Equal(HttpStatusCode.Forbidden, (await unscoped.GetTestClient().GetAsync(Route + Query)).StatusCode);
        await using var app = await AppAsync(service.Object);
        Assert.Equal(HttpStatusCode.BadRequest, (await app.GetTestClient().PostAsync(Route + "/update" + Query,
            new StringContent("{\"action\":\"ApproveAndPost\"}", Encoding.UTF8, "application/json"))).StatusCode);
        service.VerifyNoOtherCalls();
    }

    private static IntercompanyAgreementWorkQueueObserveRequestDto Observe() =>
        new(Guid.NewGuid(), "case-1", "expected-source", "Track the upcoming expiry");

    private static IntercompanyAgreementWorkQueueUpdateRequestDto Update() =>
        new(Guid.NewGuid(), "case-1", IntercompanyAgreementWorkItemActionDto.Resolve, 7,
            "expected-record", "expected-source", "Reviewed amendment", EvidenceReferences: ["agreement:approved-revision-2"]);

    private static IntercompanyAgreementWorkQueueDto Queue() =>
        new(AsOf, 30, Now, "Current", true, "expected-source", [], [], []) { LedgerBookId = BookId, FundProfileId = "fund-a" };

    private static IntercompanyAgreementWorkItemDto Item() =>
        new("case-1", BookId, Guid.NewGuid(), 1, "intercompany.agreement.expiring", "Warning", "Expiry approaching",
            "Review an amendment", new(2026, 12, 1), AsOf, true, true, IntercompanyAgreementWorkItemStateDto.Open,
            null, Now, Now, 0, 0, "expected-source", 1, "record-hash", []);

    private static async Task<WebApplication> AppAsync(IIntercompanyAgreementWorkQueueService? service,
        UserPermission permissions = UserPermission.ManageLedgerReports, string ownerCompany = "company-a",
        string ownerTenant = "tenant-a", bool includeScope = true, Action<IServiceCollection>? configure = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
        builder.WebHost.UseTestServer();
        builder.Services.AddHttpContextAccessor();
        if (service is not null)
            builder.Services.AddSingleton(service);
        var journals = new Mock<ILedgerJournalStore>();
        journals.Setup(x => x.GetLedgerBookAsync(BookId, It.IsAny<CancellationToken>())).ReturnsAsync(new LedgerBookRecord(
            BookId, "fund-a", EntityId, FundStructureNodeKindDto.Entity, "Synthetic book", "USD", Now, Now));
        var owners = new Mock<IFundProfileTenancyRegistry>();
        owners.Setup(x => x.ResolveAsync("fund-a", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FundProfileOwnership("fund-a", ownerTenant, ownerCompany));
        builder.Services.AddSingleton(journals.Object);
        builder.Services.AddSingleton(owners.Object);
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
            await next(http);
        });
        app.MapIntercompanyAgreementWorkQueueEndpoints(Json);
        await app.StartAsync();
        return app;
    }
}

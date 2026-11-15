using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Services;
using Meridian.Contracts.Tenancy;
using Meridian.Contracts.Workstation;
using Meridian.Identity;
using Meridian.Identity.Auth;
using Meridian.Storage.FundStructure;
using Meridian.Storage.Ledger;
using Meridian.Ui.Shared.Endpoints;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace Meridian.Tests.Ui;

public sealed class IntercompanyCloseAdvisoryAuthorizationTests
{
    [Fact]
    public async Task CloseReaderWithoutLedgerPermissionNeverReadsAccountingAdvisories()
    {
        using var fixture = new Fixture(UserPermission.ViewSecurityMaster | UserPermission.ManageCompliance);
        var result = await fixture.Adapter.GetAsync(fixture.Scope, Fixture.Tenant, Fixture.Company);
        Assert.Equal("Unavailable", result.Status);
        Assert.Null(result.BalanceSchedule);
        Assert.Empty(result.Guarantees);
        Assert.DoesNotContain("Sensitive", Assert.Single(result.Findings).Message);
        fixture.Monitor.VerifyNoOtherCalls();
        fixture.Journals.VerifyNoOtherCalls();
        fixture.Ownership.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ComplianceGrantCannotSubstituteForDeniedLedgerReportingScope()
    {
        using var fixture = new Fixture(UserPermission.ViewSecurityMaster | UserPermission.ManageCompliance | UserPermission.ViewLedgerReports);
        fixture.Grant = (permission, _) => permission == UserPermission.ManageCompliance;
        fixture.UseActualScopedAuthority();
        var result = await fixture.Adapter.GetAsync(fixture.Scope, Fixture.Tenant, Fixture.Company);
        Assert.Equal("Unavailable", result.Status);
        Assert.Null(result.BalanceSchedule);
        Assert.DoesNotContain(result.Findings, finding => finding.Message.Contains("Sensitive", StringComparison.Ordinal));
        fixture.Authorization.Verify(x => x.AuthorizeManyAsync(Fixture.Actor, UserPermission.ManageCompliance,
            AccessScopeKindDto.LegalEntity, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<UserPermission>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task EitherReportingPermissionCanAuthorizeAUserHoldingBothGlobalPermissions()
    {
        using var fixture = new Fixture(UserPermission.ViewSecurityMaster | UserPermission.ManageCompliance |
            UserPermission.ViewLedgerReports | UserPermission.ManageLedgerReports);
        fixture.Grant = (permission, _) => permission == UserPermission.ManageLedgerReports;
        fixture.Http.Items[HttpContextIntercompanyAgreementAccessAuthority.RequiredPermissionKey] = UserPermission.ManageCompliance;
        fixture.UseActualScopedAuthority();
        var result = await fixture.Adapter.GetAsync(fixture.Scope, Fixture.Tenant, Fixture.Company);
        Assert.Equal("Current", result.Status);
        Assert.Equal(fixture.Period.EndDate, result.AsOf);
        Assert.Null(result.BalanceSchedule); // The close summary never includes the accounting schedule.
        Assert.Contains(result.Findings, finding => finding.Code == "synthetic.expiring");
        Assert.Equal(new IntercompanyAgreementCallContext(Fixture.Tenant, Fixture.Actor), fixture.CapturedContext);
        Assert.Equal(Fixture.Company, fixture.CapturedCompany);
        Assert.Equal(fixture.Book.LedgerBookId, fixture.CapturedRequest!.LedgerBookId);
        Assert.Equal(UserPermission.ManageCompliance,
            fixture.Http.Items[HttpContextIntercompanyAgreementAccessAuthority.RequiredPermissionKey]);
    }

    [Fact]
    public async Task ReportingGrantsUnionAcrossBothPartiesWithoutUsingOtherPermissions()
    {
        using var fixture = new Fixture(UserPermission.ViewLedgerReports | UserPermission.ManageLedgerReports);
        fixture.Grant = (permission, entity) => permission == UserPermission.ViewLedgerReports && entity == fixture.EntityId
            || permission == UserPermission.ManageLedgerReports && entity == fixture.CounterpartyId;
        fixture.UseActualScopedAuthority();
        var result = await fixture.Adapter.GetAsync(fixture.Scope, Fixture.Tenant, Fixture.Company);
        Assert.Equal("Current", result.Status);
        Assert.False(fixture.Http.Items.ContainsKey(HttpContextIntercompanyAgreementAccessAuthority.RequiredPermissionKey));
    }

    [Fact]
    public async Task ReportingAdminOverrideStillCannotCrossTenantEntityOwnership()
    {
        using var fixture = new Fixture(UserPermission.AdminMaintenance);
        fixture.Structure.Setup(x => x.GetNodeTenantsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(
            new FundStructureTenantMap(true, new Dictionary<Guid, string>
            { [fixture.EntityId] = Fixture.Tenant, [fixture.CounterpartyId] = "another-tenant" }));
        fixture.UseActualScopedAuthority();
        var result = await fixture.Adapter.GetAsync(fixture.Scope, Fixture.Tenant, Fixture.Company);
        Assert.Equal("Unavailable", result.Status);
        Assert.Null(result.BalanceSchedule);
    }

    private sealed class Fixture : IDisposable
    {
        public const string Tenant = "tenant-a";
        public const string Company = "company-a";
        public const string Actor = "operator-a";
        public Guid EntityId { get; } = Guid.NewGuid();
        public Guid CounterpartyId { get; } = Guid.NewGuid();
        public DefaultHttpContext Http { get; } = new();
        public Mock<ILedgerJournalStore> Journals { get; } = new(MockBehavior.Strict);
        public Mock<IFundProfileTenancyRegistry> Ownership { get; } = new(MockBehavior.Strict);
        public Mock<IIntercompanyObligationMonitorService> Monitor { get; } = new(MockBehavior.Strict);
        public Mock<IFundStructureStore> Structure { get; } = new();
        public Mock<IScopedAuthorizationService> Authorization { get; } = new();
        public Func<UserPermission, Guid, bool> Grant { get; set; } = (_, _) => false;
        public LedgerBookRecord Book { get; }
        public LedgerAccountingPeriod Period { get; }
        public IntercompanyCloseAdvisorySource Adapter { get; }
        public HttpContextIntercompanyAgreementAccessAuthority Access { get; }
        public IntercompanyAgreementCallContext? CapturedContext { get; private set; }
        public string? CapturedCompany { get; private set; }
        public IntercompanyObligationMonitorRequestDto? CapturedRequest { get; private set; }
        private readonly ServiceProvider _services;
        public CloseReadinessScopeDto Scope => new(Book.FundProfileId, Book.LedgerBookId, Guid.NewGuid(),
            EntityId.ToString("D"), Period.PeriodId.ToString("D"));

        public Fixture(UserPermission permissions)
        {
            var now = DateTimeOffset.Parse("2026-11-13T00:00:00Z");
            Book = new(Guid.NewGuid(), "fund-a", EntityId, FundStructureNodeKindDto.Entity, "Synthetic source", "USD", now, now);
            Period = new(Guid.NewGuid(), Book.LedgerBookId, 2026, 11, "November", new(2026, 11, 1), new(2026, 11, 30), "Open", now, null, 1);
            var accessor = new HttpContextAccessor { HttpContext = Http };
            var services = new ServiceCollection();
            services.AddSingleton<IHttpContextAccessor>(accessor);
            services.AddSingleton(Journals.Object);
            services.AddSingleton(Ownership.Object);
            services.AddSingleton(Monitor.Object);
            services.AddSingleton(Authorization.Object);
            _services = services.BuildServiceProvider();
            Http.RequestServices = _services;
            Http.Items[LoginSessionMiddleware.CurrentUserKey] = Actor;
            Http.Items[LoginSessionMiddleware.CurrentTenantIdKey] = Tenant;
            Http.Items[LoginSessionMiddleware.CurrentUserCompanyIdKey] = Company;
            Http.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] = permissions;
            Structure.Setup(x => x.GetNodeTenantsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(
                new FundStructureTenantMap(true, new Dictionary<Guid, string> { [EntityId] = Tenant, [CounterpartyId] = Tenant }));
            Authorization.Setup(x => x.AuthorizeManyAsync(Actor, It.IsAny<UserPermission>(), AccessScopeKindDto.LegalEntity,
                It.IsAny<IReadOnlyCollection<Guid>>(), permissions, It.IsAny<CancellationToken>()))
                .ReturnsAsync((string actor, UserPermission permission, AccessScopeKindDto kind,
                    IReadOnlyCollection<Guid> entities, UserPermission _, CancellationToken _) =>
                    (IReadOnlyDictionary<Guid, ScopedAuthorizationDecisionDto>)entities.ToDictionary(id => id,
                        id => new ScopedAuthorizationDecisionDto(Grant(permission, id), actor, permission, kind, id, "Synthetic scoped decision")));
            Access = new(accessor, Structure.Object);
            Adapter = new(_services);
        }

        public void UseActualScopedAuthority()
        {
            Journals.Setup(x => x.GetLedgerBookAsync(Book.LedgerBookId, It.IsAny<CancellationToken>())).ReturnsAsync(Book);
            Journals.Setup(x => x.GetPeriodAsync(Period.PeriodId, It.IsAny<CancellationToken>())).ReturnsAsync(Period);
            Ownership.Setup(x => x.ResolveAsync(Book.FundProfileId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new FundProfileOwnership(Book.FundProfileId, Tenant, Company));
            Monitor.Setup(x => x.EvaluateAsync(It.IsAny<IntercompanyAgreementCallContext>(), Company,
                It.IsAny<IntercompanyObligationMonitorRequestDto>(), It.IsAny<CancellationToken>()))
                .Returns(async (IntercompanyAgreementCallContext context, string? company,
                    IntercompanyObligationMonitorRequestDto request, CancellationToken ct) =>
                {
                    CapturedContext = context;
                    CapturedCompany = company;
                    CapturedRequest = request;
                    var allowed = await Access.CanAccessEntitiesAsync(context, [EntityId, CounterpartyId], ct);
                    return allowed ? new IntercompanyObligationMonitorDto(request.AsOf, 30, DateTimeOffset.UtcNow, "Current", true,
                        "synthetic-source", [], [new("synthetic.expiring", "Warning", "Sensitive accounting-derived agreement finding")],
                        new(Tenant, Company, Book.FundProfileId, Book.LedgerBookId, request.AsOf, true, [], [], "source"), [])
                        : new(request.AsOf, 30, DateTimeOffset.UtcNow, "Unavailable", false, "", [], [], null, []);
                });
        }
        public void Dispose() => _services.Dispose();
    }
}

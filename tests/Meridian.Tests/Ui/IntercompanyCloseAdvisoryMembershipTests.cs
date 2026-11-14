using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Services;
using Meridian.Contracts.Tenancy;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.Intercompany;
using Meridian.Identity;
using Meridian.Identity.Auth;
using Meridian.PortfolioRecords.Accounts;
using Meridian.Storage.FundStructure;
using Meridian.Storage.Ledger;
using Meridian.Ui.Shared.Endpoints;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace Meridian.Tests.Ui;

public sealed class IntercompanyCloseAdvisoryMembershipTests
{
    [Theory]
    [InlineData(FundStructureNodeKindDto.Entity)]
    [InlineData(FundStructureNodeKindDto.Account)]
    [InlineData(FundStructureNodeKindDto.Fund)]
    [InlineData(FundStructureNodeKindDto.Sleeve)]
    [InlineData(FundStructureNodeKindDto.Vehicle)]
    public async Task RetainedBookMembershipPassesLegalEntityToNativeMonitor(FundStructureNodeKindDto kind)
    {
        using var fixture = new Fixture(kind);
        var result = await fixture.RunAsync();

        Assert.Equal("Partial", result.Status); // No reviewed lifecycle baseline is invented.
        Assert.Contains(result.Findings, x => x.Code == "intercompany.lifecycle.unknown" && x.EntityId == fixture.EntityId);
        Assert.Equal(fixture.EntityId, fixture.CapturedRequest!.EntityId);
        Assert.Equal(fixture.Book.LedgerBookId, fixture.CapturedRequest.LedgerBookId);
        Assert.Equal(fixture.Period.EndDate, result.AsOf);
        Assert.Null(result.BalanceSchedule);
        Assert.Equal(30, result.ExpiryHorizonDays);
        Assert.Contains(result.SourceVersions, x => x.SourceKind == "ledger-book" && x.SourceId == fixture.Book.LedgerBookId.ToString("D"));
        fixture.Agreements.Verify(x => x.ListAsync(new(Fixture.Tenant, Fixture.Actor),
            It.Is<IntercompanyAgreementQueryDto>(q => q.EntityId == fixture.EntityId && q.AsOf == fixture.Period.EndDate),
            It.IsAny<CancellationToken>()), Times.Exactly(2));
        fixture.Accounts.Verify(x => x.GetAccountAsync(fixture.Account.AccountId, It.IsAny<CancellationToken>()),
            kind == FundStructureNodeKindDto.Entity ? Times.Never() : Times.Once());
        Assert.False(fixture.Http.Items.ContainsKey(HttpContextIntercompanyAgreementAccessAuthority.RequiredPermissionKey));
    }

    [Theory]
    [InlineData(FundStructureNodeKindDto.Account)]
    [InlineData(FundStructureNodeKindDto.Fund)]
    [InlineData(FundStructureNodeKindDto.Sleeve)]
    [InlineData(FundStructureNodeKindDto.Vehicle)]
    public async Task UnrelatedRetainedAccountMembershipCannotAuthorizeBook(FundStructureNodeKindDto kind)
    {
        using var fixture = new Fixture(kind);
        fixture.Account = fixture.Account with
        {
            AccountId = Guid.NewGuid(), FundId = Guid.NewGuid(), SleeveId = Guid.NewGuid(), VehicleId = Guid.NewGuid()
        };
        AssertUnavailable(await fixture.RunAsync());
        fixture.Monitor.VerifyNoOtherCalls();
        fixture.Schedules.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("owner-tenant")]
    [InlineData("owner-company")]
    [InlineData("caller-tenant")]
    [InlineData("caller-company")]
    [InlineData("fund")]
    [InlineData("period-book")]
    [InlineData("entity")]
    public async Task ForgedScopeOrOwnershipIsUnavailableBeforeMonitoring(string defect)
    {
        using var fixture = new Fixture(FundStructureNodeKindDto.Sleeve);
        switch (defect)
        {
            case "owner-tenant": fixture.Owner = fixture.Owner with { TenantId = "other-tenant" }; break;
            case "owner-company": fixture.Owner = fixture.Owner with { CompanyId = "other-company" }; break;
            case "caller-tenant": fixture.Http.Items[LoginSessionMiddleware.CurrentTenantIdKey] = "other-tenant"; break;
            case "caller-company": fixture.Http.Items[LoginSessionMiddleware.CurrentUserCompanyIdKey] = "other-company"; break;
            case "fund": fixture.Scope = fixture.Scope with { FundProfileId = "other-fund" }; break;
            case "period-book": fixture.Period = fixture.Period with { LedgerBookId = Guid.NewGuid() }; break;
            case "entity": fixture.Scope = fixture.Scope with { EntityId = Guid.NewGuid().ToString("D") }; break;
        }
        AssertUnavailable(await fixture.RunAsync());
        fixture.Monitor.VerifyNoOtherCalls();
        fixture.Schedules.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("inactive")]
    [InlineData("expired")]
    [InlineData("missing-account")]
    [InlineData("missing-book")]
    [InlineData("missing-account-selection")]
    [InlineData("missing-resolver")]
    [InlineData("subject-scope")]
    [InlineData("subject-status")]
    [InlineData("subject-version")]
    public async Task MissingOrIncompleteMembershipAuthorityIsUnavailable(string defect)
    {
        using var fixture = new Fixture(FundStructureNodeKindDto.Vehicle, registerSubject: defect != "missing-resolver");
        switch (defect)
        {
            case "inactive": fixture.Account = fixture.Account with { IsActive = false }; break;
            case "expired": fixture.Account = fixture.Account with { EffectiveTo = DateTimeOffset.UtcNow.AddDays(-1) }; break;
            case "missing-account": fixture.MissingAccount = true; break;
            case "missing-book": fixture.MissingSubjectBook = true; break;
            case "missing-account-selection": fixture.Scope = fixture.Scope with { FundAccountId = null }; break;
            case "subject-scope": fixture.TransformSubject = x => x with { Scope = x.Scope with { EntityId = Guid.NewGuid().ToString("D") } }; break;
            case "subject-status": fixture.TransformSubject = x => x with { Status = "Unavailable" }; break;
            case "subject-version": fixture.TransformSubject = x => x with { EvidenceVersion = "" }; break;
        }
        AssertUnavailable(await fixture.RunAsync());
        fixture.Monitor.VerifyNoOtherCalls();
        fixture.Schedules.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("missing-entity-book")]
    [InlineData("denied-reporting-grant")]
    [InlineData("entity-other-tenant")]
    [InlineData("unpartitioned-authority")]
    public async Task ValidMembershipCannotReplaceNativeEntityAuthority(string defect)
    {
        using var fixture = new Fixture(FundStructureNodeKindDto.Account);
        switch (defect)
        {
            case "missing-entity-book": fixture.EntityBookAvailable = false; break;
            case "denied-reporting-grant": fixture.EntityReportAllowed = false; break;
            case "entity-other-tenant": fixture.EntityTenant = "other-tenant"; break;
            case "unpartitioned-authority": fixture.Partitioned = false; break;
        }
        AssertUnavailable(await fixture.RunAsync());
        Assert.NotNull(fixture.CapturedRequest);
        fixture.Agreements.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(FundStructureNodeKindDto.Organization)]
    [InlineData(FundStructureNodeKindDto.Entity)]
    public async Task UnsupportedOrDifferentEntityBookCannotBorrowAccountMembership(FundStructureNodeKindDto kind)
    {
        using var fixture = new Fixture(kind);
        fixture.Scope = fixture.Scope with { EntityId = Guid.NewGuid().ToString("D") };
        AssertUnavailable(await fixture.RunAsync());
        fixture.Monitor.VerifyNoOtherCalls();
        fixture.Accounts.VerifyNoOtherCalls();
    }

    private static void AssertUnavailable(IntercompanyObligationMonitorDto result)
    {
        Assert.Equal("Unavailable", result.Status);
        Assert.False(result.IsComplete);
        Assert.Null(result.BalanceSchedule);
        Assert.Empty(result.Guarantees);
        Assert.Empty(result.SourceVersions);
        Assert.DoesNotContain(result.Findings, x => x.Message.Contains("other-", StringComparison.Ordinal));
    }

    private sealed class Fixture : IDisposable
    {
        public const string Tenant = "tenant-a";
        public const string Company = "company-a";
        public const string Actor = "operator-a";
        public Guid EntityId { get; } = Guid.NewGuid();
        public AccountSummaryDto Account { get; set; }
        public LedgerBookRecord Book { get; }
        public LedgerAccountingPeriod Period { get; set; }
        public FundProfileOwnership Owner { get; set; }
        public CloseReadinessScopeDto Scope { get; set; }
        public bool MissingAccount { get; set; }
        public bool MissingSubjectBook { get; set; }
        public bool EntityBookAvailable { get; set; } = true;
        public bool EntityReportAllowed { get; set; } = true;
        public string EntityTenant { get; set; } = Tenant;
        public bool Partitioned { get; set; } = true;
        public Func<CloseReadinessSubjectDto, CloseReadinessSubjectDto> TransformSubject { get; set; } = x => x;
        public DefaultHttpContext Http { get; } = new();
        public Mock<IAccountQueryService> Accounts { get; } = new(MockBehavior.Strict);
        public Mock<IIntercompanyBalanceScheduleSource> Schedules { get; } = new(MockBehavior.Strict);
        public Mock<IIntercompanyAgreementService> Agreements { get; } = new(MockBehavior.Strict);
        public Mock<IIntercompanyObligationMonitorService> Monitor { get; } = new(MockBehavior.Strict);
        public IntercompanyObligationMonitorRequestDto? CapturedRequest { get; private set; }
        private readonly ServiceProvider _services;
        private readonly IntercompanyCloseAdvisorySource _adapter;

        public Fixture(FundStructureNodeKindDto kind, bool registerSubject = true)
        {
            var now = DateTimeOffset.UtcNow;
            Account = new(Guid.NewGuid(), default, EntityId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
                "SYNTHETIC", "Synthetic account", "USD", "Synthetic custodian", true, now.AddYears(-1), null, null, null, null, null);
            var accountId = Account.AccountId;
            var nodeId = kind switch
            {
                FundStructureNodeKindDto.Entity => EntityId,
                FundStructureNodeKindDto.Account => accountId,
                FundStructureNodeKindDto.Fund => Account.FundId!.Value,
                FundStructureNodeKindDto.Sleeve => Account.SleeveId!.Value,
                FundStructureNodeKindDto.Vehicle => Account.VehicleId!.Value,
                _ => Guid.NewGuid()
            };
            Book = new(Guid.NewGuid(), "fund-a", nodeId, kind, "Synthetic primary book", "USD", now, now);
            Period = new(Guid.NewGuid(), Book.LedgerBookId, 2026, 11, "November", new(2026, 11, 1), new(2026, 11, 30), "Open", now, null, 1);
            Owner = new(Book.FundProfileId, Tenant, Company);
            Scope = new(Book.FundProfileId, Book.LedgerBookId, accountId, EntityId.ToString("D"), Period.PeriodId.ToString("D"));
            var journals = new Mock<ILedgerJournalStore>(MockBehavior.Strict);
            journals.Setup(x => x.GetLedgerBookAsync(Book.LedgerBookId, It.IsAny<CancellationToken>())).ReturnsAsync(Book);
            journals.Setup(x => x.GetPeriodAsync(Period.PeriodId, It.IsAny<CancellationToken>())).ReturnsAsync(() => Period);
            var entityBook = Book with { LedgerBookId = Guid.NewGuid(), FundStructureNodeId = EntityId, FundStructureNodeKind = FundStructureNodeKindDto.Entity };
            journals.Setup(x => x.ListLedgerBooksAsync(Book.FundProfileId, null, FundStructureNodeKindDto.Entity, It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => EntityBookAvailable ? [entityBook] : []);
            var ownership = new Mock<IFundProfileTenancyRegistry>(MockBehavior.Strict);
            ownership.Setup(x => x.ResolveAsync(Book.FundProfileId, It.IsAny<CancellationToken>())).ReturnsAsync(() => Owner);
            var books = new Mock<ILedgerBookService>(MockBehavior.Strict);
            books.Setup(x => x.GetBookAsync(Book.LedgerBookId, It.IsAny<CancellationToken>())).ReturnsAsync(() => MissingSubjectBook ? null :
                new LedgerBookDto(Book.LedgerBookId, Book.FundProfileId, nodeId, kind, Book.DisplayName, Book.BaseCurrency, now, now));
            Accounts.Setup(x => x.GetAccountAsync(accountId, It.IsAny<CancellationToken>())).ReturnsAsync(() => MissingAccount ? null : Account);
            var nativeSubjects = new CloseReadinessSubjectSource(books.Object, Accounts.Object);
            var subjects = new Mock<ICloseReadinessSubjectSource>(MockBehavior.Strict);
            subjects.Setup(x => x.GetSubjectAsync(It.IsAny<CloseReadinessScopeDto>(), It.IsAny<CancellationToken>()))
                .Returns(async (CloseReadinessScopeDto scope, CancellationToken ct) =>
                {
                    var subject = await nativeSubjects.GetSubjectAsync(scope, ct);
                    return subject is null ? null : TransformSubject(subject);
                });
            var structure = new Mock<IFundStructureStore>(MockBehavior.Strict);
            structure.Setup(x => x.GetNodeTenantsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(() =>
                new FundStructureTenantMap(Partitioned, new Dictionary<Guid, string> { [EntityId] = EntityTenant }));
            var authorization = new Mock<IScopedAuthorizationService>(MockBehavior.Strict);
            authorization.Setup(x => x.AuthorizeManyAsync(Actor, UserPermission.ViewLedgerReports, AccessScopeKindDto.LegalEntity,
                It.IsAny<IReadOnlyCollection<Guid>>(), UserPermission.ViewLedgerReports, It.IsAny<CancellationToken>()))
                .ReturnsAsync((string actor, UserPermission permission, AccessScopeKindDto scope, IReadOnlyCollection<Guid> ids,
                    UserPermission _, CancellationToken _) => (IReadOnlyDictionary<Guid, ScopedAuthorizationDecisionDto>)ids.ToDictionary(id => id,
                        id => new ScopedAuthorizationDecisionDto(EntityReportAllowed && id == EntityId, actor, permission, scope, id, "Synthetic reporting decision")));
            var accessor = new HttpContextAccessor { HttpContext = Http };
            var access = new HttpContextIntercompanyAgreementAccessAuthority(accessor, structure.Object);
            Schedules.Setup(x => x.GetAsync(Tenant, Company, Book.FundProfileId, Book.LedgerBookId, Period.EndDate, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new IntercompanyBalanceScheduleDto(Tenant, Company, Book.FundProfileId, Book.LedgerBookId, Period.EndDate, true, [], [], "synthetic-source"));
            Agreements.Setup(x => x.ListAsync(new(Tenant, Actor), It.IsAny<IntercompanyAgreementQueryDto>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new IntercompanyAgreementListDto([], null));
            var lifecycle = new Mock<IEntityLifecycleService>(MockBehavior.Strict);
            lifecycle.Setup(x => x.GetAsync(EntityId, null, It.IsAny<CancellationToken>())).ReturnsAsync((EntityLifecycleRevisionDto?)null);
            var nativeMonitor = new IntercompanyObligationMonitorService(Schedules.Object, Agreements.Object, lifecycle.Object, journals.Object, access);
            Monitor.Setup(x => x.EvaluateAsync(new(Tenant, Actor), Company, It.IsAny<IntercompanyObligationMonitorRequestDto>(), It.IsAny<CancellationToken>()))
                .Returns((IntercompanyAgreementCallContext context, string? company, IntercompanyObligationMonitorRequestDto request, CancellationToken ct) =>
                {
                    CapturedRequest = request;
                    return nativeMonitor.EvaluateAsync(context, company, request, ct);
                });
            var services = new ServiceCollection();
            services.AddSingleton<IHttpContextAccessor>(accessor);
            services.AddSingleton(journals.Object);
            services.AddSingleton(ownership.Object);
            services.AddSingleton(Monitor.Object);
            services.AddSingleton(authorization.Object);
            if (registerSubject)
                services.AddSingleton(subjects.Object);
            _services = services.BuildServiceProvider();
            Http.RequestServices = _services;
            Http.Items[LoginSessionMiddleware.CurrentUserKey] = Actor;
            Http.Items[LoginSessionMiddleware.CurrentTenantIdKey] = Tenant;
            Http.Items[LoginSessionMiddleware.CurrentUserCompanyIdKey] = Company;
            Http.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] = UserPermission.ViewLedgerReports;
            _adapter = new(_services);
        }

        public Task<IntercompanyObligationMonitorDto> RunAsync() => _adapter.GetAsync(Scope, Tenant, Company);
        public void Dispose() => _services.Dispose();
    }
}

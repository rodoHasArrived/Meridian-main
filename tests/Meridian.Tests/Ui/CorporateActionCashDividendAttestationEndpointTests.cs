using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using FluentAssertions;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;
using Meridian.Identity.Auth;
using Meridian.Storage.SecurityMaster;
using Meridian.Ui.Shared.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace Meridian.Tests.Ui;

public sealed class CorporateActionCashDividendAttestationEndpointTests
{
    private static readonly Guid CaseId = Guid.Parse("c0140000-0000-4000-8000-000000000001");
    private static readonly Guid BookId = Guid.Parse("c0140000-0000-4000-8000-000000000002");
    private static readonly Guid ProjectionId = Guid.Parse("c0140000-0000-4000-8000-000000000003");
    private static readonly Guid AttestationId = Guid.Parse("c0140000-0000-8000-8000-000000000004");
    private static readonly Guid DeclarationId = Guid.Parse("c0140000-0000-4000-8000-000000000005");
    private const UserPermission Read = UserPermission.ViewSecurityMaster | UserPermission.ViewCorporateActions;
    private const UserPermission All = Read | UserPermission.RecordPreparerAttestations
        | UserPermission.ApproveCorporateActionAccounting | UserPermission.PostCorporateActionAccounting
        | UserPermission.ManageSinglePrincipalDeclarations | UserPermission.ManageLedgerReports;
    private static readonly string Route = $"/api/security-master/corporate-actions/cases/{CaseId:D}/cash-dividend-attestation";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    [Fact]
    public async Task LeanAttestation_StampsTrustedScopeActorAndCase_AndRetainsConfirmedOperands()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Permissions = Read | UserPermission.RecordPreparerAttestations | UserPermission.ApproveCorporateActionAccounting;

        (await fixture.Client.PostAsJsonAsync(Route + "/attest", AttestBody(), Json)).EnsureSuccessStatusCode();

        fixture.AttestCommand.Should().NotBeNull();
        var command = fixture.AttestCommand!;
        command.CaseId.Should().Be(CaseId);
        command.TenantId.Should().Be("tenant-a");
        command.CompanyId.Should().Be("company-a");
        command.Actor.Should().Be("principal-a");
        command.ScopeAssertion.Should().Be(fixture.Case.Scope);
        command.ExpectedVersion.Should().Be(7);
        command.ExpectedDeclarationRevision.Should().Be(3);
        command.ExpectedEligibilityHash.Should().Be(new string('a', 64));
        command.Authority.Should().Be(new CorporateActionCashDividendDecisionAuthorityDto(true, false));
        fixture.PostCommand.Should().BeNull();
    }

    [Fact]
    public async Task LeanPosting_UsesSeparatePostingGrant_AndDoesNotCreateAttestationAuthority()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Permissions = Read | UserPermission.PostCorporateActionAccounting;

        (await fixture.Client.PostAsJsonAsync(Route + "/post", PostBody(), Json)).EnsureSuccessStatusCode();

        fixture.PostCommand!.CaseId.Should().Be(CaseId);
        fixture.PostCommand.Actor.Should().Be("principal-a");
        fixture.PostCommand.ScopeAssertion.Should().Be(fixture.Case.Scope);
        fixture.PostCommand.Authority.Should().Be(new CorporateActionCashDividendDecisionAuthorityDto(false, true));
        fixture.AttestCommand.Should().BeNull();
    }

    [Theory]
    [InlineData("tenantId", "foreign-tenant", HttpStatusCode.Forbidden)]
    [InlineData("companyId", "foreign-company", HttpStatusCode.Forbidden)]
    [InlineData("actor", "forged-principal", HttpStatusCode.Forbidden)]
    [InlineData("caseId", "c0140000-0000-4000-8000-000000000099", HttpStatusCode.UnprocessableEntity)]
    public async Task SuppliedIdentityMismatches_AreRejectedBeforeAttestationService(
        string field, string value, HttpStatusCode status)
    {
        await using var fixture = await Fixture.CreateAsync();
        var body = AttestBody();
        body[field] = value;

        (await fixture.Client.PostAsJsonAsync(Route + "/attest", body, Json)).StatusCode.Should().Be(status);

        fixture.AttestCommand.Should().BeNull();
    }

    [Fact]
    public async Task SuppliedWrongBookScope_AndUnknownScopedCase_AreRejected()
    {
        await using var fixture = await Fixture.CreateAsync();
        var body = PostBody();
        body["scopeAssertion"] = fixture.Case.Scope with { LedgerBookId = Guid.NewGuid().ToString("D") };
        (await fixture.Client.PostAsJsonAsync(Route + "/post", body, Json)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var unknown = Route.Replace(CaseId.ToString("D"), Guid.NewGuid().ToString("D"), StringComparison.Ordinal);
        (await fixture.Client.PostAsJsonAsync(unknown + "/post", PostBody(), Json)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        fixture.PostCommand.Should().BeNull();
    }

    [Fact]
    public async Task BodyAuthorityCannotGrantPermissions_AndHistoricalCommandsReachTypedServiceWithReadOnlyAuthority()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Permissions = Read;
        var body = AttestBody();
        body["authority"] = new CorporateActionCashDividendDecisionAuthorityDto(true, true);
        (await fixture.Client.PostAsJsonAsync(Route + "/attest", body, Json)).EnsureSuccessStatusCode();
        fixture.AttestCommand!.Authority.Should().Be(new CorporateActionCashDividendDecisionAuthorityDto(false, false));
        var post = PostBody();
        post["authority"] = new CorporateActionCashDividendDecisionAuthorityDto(true, true);
        (await fixture.Client.PostAsJsonAsync(Route + "/post", post, Json)).EnsureSuccessStatusCode();
        fixture.PostCommand!.Authority.Should().Be(new CorporateActionCashDividendDecisionAuthorityDto(false, false));
        // The real typed service permits only exact retained replay with these false grants.
    }

    [Theory]
    [InlineData(UserPermission.RecordPreparerAttestations)]
    [InlineData(UserPermission.ApproveCorporateActionAccounting)]
    [InlineData(UserPermission.PostCorporateActionAccounting)]
    public async Task AttestationGrantRequiresBothExplicitAttestationAndExistingAccountingApproval(UserPermission grant)
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Permissions = Read | grant;
        (await fixture.Client.PostAsJsonAsync(Route + "/attest", AttestBody(), Json)).EnsureSuccessStatusCode();
        fixture.AttestCommand!.Authority!.CanAttestAccounting.Should().BeFalse();
    }

    [Theory]
    [InlineData("attest")]
    [InlineData("post")]
    [InlineData("declaration")]
    public async Task AutomationOriginIsRefusedEvenWithHumanSessionAndAllGrants(string command)
    {
        await using var fixture = await Fixture.CreateAsync();
        var body = command == "declaration" ? DeclarationBody() : command == "post" ? PostBody() : AttestBody();
        body["authority"] = command == "declaration"
            ? new CashDividendDeclarationAuthorityDto(true, OperationsActionOriginDto.AssistantDraft, true)
            : new CorporateActionCashDividendDecisionAuthorityDto(true, true, OperationsActionOriginDto.AssistantDraft);
        (await fixture.Client.PostAsJsonAsync(Route + "/" + command, body, Json)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        fixture.AttestCommand.Should().BeNull();
        fixture.PostCommand.Should().BeNull();
        fixture.DeclarationCommand.Should().BeNull();
    }

    [Fact]
    public async Task DeclarationEnable_StampsBookAndValidatesExplicitBindingAgainstAuthoritativeNativePreview()
    {
        await using var fixture = await Fixture.CreateAsync();
        (await fixture.Client.PostAsJsonAsync(Route + "/declaration", DeclarationBody(), Json)).EnsureSuccessStatusCode();
        fixture.DeclarationCommand!.LedgerBookId.Should().Be(BookId);
        fixture.DeclarationCommand.Actor.Should().Be("principal-a");
        fixture.DeclarationCommand.TenantId.Should().Be("tenant-a");
        fixture.DeclarationCommand.AccountRoleBinding.Should().Be(Binding());
        fixture.DeclarationCommand.Authority.Should().Be(new CashDividendDeclarationAuthorityDto(true,
            OperationsActionOriginDto.HumanOperator, true));
        fixture.Service.Verify(service => service.PreviewCashDividendAsync(CaseId, "tenant-a", "company-a", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeclarationEnable_RejectsWrongBookMissingBindingAndChangedNativeBinding()
    {
        await using var fixture = await Fixture.CreateAsync();
        var wrongBook = DeclarationBody();
        wrongBook["ledgerBookId"] = Guid.NewGuid();
        (await fixture.Client.PostAsJsonAsync(Route + "/declaration", wrongBook, Json)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var missing = DeclarationBody();
        missing.Remove("accountRoleBinding");
        (await fixture.Client.PostAsJsonAsync(Route + "/declaration", missing, Json)).IsSuccessStatusCode.Should().BeFalse();
        var changed = DeclarationBody();
        changed["accountRoleBinding"] = Binding() with { DividendReceivableAccountName = "Unverified cash" };
        (await fixture.Client.PostAsJsonAsync(Route + "/declaration", changed, Json)).IsSuccessStatusCode.Should().BeFalse();
        fixture.DeclarationCommand.Should().BeNull();
    }

    [Fact]
    public async Task DeclarationEnable_RequiresSeparateFinancialBindingGrant_WhileWithdrawalPreservesBindingWithAdminAlone()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Permissions = Read | UserPermission.ManageSinglePrincipalDeclarations;
        (await fixture.Client.PostAsJsonAsync(Route + "/declaration", DeclarationBody(), Json)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var withdrawal = DeclarationBody();
        withdrawal["enabled"] = false;
        withdrawal.Remove("accountRoleBinding");
        fixture.PolicyEnabled = false;
        (await fixture.Client.PostAsJsonAsync(Route + "/declaration", withdrawal, Json)).EnsureSuccessStatusCode();
        fixture.DeclarationCommand!.Enabled.Should().BeFalse();
        fixture.DeclarationCommand.AccountRoleBinding.Should().BeNull("native store preserves the original binding for an omitted withdrawal binding");
        fixture.DeclarationCommand.Authority!.CanBindAccountingRoles.Should().BeFalse();
        fixture.Service.Verify(service => service.PreviewCashDividendAsync(It.IsAny<Guid>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeclarationReplay_PrecedesMutableFinancialBindingChecks()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Permissions = Read | UserPermission.ManageSinglePrincipalDeclarations;
        fixture.DeclarationReplay = new(Declaration(), true);
        (await fixture.Client.PostAsJsonAsync(Route + "/declaration", DeclarationBody(), Json)).EnsureSuccessStatusCode();
        fixture.DeclarationCommand.Should().BeNull();
        fixture.Service.Verify(service => service.PreviewCashDividendAsync(It.IsAny<Guid>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Workspace_CanPostRequiresValidPreviewAndAttestedState_AndPersistenceRemainsAvailableWhenFlagOff()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Attestation = Attestation();
        fixture.Case = fixture.Case with { State = CorporateActionCaseStates.PreparerAttested };
        var available = await fixture.WorkspaceAsync();
        available.CanPostAttestedAccounting.Should().BeTrue();
        available.CanBindAccountingRoles.Should().BeTrue();
        fixture.PreviewError = new CorporateActionOperationException(CorporateActionProblemCodes.PeriodLocked, "The source period is closed.");
        (await fixture.WorkspaceAsync()).CanPostAttestedAccounting.Should().BeFalse();
        fixture.PreviewError = null;
        fixture.Case = fixture.Case with { State = CorporateActionCaseStates.ReadyForApproval };
        (await fixture.WorkspaceAsync()).CanPostAttestedAccounting.Should().BeFalse();
        fixture.PolicyEnabled = false;
        fixture.Permissions = Read;
        var disabled = await fixture.WorkspaceAsync();
        disabled.Availability.Should().Be("Available");
        disabled.PolicyEnabled.Should().BeFalse();
        disabled.CanAttestAccounting.Should().BeFalse();
        disabled.CanPostAttestedAccounting.Should().BeFalse();
        disabled.CanBindAccountingRoles.Should().BeFalse();
    }

    [Fact]
    public async Task EveryCommandRequiresInteractiveReaderAndAuthenticatedCompanyScope()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.IsApiKey = true;
        (await fixture.Client.GetAsync(Route)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        fixture.IsApiKey = false;
        fixture.Permissions = UserPermission.PostCorporateActionAccounting;
        (await fixture.Client.PostAsJsonAsync(Route + "/post", PostBody(), Json)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        fixture.Permissions = All;
        fixture.CompanyId = null;
        (await fixture.Client.GetAsync(Route)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        fixture.PostCommand.Should().BeNull();
    }

    [Fact]
    public async Task RetainedHistoryAndWithdrawalRemainAvailableWhenReadinessCompositionIsUnavailable()
    {
        await using var fixture = await Fixture.CreateAsync(registerService: false);
        fixture.PolicyEnabled = false;
        fixture.Permissions = Read | UserPermission.ManageSinglePrincipalDeclarations;
        var workspace = await fixture.WorkspaceAsync();
        workspace.Availability.Should().Be("Available");
        workspace.Declaration.Should().NotBeNull();
        workspace.Preview.Should().BeNull();
        var withdrawal = DeclarationBody();
        withdrawal["enabled"] = false;
        withdrawal.Remove("accountRoleBinding");
        (await fixture.Client.PostAsJsonAsync(Route + "/declaration", withdrawal, Json)).EnsureSuccessStatusCode();
        fixture.DeclarationCommand!.Enabled.Should().BeFalse();
    }

    private static Dictionary<string, object?> AttestBody() => new()
    {
        ["expectedVersion"] = 7, ["idempotencyKey"] = "synthetic-attest-v7", ["projectionId"] = ProjectionId,
        ["expectedDeclarationRevision"] = 3, ["expectedEligibilityHash"] = new string('a', 64),
        ["reason"] = "Inspected the complete synthetic book entitlement", ["evidenceReference"] = "cash-dividend-eligibility:" + new string('a', 64),
        ["evidenceHash"] = new string('a', 64), ["confirmation"] = CorporateActionCashDividendAttestationPolicy.Confirmation
    };

    private static Dictionary<string, object?> PostBody() => new()
    {
        ["expectedVersion"] = 8, ["idempotencyKey"] = "synthetic-post-v8", ["projectionId"] = ProjectionId,
        ["attestationId"] = AttestationId, ["reason"] = "Post the exact retained attested declaration"
    };

    private static Dictionary<string, object?> DeclarationBody() => new()
    {
        ["commandId"] = Guid.NewGuid(), ["expectedRevision"] = 3, ["enabled"] = true,
        ["policyVersion"] = CorporateActionCashDividendAttestationPolicy.Version,
        ["policyHash"] = CorporateActionCashDividendAttestationPolicy.PolicyHash,
        ["reason"] = "Select this synthetic USD book and its existing receivable/income rule",
        ["accountRoleBinding"] = Binding()
    };

    private static CashDividendAccountRoleBindingDto Binding() => new(BookId, "assets:dividend-receivable", "income:dividends",
        "synthetic-pack", "1", "dividend-declaration", "2", DividendReceivableAccountName: "Dividend receivable", DividendIncomeAccountName: "Dividend income");

    private static CashDividendAttestationDeclarationDto Declaration() => new(DeclarationId, 3, "tenant-a", "company-a", BookId,
        true, "principal-a", CorporateActionCashDividendAttestationPolicy.Version, CorporateActionCashDividendAttestationPolicy.PolicyHash,
        "principal-a", DateTimeOffset.UnixEpoch, "Synthetic declaration", Binding());

    private static CorporateActionCashDividendAttestationDto Attestation() => new(AttestationId, CaseId, ProjectionId, 7,
        DeclarationId, 3, CorporateActionCashDividendAttestationPolicy.Version, CorporateActionCashDividendAttestationPolicy.PolicyHash,
        Guid.NewGuid(), "synthetic-related-event", new(2026, 11, 12), "synthetic", "dividend-1", "1", new string('f', 64),
        new string('a', 64), "{}", 100m, 100m, new(2026, 11, 1), "principal-a", DateTimeOffset.UnixEpoch,
        "Synthetic assertion", "cash-dividend-eligibility:" + new string('a', 64), new string('a', 64),
        CorporateActionCashDividendAttestationPolicy.Confirmation, CorporateActionCashDividendAttestationPolicy.Disclosure,
        AccountRoleBinding: Binding());

    private sealed class Fixture : IAsyncDisposable
    {
        public UserPermission Permissions { get; set; } = All;
        public bool PolicyEnabled { get; set; } = true;
        public string? CompanyId { get; set; } = "company-a";
        public bool IsApiKey { get; set; }
        public CorporateActionOperationException? PreviewError { get; set; }
        public CorporateActionCashDividendAttestationDto? Attestation { get; set; }
        public CashDividendAttestationDeclarationResultDto? DeclarationReplay { get; set; }
        public CorporateActionProcessingCaseDto Case { get; set; } = new(CaseId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            new("tenant-a", "company-a", FundProfileId: "fund-a", LedgerBookId: BookId.ToString("D"), PeriodId: "2026-11", FunctionalCurrency: "USD"),
            CorporateActionCaseStates.ReadyForApproval, 7, null, null, null, "principal-a", DateTimeOffset.UnixEpoch, "principal-a", DateTimeOffset.UnixEpoch);
        public Mock<ICorporateActionCashDividendAccountingService> Service { get; } = new();
        public AttestCorporateActionCashDividendRequestDto? AttestCommand { get; set; }
        public PostAttestedCorporateActionCashDividendRequestDto? PostCommand { get; set; }
        public ChangeCashDividendAttestationDeclarationRequestDto? DeclarationCommand { get; set; }
        public WebApplication App { get; private set; } = null!;
        public HttpClient Client { get; private set; } = null!;

        public static async Task<Fixture> CreateAsync(bool registerService = true)
        {
            var fixture = new Fixture();
            var cases = new Mock<ICorporateActionOperationsStore>();
            cases.Setup(store => store.GetCaseAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Guid caseId, string tenantId, string companyId, CancellationToken _) =>
                    caseId == CaseId && tenantId == "tenant-a" && companyId == "company-a" ? fixture.Case : null);
            var native = new Mock<ICorporateActionCashDividendAccountingStore>();
            native.Setup(store => store.GetCashDividendDeclarationStateAsync("tenant-a", "company-a", It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => new("tenant-a", "company-a", fixture.PolicyEnabled,
                    CorporateActionCashDividendAttestationPolicy.Version, CorporateActionCashDividendAttestationPolicy.PolicyHash, Declaration(), [Declaration()]));
            native.Setup(store => store.GetCashDividendDeclarationAsync("tenant-a", "company-a", It.IsAny<CancellationToken>())).ReturnsAsync(Declaration());
            native.Setup(store => store.GetCashDividendAttestationAsync(CaseId, "tenant-a", "company-a", It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => fixture.Attestation);
            native.Setup(store => store.GetCashDividendDeclarationReceiptAsync(It.IsAny<ChangeCashDividendAttestationDeclarationRequestDto>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => fixture.DeclarationReplay);
            native.Setup(store => store.ChangeCashDividendDeclarationAsync(It.IsAny<ChangeCashDividendAttestationDeclarationRequestDto>(), It.IsAny<CancellationToken>()))
                .Callback<ChangeCashDividendAttestationDeclarationRequestDto, CancellationToken>((request, _) => fixture.DeclarationCommand = request)
                .ReturnsAsync(new CashDividendAttestationDeclarationResultDto(Declaration(), false));
            fixture.Service.Setup(service => service.PreviewCashDividendAsync(CaseId, "tenant-a", "company-a", It.IsAny<CancellationToken>()))
                .Returns(() => fixture.PreviewError is not null
                    ? Task.FromException<CorporateActionCashDividendPreviewDto>(fixture.PreviewError)
                    : Task.FromResult(fixture.Preview()));
            fixture.Service.Setup(service => service.AttestCashDividendAsync(It.IsAny<AttestCorporateActionCashDividendRequestDto>(), It.IsAny<CancellationToken>()))
                .Callback<AttestCorporateActionCashDividendRequestDto, CancellationToken>((request, _) => fixture.AttestCommand = request)
                .ReturnsAsync((CorporateActionCashDividendAttestationResultDto)null!);
            fixture.Service.Setup(service => service.PostAttestedCashDividendAsync(It.IsAny<PostAttestedCorporateActionCashDividendRequestDto>(), It.IsAny<CancellationToken>()))
                .Callback<PostAttestedCorporateActionCashDividendRequestDto, CancellationToken>((request, _) => fixture.PostCommand = request)
                .ReturnsAsync((CorporateActionCashDividendPostingResultDto)null!);
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();
            builder.Services.AddSingleton(cases.Object);
            builder.Services.AddSingleton(native.Object);
            if (registerService)
                builder.Services.AddSingleton(fixture.Service.Object);
            builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
            builder.Services.AddRateLimiter(options => options.AddPolicy(UiEndpoints.MutationRateLimitPolicy,
                _ => RateLimitPartition.GetNoLimiter("cash-dividend-tests")));
            var app = builder.Build();
            fixture.App = app;
            app.Use(async (context, next) =>
            {
                context.Items[LoginSessionMiddleware.CurrentUserKey] = "principal-a";
                context.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] = fixture.Permissions;
                context.Items[LoginSessionMiddleware.CurrentTenantIdKey] = "tenant-a";
                if (fixture.CompanyId is not null)
                    context.Items[LoginSessionMiddleware.CurrentUserCompanyIdKey] = fixture.CompanyId;
                if (fixture.IsApiKey)
                    context.Items[ApiKeyMiddleware.ApiKeyPrincipalKey] = new object();
                await next();
            });
            app.UseRouting();
            app.UseRateLimiter();
            typeof(SecurityMasterEndpoints).GetMethod("MapCashDividendAttestationEndpoints", BindingFlags.NonPublic | BindingFlags.Static)!
                .Invoke(null, [app.MapGroup(""), Json]);
            await app.StartAsync();
            fixture.Client = app.GetTestClient();
            return fixture;
        }

        private CorporateActionCashDividendPreviewDto Preview()
        {
            var projection = new CorporateActionCaseAccountingProjectionDto(ProjectionId, CaseId, 7, Guid.NewGuid(), 1, 1,
                new string('b', 64), new string('c', 64), "native-posting-key", new string('d', 64), Guid.NewGuid(), 1,
                "synthetic-pack", "1", "dividend-declaration", "2", BookId, Guid.NewGuid(), 1, "Accrual", "fund-a", "USD",
                new(2026, 11, 12), 100m, 100m, Guid.NewGuid(), 1, true, "principal-a", DateTimeOffset.UnixEpoch, true);
            var eligibility = new CorporateActionCashDividendEligibilityDto(Case.SecurityId, new(2026, 11, 12), "synthetic-event",
                "synthetic", "dividend-1", "1", new string('f', 64), 1m, 100m, 100m, new(2026, 11, 1), new string('a', 64), "{}", Binding());
            return new(Case, projection, eligibility, CorporateActionCashDividendAttestationPolicy.Disclosure,
                CorporateActionCashDividendAttestationPolicy.Confirmation);
        }

        public async Task<CorporateActionCashDividendWorkspaceDto> WorkspaceAsync() =>
            (await Client.GetFromJsonAsync<CorporateActionCashDividendWorkspaceDto>(Route, Json))!;

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await App.DisposeAsync();
        }
    }
}

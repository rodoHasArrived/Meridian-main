using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.Ledger;
using Meridian.Identity.Auth;
using Meridian.Storage.Ledger;
using Meridian.Ui.Shared.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace Meridian.Tests.Ui;

public sealed partial class WorkstationEndpointsTests
{
    [Fact]
    public async Task AutomatedGrant_AdminApiKeyCannotClaimDefaultHumanActivationOrRevocation()
    {
        var grants = new Mock<IAutomatedPostingGrantStore>(MockBehavior.Strict);
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(grants.Object);
        await using var app = builder.Build();
        app.Use(async (context, next) =>
        {
            context.Items[LoginSessionMiddleware.CurrentUserKey] = "privileged-api-key";
            context.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] = UserPermission.AdminMaintenance;
            context.Items[LoginSessionMiddleware.CurrentTenantIdKey] = "tenant-alpha";
            context.Items[LoginSessionMiddleware.CurrentUserCompanyIdKey] = "company-alpha";
            context.Items[ApiKeyMiddleware.ApiKeyPrincipalKey] = true;
            await next();
        });
        app.MapLedgerEndpoints(new JsonSerializerOptions(JsonSerializerDefaults.Web));
        await app.StartAsync();
        using var activate = await app.GetTestClient().PostAsJsonAsync("/api/ledger/automation/grants",
            new ActivateAutomatedPostingGrantRequest(Guid.NewGuid(), "account", Guid.NewGuid(), Guid.NewGuid(), "policy", "v1", "mapping"));
        using var revoke = await app.GetTestClient().PostAsJsonAsync($"/api/ledger/automation/grants/{Guid.NewGuid():D}/revoke",
            new RevokeAutomatedPostingGrantRequest(1));
        activate.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        revoke.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        grants.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(OperationsActionOriginDto.AutomationAssistant)]
    [InlineData(OperationsActionOriginDto.AuthorizedSystem)]
    public async Task AutomatedGrant_PrivilegedNonHumanActivationRefusesBeforeAnyStoreCall(OperationsActionOriginDto origin)
    {
        var grants = new Mock<IAutomatedPostingGrantStore>(MockBehavior.Strict);
        await using var app = await CreateAppAsync(services => services.AddSingleton(grants.Object),
            currentUserPermissions: UserPermission.AdminMaintenance, currentUserName: "operator",
            currentUserTenantId: "tenant-alpha", currentUserCompanyId: "company-alpha", mapLedgerApi: true);
        using var response = await app.GetTestClient().PostAsJsonAsync("/api/ledger/automation/grants",
            new ActivateAutomatedPostingGrantRequest(Guid.NewGuid(), "account", Guid.NewGuid(), Guid.NewGuid(),
                "policy", "v1", "mapping", ActionOrigin: origin));
        using var revoke = await app.GetTestClient().PostAsJsonAsync($"/api/ledger/automation/grants/{Guid.NewGuid():D}/revoke",
            new RevokeAutomatedPostingGrantRequest(1, origin));
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        revoke.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        grants.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task AutomatedGrant_ReaderCannotActivateOrRevoke()
    {
        var grants = new Mock<IAutomatedPostingGrantStore>(MockBehavior.Strict);
        await using var app = await CreateAppAsync(services => services.AddSingleton(grants.Object),
            currentUserPermissions: UserPermission.ViewLedgerReports, currentUserName: "reader",
            currentUserTenantId: "tenant-alpha", currentUserCompanyId: "company-alpha", mapLedgerApi: true);
        using var activate = await app.GetTestClient().PostAsJsonAsync("/api/ledger/automation/grants",
            new ActivateAutomatedPostingGrantRequest(Guid.NewGuid(), "account", Guid.NewGuid(), Guid.NewGuid(), "policy", "v1", "mapping"));
        using var revoke = await app.GetTestClient().PostAsJsonAsync($"/api/ledger/automation/grants/{Guid.NewGuid():D}/revoke",
            new RevokeAutomatedPostingGrantRequest(1));
        activate.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        revoke.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        grants.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task AutomatedGrant_ActivationBindsAuthenticatedActorTenantAndCompany()
    {
        var now = DateTimeOffset.UtcNow;
        var book = new LedgerBookRecord(Guid.NewGuid(), "fund", Guid.NewGuid(), FundStructureNodeKindDto.Fund,
            "Synthetic", "USD", now, now, AccountingPolicyId: "policy", AccountingPolicyVersion: "v1");
        var period = new LedgerAccountingPeriod(Guid.NewGuid(), book.LedgerBookId, 2026, 5, "May",
            new(2026, 5, 1), new(2026, 5, 31), "Open", now, null, 1);
        var grantId = Guid.NewGuid();
        var grant = new AutomatedPostingGrantDto(new(grantId, 1, "tenant-alpha", "account", book.LedgerBookId,
            period.PeriodId, "policy", "v1", "mapping", "Synthetic", "company-alpha"), true, "authenticated-admin", now);
        var rules = new[] { "execution.trade-fill", "execution.trade-commission", "execution.trade-fee" }
            .Select(source => new AccountingPolicyRuleDto(source, AccountingTreatmentKindDto.General,
                SourceEventType: source, RequiresApproval: false, AllowsAutoPosting: true)).ToArray();
        var policy = new AccountingPolicyDto("policy", AccountingBasisKindDto.Primary, "v1", "Synthetic",
            period.StartDate, period.EndDate, false, "{}", now,
            RulePack: new AccountingPolicyRulePackDto("synthetic", "v1", rules));
        var expectedPolicyQuery = new AccountingPolicyQuery(book.AccountingBasis, period.StartDate,
            "policy", book.FundProfileId, book.FundStructureNodeId, PolicyVersion: "v1");
        var expectedRequest = new ActivateAutomatedPostingGrantRequest(grantId, "account", book.LedgerBookId,
            period.PeriodId, "policy", "v1", "mapping", CompanyId: "company-alpha");
        var retainedGrant = grant with { PolicySnapshot = policy };
        var journals = new Mock<ILedgerJournalStore>(MockBehavior.Strict);
        journals.Setup(store => store.GetPeriodAsync(period.PeriodId, It.IsAny<CancellationToken>())).ReturnsAsync(period);
        journals.Setup(store => store.GetLedgerBookAsync(book.LedgerBookId, It.IsAny<CancellationToken>())).ReturnsAsync(book);
        var policies = new Mock<IAccountingPolicyService>(MockBehavior.Strict);
        policies.Setup(service => service.AcquireConsolidationAuthorityLeaseAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GrantPolicyLease());
        policies.Setup(service => service.ResolvePolicyAsync(expectedPolicyQuery, It.IsAny<CancellationToken>())).ReturnsAsync(policy);
        var grants = new Mock<IAutomatedPostingGrantStore>(MockBehavior.Strict);
        grants.Setup(store => store.ActivateWithPolicyAsync(expectedRequest, policy,
                "authenticated-admin", "tenant-alpha", It.IsAny<CancellationToken>()))
            .ReturnsAsync(retainedGrant);
        await using var app = await CreateAppAsync(services =>
        {
            services.AddSingleton(journals.Object);
            services.AddSingleton(policies.Object);
            services.AddSingleton(grants.Object);
        }, currentUserPermissions: UserPermission.AdminMaintenance, currentUserName: "authenticated-admin",
            currentUserTenantId: "tenant-alpha", currentUserCompanyId: "company-alpha", mapLedgerApi: true);
        using var response = await app.GetTestClient().PostAsJsonAsync("/api/ledger/automation/grants", new
        {
            grantId,
            financialAccountId = "account",
            ledgerBookId = book.LedgerBookId,
            periodId = period.PeriodId,
            policyId = "policy",
            policyVersion = "v1",
            mappingRevision = "mapping",
            sourceMode = "Synthetic",
            companyId = "forged-company",
            actor = "forged-actor",
            tenantId = "forged-tenant"
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var created = await response.Content.ReadFromJsonAsync<AutomatedPostingGrantDto>(ServerJsonOptions);
        created.Should().NotBeNull();
        created!.ActivatedBy.Should().Be("authenticated-admin");
        created.Reference.TenantId.Should().Be("tenant-alpha");
        created.Reference.CompanyId.Should().Be("company-alpha");
        created.PolicySnapshot.Should().BeEquivalentTo(policy);
        grants.Verify(store => store.ActivateWithPolicyAsync(expectedRequest, policy,
            "authenticated-admin", "tenant-alpha", It.IsAny<CancellationToken>()), Times.Once);
        grants.VerifyNoOtherCalls();
        policies.Verify(service => service.AcquireConsolidationAuthorityLeaseAsync(It.IsAny<CancellationToken>()), Times.Once);
        policies.Verify(service => service.ResolvePolicyAsync(expectedPolicyQuery, It.IsAny<CancellationToken>()), Times.Once);
        policies.VerifyNoOtherCalls();
        journals.Verify(store => store.GetPeriodAsync(period.PeriodId, It.IsAny<CancellationToken>()), Times.Once);
        journals.Verify(store => store.GetLedgerBookAsync(book.LedgerBookId, It.IsAny<CancellationToken>()), Times.Once);
        journals.VerifyNoOtherCalls();
    }

    private sealed class GrantPolicyLease : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
